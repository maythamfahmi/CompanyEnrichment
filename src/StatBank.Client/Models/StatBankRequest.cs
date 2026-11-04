namespace StatBank.Client.Models;

/// <summary>Request body for Statistics Denmark's /v1/data endpoint (StatBank API).</summary>
public sealed class StatBankRequest
{
    public string Table { get; set; } = "ERHV1";

    public string Format { get; set; } = "CSV";

    public List<StatBankVariable> Variables { get; set; } = [];
}

public sealed class StatBankVariable
{
    public StatBankVariable(string code, IEnumerable<string> values)
    {
        Code = code;
        Values = values.ToList();
    }

    public string Code { get; set; }

    public List<string> Values { get; set; }
}
