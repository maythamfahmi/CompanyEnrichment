namespace Cvr.Client.Models;

public sealed class CvrOptions
{
    public const string SectionName = "Cvr";

    public string BaseUrl { get; set; } = "https://apicvr.dk/api/v1/";

    public int PerAttemptTimeoutSeconds { get; set; } = 5;

    public int TotalTimeoutSeconds { get; set; } = 15;
}
