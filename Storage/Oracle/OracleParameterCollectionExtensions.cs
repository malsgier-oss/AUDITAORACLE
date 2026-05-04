using Oracle.ManagedDataAccess.Client;

namespace WorkAudit.Storage.Oracle;

/// <summary>ODP.NET <see cref="OracleParameterCollection"/> has no AddWithValue; this matches ADO-style usage used across stores.</summary>
public static class OracleParameterCollectionExtensions
{
    public static void AddWithValue(this OracleParameterCollection parameters, string name, object? value)
    {
        var normalizedName = NormalizeName(name);
        parameters.Add(new OracleParameter(normalizedName, value ?? DBNull.Value));
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        var n = name.Trim();
        if (n.StartsWith('@') || n.StartsWith(':'))
            return n[1..];
        return n;
    }
}
