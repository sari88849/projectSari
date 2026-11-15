using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ValidationSystem.Core.Services;
using ValidationSystem.Core.Sql;

namespace ValidationSystem.Infrastructure;

/// <summary>Dapper-based persistence for app.Validations / app.ValidationRuns / app.ValidationRunResults.</summary>
public sealed class SqlValidationRepository(SqlConnectionFactory db) : IValidationRepository
{
    private const string RunColumns = """
        RunId, ValidationId, Status, StartedAt, FinishedAt, DurationMs, ViolationCount, StoredResultCount,
        IsTruncated, ErrorMessage, GeneratedSql, DefinitionSnapshotJson, ResultMetadataJson
        """;

    public async Task<IReadOnlyList<ValidationSummary>> ListAsync(string? search, string? rootEntity, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<ValidationSummary>(new CommandDefinition("""
            SELECT v.ValidationId AS Id, v.Name, v.Description, v.RootEntityKey, v.Source, v.IsActive, v.UpdatedAt,
                   r.RunId AS LastRunId, r.Status AS LastRunStatus, r.StartedAt AS LastRunStartedAt,
                   r.DurationMs AS LastRunDurationMs, r.ViolationCount AS LastRunViolationCount
            FROM app.Validations v
            OUTER APPLY (SELECT TOP (1) * FROM app.ValidationRuns r
                         WHERE r.ValidationId = v.ValidationId ORDER BY r.StartedAt DESC, r.RunId DESC) r
            WHERE (@pattern IS NULL OR v.Name LIKE @pattern OR v.Description LIKE @pattern)
              AND (@rootEntity IS NULL OR v.RootEntityKey = @rootEntity)
            ORDER BY v.UpdatedAt DESC, v.ValidationId DESC;
            """, new { pattern = LikePattern(search), rootEntity = string.IsNullOrWhiteSpace(rootEntity) ? null : rootEntity }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<StoredValidation?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<StoredValidation>(new CommandDefinition("""
            SELECT ValidationId AS Id, Name, Description, RootEntityKey, DefinitionJson, Source, IsActive, CreatedAt, UpdatedAt
            FROM app.Validations WHERE ValidationId = @id;
            """, new { id }, cancellationToken: ct));
    }

    public async Task<int> CreateAsync(StoredValidation v, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO app.Validations (Name, Description, RootEntityKey, DefinitionJson, Source, IsActive, CreatedAt, UpdatedAt)
            OUTPUT INSERTED.ValidationId
            VALUES (@Name, @Description, @RootEntityKey, @DefinitionJson, @Source, @IsActive, @CreatedAt, @UpdatedAt);
            """, v, cancellationToken: ct));
    }

    public async Task<bool> UpdateAsync(StoredValidation v, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition("""
            UPDATE app.Validations
            SET Name = @Name, Description = @Description, RootEntityKey = @RootEntityKey,
                DefinitionJson = @DefinitionJson, UpdatedAt = @UpdatedAt
            WHERE ValidationId = @Id;
            """, v, cancellationToken: ct)) == 1;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition(
            "DELETE FROM app.Validations WHERE ValidationId = @id;", new { id }, cancellationToken: ct)) == 1;
    }

    public async Task<long> StartRunAsync(int validationId, string definitionJson, DateTime startedAtUtc, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO app.ValidationRuns (ValidationId, Status, StartedAt, DefinitionSnapshotJson)
            OUTPUT INSERTED.RunId
            VALUES (@validationId, @status, @startedAtUtc, @definitionJson);
            """, new { validationId, status = RunStatus.Running, startedAtUtc, definitionJson }, cancellationToken: ct));
    }

