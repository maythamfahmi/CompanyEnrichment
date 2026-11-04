using CompanyEnrichment.Application;
using Microsoft.AspNetCore.Mvc;

namespace CompanyEnrichment.Controllers;

[ApiController]
[Route("api/companies")]
public sealed class CompanyEnrichmentController(IEnrichmentService enrichmentService, ILogger<CompanyEnrichmentController> logger)
    : ControllerBase
{
    /// <summary>Returns a CVR company combined with its industry statistics for the given year.</summary>
    [HttpGet("{cvr}/enrichment")]
    public async Task<ActionResult<CompanyEnrichmentResult>> GetEnrichmentAsync(
        string cvr, [FromQuery] int? year, CancellationToken cancellationToken)
    {
        var effectiveYear = year ?? DateTime.UtcNow.Year - 1;

        try
        {
            var result = await enrichmentService.EnrichByCvrAsync(cvr, effectiveYear, cancellationToken);
            return Ok(result);
        }
        catch (InvalidCvrFormatException ex)
        {
            return Problem(title: "Invalid CVR number", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (CompanyNotFoundException ex)
        {
            return Problem(title: "Company not found", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (IndustryStatisticsUnavailableException ex)
        {
            return Problem(title: "Industry statistics unavailable", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (ExternalTimeoutException ex)
        {
            logger.LogError(ex, "Enrichment request timed out");
            return Problem(title: "Upstream timeout", detail: ex.Message, statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (EnrichmentException ex)
        {
            logger.LogError(ex, "Enrichment request failed");
            return Problem(title: "Upstream dependency error", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
