namespace Cvr.Client.Models;

/// <summary>
/// Normalized CVR company representation used internally by the Application layer.
/// Decoupled from the provider's wire format.
/// </summary>
public sealed class CvrCompany(
    string cvr,
    string name,
    string? address,
    string industryCode,
    string? industryText,
    int? employeeCount)
{
    public string Cvr { get; } = cvr;
    public string Name { get; } = name;
    public string? Address { get; } = address;
    public string IndustryCode { get; } = industryCode;
    public string? IndustryText { get; } = industryText;
    public int? EmployeeCount { get; } = employeeCount;
}

