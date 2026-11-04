using System.Net;
using System.Net.Http.Json;
using CompanyEnrichment.Application;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CompanyEnrichment.IntegrationTests;

/// <summary>
/// End-to-end integration tests that exercise the real enrichment pipeline
/// (controller -> EnrichmentService -> CvrClient/StatisticsClient) against
/// the live external CVR and Statistikbanken APIs.
/// </summary>
public class CompanyEnrichmentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Cvr = "16500836";
    private const int Year = 2024;

    private readonly WebApplicationFactory<Program> _factory;

    public CompanyEnrichmentEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetEnrichmentAsync_ReturnsOk_ForKnownCvrAndYear()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Cvr}/enrichment?year={Year}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CompanyEnrichmentResult>(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(Cvr, result!.Company.Cvr);
        Assert.False(string.IsNullOrWhiteSpace(result.Company.Name));
    }

    [Fact]
    public async Task GetEnrichmentAsync_ReturnsIndustryStatistics_ForKnownCvrAndYear()
    {
        using var client = _factory.CreateClient();

        var result = await client.GetFromJsonAsync<CompanyEnrichmentResult>(
            $"/api/companies/{Cvr}/enrichment?year={Year}", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.NotNull(result!.Statistics);
    }

    [Fact]
    public async Task GetEnrichmentAsync_ReturnsBadRequest_WhenCvrIsMalformed()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/not-a-cvr/enrichment?year={Year}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
