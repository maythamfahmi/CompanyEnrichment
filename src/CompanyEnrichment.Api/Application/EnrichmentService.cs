using System.Diagnostics;
using CompanyEnrichment.Infrastructure.Statistics;
using Cvr.Client.Models;
using cvrapi.Infrastructure.Cvr;
using StatBank.Client.Utils;

namespace CompanyEnrichment.Application;

/// <summary>Combined result of a CVR lookup enriched with industry statistics.</summary>
public sealed record CompanyEnrichmentResult(CvrCompany Company, IReadOnlyList<StatBankObservation> Statistics);

public interface IEnrichmentService
{
    Task<CompanyEnrichmentResult> EnrichByCvrAsync(string cvr, int year, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates the CVR -> StatBank lookup. StatBank is queried using the industry code
/// returned by CVR, so the two calls are inherently sequential; a CVR failure short-circuits
/// the flow and no StatBank call is made.
/// </summary>
public sealed class EnrichmentService(
    ICvrClient cvrClient,
    IStatisticsClient statisticsClient,
    ILogger<EnrichmentService> logger) : IEnrichmentService
{
    public static readonly ActivitySource ActivitySource = new("CompanyEnrichment.Orchestration", "1.0.0");

    public async Task<CompanyEnrichmentResult> EnrichByCvrAsync(string cvr, int year, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("EnrichCompany");
        activity?.SetTag("enrichment.year", year);

        logger.LogInformation("Starting company enrichment for request {Year}", year);

        var company = await cvrClient.GetByCvrAsync(cvr, cancellationToken);
        if (company is null)
        {
            logger.LogWarning("Company enrichment aborted: CVR lookup returned no match");
            throw new CompanyNotFoundException(cvr);
        }

        if (string.IsNullOrWhiteSpace(company.IndustryCode))
        {
            logger.LogWarning("Company enrichment aborted: company has no industry code, skipping StatBank call");
            throw new IndustryStatisticsUnavailableException(string.Empty, year);
        }

        activity?.SetTag("enrichment.industry_code", company.IndustryCode);

        var statistics = await statisticsClient.GetIndustryStatisticsAsync(company.IndustryCode, year, cancellationToken);

        logger.LogInformation(
            "Completed company enrichment with {ObservationCount} statistics observations", statistics.Count);

        return new CompanyEnrichmentResult(company, statistics);
    }
}
