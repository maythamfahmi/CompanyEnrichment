namespace CompanyEnrichment.Application;

/// <summary>Base type for all enrichment-domain failures that map to specific HTTP responses.</summary>
public abstract class EnrichmentException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class CompanyNotFoundException(string cvr)
    : EnrichmentException($"No company was found for CVR number '{cvr}'.");

public sealed class AmbiguousCompanySearchException(string name, int matchCount)
    : EnrichmentException($"Search for '{name}' returned {matchCount} matching companies; a unique match is required.");

public sealed class IndustryStatisticsUnavailableException(string industryCode, int year)
    : EnrichmentException($"Industry statistics for BRANCHE07 '{industryCode}' and year {year} are not available.");

public sealed class ExternalTimeoutException(string provider, Exception? inner = null)
    : EnrichmentException($"The '{provider}' dependency timed out.", inner);

public sealed class ExternalServiceException(string provider, Exception? inner = null)
    : EnrichmentException($"The '{provider}' dependency returned an error.", inner);

public sealed class InvalidExternalResponseException(string provider, string details, Exception? inner = null)
    : EnrichmentException($"The '{provider}' dependency returned a malformed response: {details}", inner);

public sealed class InvalidCvrFormatException(string cvr)
    : EnrichmentException($"'{cvr}' is not a valid CVR number; expected exactly 8 digits.");

public sealed class InvalidSearchNameException(string name)
    : EnrichmentException($"'{name}' is not a valid company name search term; it must not be empty or whitespace.");
