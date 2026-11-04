namespace StatBank.Client.Models;

public sealed class StatisticsOptions
{
    public const string SectionName = "Statistics";

    public string BaseUrl { get; set; } = "https://api.statbank.dk/v1/";

    public int PerAttemptTimeoutSeconds { get; set; } = 8;

    public int TotalTimeoutSeconds { get; set; } = 20;

    public string Table { get; set; } = "ERHV1";
}