    /// <summary>Marks the run succeeded and bulk-inserts the result snapshot in one transaction.</summary>
    public async Task CompleteRunAsync(RunCompletion run, IReadOnlyList<StoredResult> results, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(ct);

        if (results.Count > 0)
        {
            var table = new DataTable();
            table.Columns.Add("RunId", typeof(long));
            table.Columns.Add("RowNumber", typeof(int));
            table.Columns.Add("RecordKey", typeof(string));
            table.Columns.Add("RecordTitle", typeof(string));
            table.Columns.Add("RecordDataJson", typeof(string));
            table.Columns.Add("ReasonsJson", typeof(string));
            table.Columns.Add("SearchText", typeof(string));
            foreach (var r in results)
                table.Rows.Add(run.RunId, r.RowNumber, r.RecordKey, r.RecordTitle, r.RecordDataJson, r.ReasonsJson, r.SearchText);

            using var bulk = new SqlBulkCopy(c, SqlBulkCopyOptions.CheckConstraints, tx) { DestinationTableName = "app.ValidationRunResults" };
            foreach (DataColumn col in table.Columns) bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }

        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE app.ValidationRuns
            SET Status = @status, FinishedAt = @FinishedAtUtc, DurationMs = @DurationMs, ViolationCount = @ViolationCount,
                StoredResultCount = @stored, IsTruncated = @IsTruncated, GeneratedSql = @GeneratedSql,
                ResultMetadataJson = @ResultMetadataJson
            WHERE RunId = @RunId;
            """, new
        {
            status = RunStatus.Succeeded, run.FinishedAtUtc, run.DurationMs, run.ViolationCount, stored = results.Count,
            run.IsTruncated, run.GeneratedSql, run.ResultMetadataJson, run.RunId
        }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task FailRunAsync(long runId, DateTime finishedAtUtc, int durationMs, string errorMessage, string? generatedSql, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE app.ValidationRuns
            SET Status = @status, FinishedAt = @finishedAtUtc, DurationMs = @durationMs,
                ErrorMessage = LEFT(@errorMessage, 2000), GeneratedSql = @generatedSql
            WHERE RunId = @runId;
            """, new { status = RunStatus.Failed, finishedAtUtc, durationMs, errorMessage, generatedSql, runId }, cancellationToken: ct));
    }

    public async Task<RunInfo?> GetRunAsync(long runId, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<RunInfo>(new CommandDefinition(
            $"SELECT {RunColumns} FROM app.ValidationRuns WHERE RunId = @runId;", new { runId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<RunInfo>> ListRunsAsync(int validationId, int take, CancellationToken ct = default)
    {
        await using var c = await db.OpenAsync(ct);
        var rows = await c.QueryAsync<RunInfo>(new CommandDefinition(
            $"SELECT TOP (@take) {RunColumns} FROM app.ValidationRuns WHERE ValidationId = @validationId ORDER BY StartedAt DESC, RunId DESC;",
            new { validationId, take }, cancellationToken: ct));
        return rows.ToList();
    }

    /// <summary>Server-side search (title / values / reasons) and filter by failed condition, with paging.</summary>
    public async Task<PagedResults> GetResultsAsync(long runId, ResultQuery query, CancellationToken ct = default)
    {
        const string filter = """
            WHERE r.RunId = @runId
              AND (@pattern IS NULL OR r.SearchText LIKE @pattern)
              AND (@conditionId IS NULL OR EXISTS (
                    SELECT 1 FROM OPENJSON(r.ReasonsJson) WITH (conditionId NVARCHAR(50) '$.conditionId') j
                    WHERE j.conditionId = @conditionId))
            """;
        var args = new
        {
            runId,
            pattern = LikePattern(query.Search),
            conditionId = query.ConditionId,
            skip = (query.Page - 1) * query.PageSize,
            take = query.PageSize
        };

        await using var c = await db.OpenAsync(ct);
        using var multi = await c.QueryMultipleAsync(new CommandDefinition($"""
            SELECT COUNT(*) FROM app.ValidationRunResults r {filter};
            SELECT r.RowNumber, r.RecordKey, r.RecordTitle, r.RecordDataJson, r.ReasonsJson, r.SearchText
            FROM app.ValidationRunResults r {filter}
            ORDER BY r.RowNumber OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """, args, cancellationToken: ct));

        var total = await multi.ReadSingleAsync<int>();
        var items = (await multi.ReadAsync<StoredResult>()).ToList();
        return new PagedResults(total, items);
    }

    private static string? LikePattern(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : "%" + SqlQueryBuilder.EscapeLike(search.Trim()) + "%";
}
