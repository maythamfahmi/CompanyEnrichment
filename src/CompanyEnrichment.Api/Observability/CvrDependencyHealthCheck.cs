using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CompanyEnrichment.Observability;

/// <summary>
/// Readiness probe for the CVR dependency. Does not gate liveness — an external outage must not
/// cause the process itself to be recycled.
/// </summary>
public sealed class CvrDependencyHealthCheck(IHttpClientFactory httpClientFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = httpClientFactory.CreateClient("cvr-health");
            client.Timeout = TimeSpan.FromSeconds(3);
            using var response = await client.GetAsync("https://apicvr.dk/", cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("CVR dependency unreachable", ex);
        }
    }
}
