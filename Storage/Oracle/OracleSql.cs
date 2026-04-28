using System.Text;
using Oracle.ManagedDataAccess.Client;

namespace WorkAudit.Storage.Oracle;

/// <summary>SQLite-style @bind → Oracle :bind (skips inside single-quoted literals).</summary>
public static class OracleSql
{
    public static string ToOracleBindSyntax(string sql)
    {
        var sb = new StringBuilder(sql.Length + 8);
        var inQuote = false;
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '\'')
            {
                inQuote = !inQuote;
                sb.Append(c);
                continue;
            }

            if (!inQuote && c == '@' && i + 1 < sql.Length && (char.IsLetterOrDigit(sql[i + 1]) || sql[i + 1] == '_'))
            {
                sb.Append(':');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>Appends Oracle 12c+ offset/fetch paging.</summary>
    public static string AppendPaging(string sql, bool alreadyHasOrderBy)
    {
        var s = sql.TrimEnd();
        if (!alreadyHasOrderBy && !s.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
            s += " ORDER BY 1";
        return s + " OFFSET :offset ROWS FETCH NEXT :limit ROWS ONLY";
    }

    public static OracleCommand CreateCommand(OracleConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = ToOracleBindSyntax(sql);
        return cmd;
    }

    public static void AddParameter(OracleCommand cmd, string nameWithoutPrefix, object? value)
    {
        var p = new OracleParameter(nameWithoutPrefix, value ?? DBNull.Value);
        cmd.Parameters.Add(p);
    }
}
