using System.Text.Json.Serialization;

namespace Cvr.Client.Models;

/// <summary>
/// Provider DTO for apicvr.dk's single-company lookup and name-search responses.
/// VERIFIED against live apicvr.dk responses (see README "Provider contract assumptions"):
/// numeric-looking fields (vat, zipcode, industrycode) are returned as JSON numbers, not strings;
/// "employees" is either a JSON number or null. The name-search endpoint
/// (/api/v1/search/company/{name}) returns a bare JSON array of this shape at the document root
/// (no "results" wrapper object).
/// </summary>
public sealed class CvrCompanyResponse
{
    [JsonPropertyName("vat")]
    public long? Vat { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("zipcode")]
    public int? ZipCode { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("industrycode")]
    public int? IndustryCode { get; set; }

    [JsonPropertyName("industrydesc")]
    public string? IndustryText { get; set; }

    [JsonPropertyName("employees")]
    public int? Employees { get; set; }
}
