using Microsoft.Data.SqlClient;

namespace ValidationSystem.Infrastructure;

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = "";
    /// <summary>Seconds before a validation query is cancelled.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
    /// <summary>Least-privilege database user that validation queries run as (EXECUTE AS USER).</summary>
    public string ExecutionUser { get; set; } = "validation_reader";
}

public sealed class SqlConnectionFactory(DatabaseOptions options)
{
    public DatabaseOptions Options { get; } = options;

    public async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(Options.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
