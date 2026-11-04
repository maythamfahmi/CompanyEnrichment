using System.Net;
using System.Net.Http.Json;
using CompanyEnrichment.Application;
using CompanyEnrichment.Observability;
using Microsoft.Extensions.Logging;
using StatBank.Client.Models;
using StatBank.Client.Utils;

namespace CompanyEnrichment.Infrastructure.Statistics;

public interface IStatisticsClient
{
    Task<IReadOnlyList<StatBankObservation>> GetIndustryStatisticsAsync(
        string branche07Code, int year, CancellationToken cancellationToken);
}

/// <summary>Typed HttpClient for api.statbank.dk's ERHV1 table.</summary>
public sealed class StatisticsClient(
    HttpClient httpClient, StatisticsOptions options, ILogger<StatisticsClient> logger, DependencyMetrics metrics)
    : IStatisticsClient
{
    private const string ProviderName = "statistics";
    private const string TalCodesHeader = "TAL";

    public async Task<IReadOnlyList<StatBankObservation>> GetIndustryStatisticsAsync(
        string branche07Code, int year, CancellationToken cancellationToken)
    {
        var scope = metrics.StartCall(ProviderName);
        var request = new StatBankRequest
        {
            Table = options.Table,
            Format = "CSV",
            Variables =
            [
                new StatBankVariable("BRANCHE07", [branche07Code]),
                new StatBankVariable(TalCodesHeader, ["ARBSTED", "ANSATTE", "FULDBESK", "LØNSUM"]),
                new StatBankVariable("TID", [year.ToString()]),
            ],
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync("data", request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            scope.Timeout();
            throw new ExternalTimeoutException(ProviderName, ex);
        }
        catch (HttpRequestException ex)
        {
            scope.Error();
            throw new ExternalServiceException(ProviderName, ex);
        }

        using (response)
        {
            try
            {
                if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.NoContent)
                {
                    scope.NotFound();
                    return [];
                }

                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    // StatBank returns 400 when a requested variable value (industry code, year, ...)
                    // is not a valid member of the table's domain — e.g. a 6-digit DB07 company-level
                    // industry code that isn't one of ERHV1's published BRANCHE07 groupings. This is a
                    // "statistics not available for this selection" condition, not a technical failure,
                    // so it is treated the same as "not found" rather than surfaced as a 502.
                    var badRequestBody = await TryReadBodyAsync(response, cancellationToken);
                    logger.LogInformation(
                        "Statistics provider rejected the request as invalid (likely unsupported industry code/year). Body: {Body}",
                        badRequestBody);
                    scope.NotFound();
                    return [];
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await TryReadBodyAsync(response, cancellationToken);
                    logger.LogWarning(
                        "Statistics provider returned non-success status {StatusCode}. Body: {Body}",
                        response.StatusCode, errorBody);

                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        throw new ExternalServiceException(ProviderName);
                    }

                    throw new InvalidExternalResponseException(
                        ProviderName, $"unexpected status code {(int)response.StatusCode}: {errorBody}");
                }

                var csv = await response.Content.ReadAsStringAsync(cancellationToken);
                var observations = StatBankCsvParser.Parse(csv);
                scope.Success();
                return observations;
            }
            catch (EnrichmentException)
            {
                scope.Error();
                throw;
            }
        }
    }

    private static async Task<string> TryReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return body.Length > 500 ? body[..500] : body;
        }
        catch
        {
            return "<unreadable body>";
        }
    }
}
