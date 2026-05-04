using System.Globalization;
using Oracle.ManagedDataAccess.Types;

namespace WorkAudit.Storage.Oracle;

/// <summary>
/// Converts Oracle scalar values (including <see cref="OracleDecimal"/> from RETURNING clauses
/// and NUMBER columns) to CLR integers without relying on <see cref="IConvertible"/>.
/// </summary>
internal static class OracleValueConversion
{
    public static int ScalarToInt32(object? value)
    {
        if (value is null || value == DBNull.Value)
            return 0;
        if (value is OracleDecimal od)
            return od.ToInt32();
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    public static long ScalarToInt64(object? value)
    {
        if (value is null || value == DBNull.Value)
            return 0;
        if (value is OracleDecimal od)
            return od.ToInt64();
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }
}
