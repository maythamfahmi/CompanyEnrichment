using CompanyEnrichment.Application;
using StatBank.Client.Utils;

namespace StatBankClient.Tests;

public class StatBankCsvParserTests
{
    [Fact]
    public void Parse_ReturnsObservations_ForWellFormedCsv()
    {
        const string csv = "BRANCHE07;TAL;TID;INDHOLD\n620100;ARBSTED;2023;150\n620100;ANSATTE;2023;3200\n";

        var observations = StatBankCsvParser.Parse(csv);

        Assert.Equal(2, observations.Count);
        Assert.Equal(150, observations[0].Value);
        Assert.False(observations[0].IsMissing);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("")]
    public void Parse_MarksObservationAsMissing_WhenValueIsSuppressedOrEmpty(string rawValue)
    {
        var csv = $"BRANCHE07;TAL;TID;INDHOLD\n620100;LØNSUM;2023;{rawValue}\n";

        var observations = StatBankCsvParser.Parse(csv);

        var item = Assert.Single(observations);
        Assert.True(item.IsMissing);
        Assert.Null(item.Value);
    }

    [Fact]
    public void Parse_Throws_WhenCsvHasNoDataRows()
    {
        const string csv = "BRANCHE07;TAL;TID;INDHOLD\n";

        Assert.Throws<InvalidExternalResponseException>(() => StatBankCsvParser.Parse(csv));
    }

    [Fact]
    public void Parse_Throws_WhenHeaderIsMissingTalColumn()
    {
        const string csv = "BRANCHE07;TID;INDHOLD\n620100;2023;150\n";

        Assert.Throws<InvalidExternalResponseException>(() => StatBankCsvParser.Parse(csv));
    }
}
