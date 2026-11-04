using System.Net;
using System.Text.Json;
using CompanyEnrichment.Application;
using CompanyEnrichment.Infrastructure.Statistics;
using CompanyEnrichment.Observability;
using CompanyEnrichment.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using StatBank.Client.Models;

namespace StatBankClient.Tests;

public class StatisticsClientTests
{
    private static (StatisticsClient Client, FakeHttpMessageHandler Handler) CreateClient(
        HttpStatusCode statusCode, string? content)
    {
        var handler = FakeHttpMessageHandler.RespondWith(statusCode, content);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.statbank.dk/v1/") };
        var client = new StatisticsClient(
            httpClient, new StatisticsOptions(), NullLogger<StatisticsClient>.Instance, new DependencyMetrics());
        return (client, handler);
    }

    [Fact]
    public async Task GetIndustryStatisticsAsync_SendsCorrectRequestBody()
    {
        const string csv = "BRANCHE07;TAL;TID;INDHOLD\n620100;ARBSTED;2023;150\n";
        var (client, handler) = CreateClient(HttpStatusCode.OK, csv);

        await client.GetIndustryStatisticsAsync("620100", 2023, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("v1/data", request.RequestUri!.AbsolutePath.TrimStart('/'));

        var body = await request.Content!.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal("ERHV1", root.GetProperty("table").GetString());
        Assert.Equal("CSV", root.GetProperty("format").GetString());

        var variables = root.GetProperty("variables").EnumerateArray().ToList();
        Assert.Contains(variables, v => v.GetProperty("code").GetString() == "BRANCHE07"
            && v.GetProperty("values")[0].GetString() == "620100");
        Assert.Contains(variables, v => v.GetProperty("code").GetString() == "TID"
            && v.GetProperty("values")[0].GetString() == "2023");
    }

    [Fact]
    public async Task GetIndustryStatisticsAsync_ThrowsExternalServiceException_OnServerError()
    {
        var (client, _) = CreateClient(HttpStatusCode.ServiceUnavailable, null);

        await Assert.ThrowsAsync<ExternalServiceException>(
            () => client.GetIndustryStatisticsAsync("620100", 2023, CancellationToken.None));
    }

    [Fact]
    public async Task GetIndustryStatisticsAsync_ReturnsEmpty_WhenProviderRejectsInvalidSelection()
    {
        var (client, _) = CreateClient(HttpStatusCode.BadRequest, "{\"error\":\"invalid variable value\"}");

        var result = await client.GetIndustryStatisticsAsync("999999", 2023, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetIndustryStatisticsAsync_ReturnsEmpty_WhenNoContentReturned()
    {
        var (client, _) = CreateClient(HttpStatusCode.NoContent, null);

        var result = await client.GetIndustryStatisticsAsync("620100", 2023, CancellationToken.None);

        Assert.Empty(result);
    }
}
