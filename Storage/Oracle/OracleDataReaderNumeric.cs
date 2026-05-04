using Oracle.ManagedDataAccess.Client;

namespace WorkAudit.Storage.Oracle;

/// <summary>
/// Oracle NUMBER columns are often surfaced as <see cref="OracleDecimal"/>; using
/// <see cref="OracleDataReader.GetInt32"/> / <see cref="OracleDataReader.GetInt64"/> can throw
/// "Unable to cast object of type 'OracleDecimal' to type 'IConvertible'".
/// </summary>
internal static class OracleDataReaderNumeric
{
    public static int GetInt32(OracleDataReader r, string columnName)
    {
        var o = r.GetOrdinal(columnName);
        if (r.IsDBNull(o))
            return 0;
        return OracleValueConversion.ScalarToInt32(r.GetValue(o));
    }

    public static long GetInt64(OracleDataReader r, string columnName)
    {
        var o = r.GetOrdinal(columnName);
        if (r.IsDBNull(o))
            return 0;
        return OracleValueConversion.ScalarToInt64(r.GetValue(o));
    }

    public static int? GetNullableInt32(OracleDataReader r, string columnName)
    {
        var o = r.GetOrdinal(columnName);
        if (r.IsDBNull(o))
            return null;
        return OracleValueConversion.ScalarToInt32(r.GetValue(o));
    }
}
