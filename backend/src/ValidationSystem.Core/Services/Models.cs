namespace ValidationSystem.Core.Services;

public static class RunStatus
{
    public const string Running = "Running";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public static class ValidationSource
{
    public const string Manual = "Manual";
    public const string Ai = "AI";
}

/// <summary>A saved validation as stored in app.Validations.</summary>
public sealed record StoredValidation(
    int Id,
    string Name,
    string? Description,
    string RootEntityKey,
    string DefinitionJson,
    string Source,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>List item: a validation plus its latest run (if any).</summary>
public sealed record ValidationSummary(
    int Id,
    string Name,
    string? Description,
    string RootEntityKey,
    string Source,
    bool IsActive,
    DateTime UpdatedAt,
    long? LastRunId,
    string? LastRunStatus,
    DateTime? LastRunStartedAt,
    int? LastRunDurationMs,
    int? LastRunViolationCount);

/// <summary>One execution of a validation (app.ValidationRuns).</summary>
public sealed record RunInfo(
    long RunId,
    int ValidationId,
    string Status,
    DateTime StartedAt,
    DateTime? FinishedAt,
    int? DurationMs,
    int? ViolationCount,
    int? StoredResultCount,
    bool IsTruncated,
    string? ErrorMessage,
    string? GeneratedSql,
    string DefinitionSnapshotJson,
    string? ResultMetadataJson);

public sealed record RunCompletion(
    long RunId,
    DateTime FinishedAtUtc,
    int DurationMs,
    int ViolationCount,
    bool IsTruncated,
    string GeneratedSql,
    string ResultMetadataJson);

/// <summary>A stored violating record (app.ValidationRunResults).</summary>
public sealed record StoredResult(
    int RowNumber,
    string RecordKey,
    string RecordTitle,
    string RecordDataJson,
    string ReasonsJson,
    string SearchText);

/// <summary>Server-side filtering of stored results.</summary>
public sealed record ResultQuery(string? Search, string? ConditionId, int Page = 1, int PageSize = 50);

public sealed record PagedResults(int TotalCount, IReadOnlyList<StoredResult> Items);

/// <summary>Columns and conditions of a run, stored with the run so results remain readable after edits.</summary>
public sealed record ResultMetadata(IReadOnlyList<ResultColumnInfo> Columns, IReadOnlyList<ConditionInfo> Conditions);
public sealed record ResultColumnInfo(string FieldPath, string Label, string DataType);
public sealed record ConditionInfo(string Id, string Description);

/// <summary>Result of checking a definition without executing it (builder / AI preview).</summary>
public sealed record DefinitionPreview(
    bool IsValid,
    IReadOnlyList<Validation.DefinitionError> Errors,
    string? Description,
    IReadOnlyList<ConditionInfo> Conditions,
    string? GeneratedSql);

/// <summary>Raised when a requested item does not exist (mapped to HTTP 404).</summary>
public sealed class NotFoundException(string message) : Exception(message);
