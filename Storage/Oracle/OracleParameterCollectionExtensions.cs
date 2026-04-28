using Oracle.ManagedDataAccess.Client;

namespace WorkAudit.Storage.Oracle;

/// <summary>ODP.NET <see cref="OracleParameterCollection"/> has no AddWithValue; this matches ADO-style usage used across stores.</summary>
public static class OracleParameterCollectionExtensions
{
    public static void AddWithValue(this OracleParameterCollection parameters, string name, object? value)
    {
        parameters.Add(new OracleParameter(name, value ?? DBNull.Value));
    }
}
