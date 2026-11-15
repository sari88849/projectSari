using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Sql;

namespace ValidationSystem.Core.Services;

/// <summary>Supplies the current approved metadata catalog (loaded from the meta schema, cached).</summary>
public interface IMetadataCatalogProvider
{
    Task<MetadataCatalog> GetCatalogAsync(CancellationToken ct = default);
    /// <summary>Forces a reload on next access (after metadata configuration changes).</summary>
    void Invalidate();
}

/// <summary>Executes a generated query (read-only) and returns rows keyed by column alias, with DB NULL as null.</summary>
public interface IQueryExecutor
{
    Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> ExecuteAsync(GeneratedQuery query, int maxRows, CancellationToken ct = default);
}

/// <summary>Persistence of saved validations, runs and result snapshots (app schema).</summary>
public interface IValidationRepository
{
    Task<IReadOnlyList<ValidationSummary>> ListAsync(string? search, string? rootEntity, CancellationToken ct = default);
    Task<StoredValidation?> GetAsync(int id, CancellationToken ct = default);
    Task<int> CreateAsync(StoredValidation validation, CancellationToken ct = default);
    Task<bool> UpdateAsync(StoredValidation validation, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    Task<long> StartRunAsync(int validationId, string definitionJson, DateTime startedAtUtc, CancellationToken ct = default);
    Task CompleteRunAsync(RunCompletion completion, IReadOnlyList<StoredResult> results, CancellationToken ct = default);
    Task FailRunAsync(long runId, DateTime finishedAtUtc, int durationMs, string errorMessage, string? generatedSql, CancellationToken ct = default);

    Task<RunInfo?> GetRunAsync(long runId, CancellationToken ct = default);
    Task<IReadOnlyList<RunInfo>> ListRunsAsync(int validationId, int take, CancellationToken ct = default);
    Task<PagedResults> GetResultsAsync(long runId, ResultQuery query, CancellationToken ct = default);
}
