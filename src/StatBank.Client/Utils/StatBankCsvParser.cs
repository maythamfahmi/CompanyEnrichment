using CompanyEnrichment.Application;

namespace StatBank.Client.Utils;

/// <summary>
/// A single observation row parsed out of the StatBank CSV response for table ERHV1.
/// </summary>
public sealed record StatBankObservation(string TalCode, string? RawValue, decimal? Value, bool IsMissing);

/// <summary>
/// Parses the semicolon-delimited CSV returned by api.statbank.dk.
/// ASSUMPTION (unverified against a live response, see README "Provider contract assumptions"):
/// - Delimiter is ';' with a header row.
/// - The header contains a "TAL" column identifying which statistic the row represents
///   (ARBSTED / ANSATTE / FULDBESK / LØNSUM) and an "INDHOLD" column holding the numeric value.
/// - Missing/suppressed observations are represented as ".." or an empty cell.
/// - Decimal values use '.' as the decimal separator (StatBank CSV convention).
/// </summary>
public static class StatBankCsvParser
{
    private const string ProviderName = "statistics";
    private static readonly string[] MissingTokens = ["..", "-", "", "NA", "N/A"];

    public static IReadOnlyList<StatBankObservation> Parse(string csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            throw new InvalidExternalResponseException(ProviderName, "empty CSV body");
        }

        var lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            throw new InvalidExternalResponseException(ProviderName, "CSV has no data rows");
        }

        var delimiter = lines[0].Contains(';') ? ';' : ',';
        var headers = lines[0].Split(delimiter).Select(h => h.Trim().Trim('"')).ToArray();

        var talIndex = FindColumn(headers, "TAL");
        var valueIndex = FindColumn(headers, "INDHOLD") is var idx && idx >= 0
            ? idx
            : headers.Length - 1;

        if (talIndex < 0)
        {
            throw new InvalidExternalResponseException(ProviderName, "missing 'TAL' column in CSV header");
        }

        var observations = new List<StatBankObservation>();

        foreach (var line in lines.Skip(1))
        {
            var cells = line.Split(delimiter).Select(c => c.Trim().Trim('"')).ToArray();
            if (cells.Length <= Math.Max(talIndex, valueIndex))
            {
                continue;
            }

            var talCode = cells[talIndex];
            var rawValue = cells[valueIndex];
            var isMissing = MissingTokens.Contains(rawValue, StringComparer.OrdinalIgnoreCase);
            decimal? value = !isMissing && decimal.TryParse(
                rawValue.Replace(",", "."),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null;

            observations.Add(new StatBankObservation(talCode, rawValue, value, isMissing || value is null));
        }

        return observations;
    }

    private static int FindColumn(string[] headers, string name) =>
        Array.FindIndex(headers, h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
}
