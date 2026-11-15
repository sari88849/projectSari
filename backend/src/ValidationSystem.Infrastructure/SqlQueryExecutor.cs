using System.Data;
using Microsoft.Data.SqlClient;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Services;
using ValidationSystem.Core.Sql;

namespace ValidationSystem.Infrastructure;

/// <summary>
/// Executes generated validation queries with layered protection:
///   1. Only <see cref="GeneratedQuery"/> objects are accepted (produced by SqlQueryBuilder - no raw SQL path exists).
///   2. A guard refuses any text that is not a single SELECT statement.
///   3. Every value is bound as an explicitly typed SqlParameter.
///   4. The statement runs as a least-privilege database user (SELECT on the business schema only),
///      so the database itself would reject any data modification.
///   5. A command timeout and a row cap (TOP) bound the cost of every run.
/// </summary>
public sealed class SqlQueryExecutor(SqlConnectionFactory db) : IQueryExecutor
{
    public async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(GeneratedQuery query, int maxRows, CancellationToken ct = default)
    {
        EnsureSingleSelect(query.Sql);
        var user = db.Options.ExecutionUser;
        if (!MetadataCatalog.IsSafeIdentifier(user)) throw new InvalidOperationException("Invalid execution user configuration.");

        await using var connection = await db.OpenAsync(ct);

        // Switch the session to the read-only user. REVERT always runs; in addition, pooled connections
        // are reset (sp_reset_connection) before reuse, which also clears any impersonation.
        await using (var impersonate = new SqlCommand($"EXECUTE AS USER = N'{user}';", connection))
            await impersonate.ExecuteNonQueryAsync(ct);

        try
        {
            await using var command = new SqlCommand(query.Sql, connection) { CommandTimeout = db.Options.CommandTimeoutSeconds };
            command.Parameters.Add(new SqlParameter(GeneratedQuery.MaxRowsParameter, SqlDbType.Int) { Value = maxRows });
            foreach (var p in query.Parameters) command.Parameters.Add(ToSqlParameter(p));

            var rows = new List<IReadOnlyDictionary<string, object?>>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.Ordinal);
                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }
        catch (SqlException ex) when (ex.Number == -2)
        {
            throw new TimeoutException("Validation query timed out.", ex);
        }
        finally
        {
            try
            {
                await using var revert = new SqlCommand("REVERT;", connection);
                await revert.ExecuteNonQueryAsync(CancellationToken.None);
            }
            catch
            {
                // Never let a possibly still-impersonated connection go back to the pool.
                SqlConnection.ClearPool(connection);
            }
        }
    }

    /// <summary>Defense in depth: the builder only produces "SELECT ...;" - anything else is refused.</summary>
    internal static void EnsureSingleSelect(string sql)
    {
        var trimmed = sql.Trim();
        if (!trimmed.StartsWith("SELECT ", StringComparison.Ordinal) ||
            trimmed.IndexOf(';') != trimmed.Length - 1 ||
            trimmed.Contains("--", StringComparison.Ordinal) || trimmed.Contains("/*", StringComparison.Ordinal))
            throw new InvalidOperationException("Only a single generated SELECT statement may be executed.");
    }

    private static SqlParameter ToSqlParameter(QueryParameter p) => p.DataType switch
    {
        FieldDataType.Number => new SqlParameter(p.Name, SqlDbType.Decimal) { Precision = 38, Scale = 6, Value = p.Value },
        FieldDataType.Date => new SqlParameter(p.Name, SqlDbType.Date) { Value = p.Value },
        FieldDataType.Boolean => new SqlParameter(p.Name, SqlDbType.Bit) { Value = p.Value },
        FieldDataType.String => new SqlParameter(p.Name, SqlDbType.NVarChar, 4000) { Value = p.Value },
        _ => throw new InvalidOperationException($"Unsupported parameter type {p.DataType}.")
    };
}
