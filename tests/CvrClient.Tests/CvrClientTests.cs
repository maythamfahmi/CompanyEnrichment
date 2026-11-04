using System.Net;
using CompanyEnrichment.Application;
using CompanyEnrichment.Observability;
using CompanyEnrichment.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using CvrApiClient = cvrapi.Infrastructure.Cvr.CvrClient;

namespace CvrClient.Tests;

public class CvrClientTests
{
    private static CvrApiClient CreateClient(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://apicvr.dk/api/v1/")
        };
        return new CvrApiClient(httpClient, NullLogger<CvrApiClient>.Instance, new DependencyMetrics());
    }

    [Fact]
    public async Task GetByCvrAsync_ReturnsCompany_WhenProviderReturnsValidJson()
    {
        const string json = """
            {"vat":33000011,"name":"Foo Bar ApS","address":"Testvej 1","zipcode":9999,"city":"Testby","industrycode":999999,"industrydesc":"Test industry","employees":42}
            """;
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var company = await client.GetByCvrAsync("33000011", CancellationToken.None);

        Assert.NotNull(company);
        Assert.Equal("Foo Bar ApS", company!.Name);
        Assert.Equal("999999", company.IndustryCode);
        Assert.Equal(42, company.EmployeeCount);
        Assert.Contains("Testvej 1", company.Address);
    }

    [Fact]
    public async Task GetByCvrAsync_ReturnsNull_WhenProviderReturns404()
    {
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var company = await client.GetByCvrAsync("33000011", CancellationToken.None);

        Assert.Null(company);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("123456789")]
    [InlineData("1234 5678")]
    [InlineData("1234567a")]
    [InlineData("12345678\u200B")]
    [InlineData("\u200B12345678")]
    [InlineData("1234\u200B5678")]
    public async Task GetByCvrAsync_ThrowsInvalidCvrFormatException_AndSkipsHttpCall_WhenCvrIsMalformed(string cvr)
    {
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.OK, "{}");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidCvrFormatException>(() => client.GetByCvrAsync(cvr, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetByCvrAsync_ThrowsExternalServiceException_WhenProviderReturns500()
    {
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.InternalServerError);
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ExternalServiceException>(() => client.GetByCvrAsync("33000011", CancellationToken.None));
    }

    [Fact]
    public async Task GetByCvrAsync_ThrowsInvalidExternalResponseException_WhenBodyIsMalformed()
    {
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.OK, "not json");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidExternalResponseException>(() => client.GetByCvrAsync("33000011", CancellationToken.None));
    }

    [Fact]
    public async Task GetByCvrAsync_ThrowsExternalTimeoutException_WhenRequestTimesOut()
    {
        var handler = FakeHttpMessageHandler.Throwing(new TaskCanceledException("timeout", new TimeoutException()));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<ExternalTimeoutException>(() => client.GetByCvrAsync("33000011", CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SearchByNameAsync_ThrowsInvalidSearchNameException_AndSkipsHttpCall_WhenNameIsEmptyOrWhitespace(string? name)
    {
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.OK, "[]");
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidSearchNameException>(() => client.SearchByNameAsync(name!, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SearchByNameAsync_ReturnsMultipleMatches_WhenProviderReturnsSeveralResults()
    {
        const string json = """
            [{"vat":33000011,"name":"Foo Bar ApS","address":"Testvej 1","zipcode":9999,"city":"Testby","industrycode":999999,"industrydesc":"Test industry","employees":42},{"vat":33000022,"name":"Foo Bar Holding ApS","address":"Testvej 2","zipcode":9999,"city":"Testby","industrycode":999999,"industrydesc":"Test industry","employees":null}]
            """;
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var matches = await client.SearchByNameAsync("Foo Bar", CancellationToken.None);

        Assert.Equal(2, matches.Count);
    }

    [Fact]
    public async Task SearchByNameAsync_ReturnsSingleMatch_WhenProviderReturnsOneResult()
    {
        const string json = """
            [{"vat":33000011,"name":"Foo Bar ApS","address":"Testvej 1","zipcode":9999,"city":"Testby","industrycode":999999,"industrydesc":"Test industry","employees":null}]
            """;
        var handler = FakeHttpMessageHandler.RespondWith(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var matches = await client.SearchByNameAsync("foobar", CancellationToken.None);

        var match = Assert.Single(matches);
        Assert.Equal("Foo Bar ApS", match.Name);
        Assert.Equal("33000011", match.Cvr);
    }
}
