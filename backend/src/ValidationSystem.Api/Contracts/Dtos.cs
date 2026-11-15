using System.Text.Json;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Services;

namespace ValidationSystem.Api.Contracts;

// ---------- Metadata ----------

public sealed record EntityDto(string Key, string DisplayName, string DisplayNamePlural, string? Description);

/// <summary>A selectable field path for the builder, e.g. "Department.IsActive" / "מחלקה › סטטוס מחלקה".</summary>
public sealed record FieldDto(
    string Path, string Label, string FieldLabel, string? GroupLabel, string GroupPath, string DataType,
    bool IsMandatory, string? TrueLabel, string? FalseLabel, string? Description);

public sealed record OperatorDto(
    string Operator, string Label, string? DateLabel, string Shape, IReadOnlyList<string> SupportedTypes, bool AllowsFieldOperand);

// ---------- Validations ----------

public sealed record SaveValidationRequest(string Name, string? Description, JsonElement Definition, string? Source);

public sealed record PreviewRequest(JsonElement Definition);

public sealed record ValidationDto(
    int Id, string Name, string? Description, string RootEntity, string Source, bool IsActive,
    JsonElement Definition, DateTime CreatedAt, DateTime UpdatedAt);

// ---------- Runs & results ----------

public sealed record RunDto(
    long RunId, int ValidationId, string Status, DateTime StartedAt, DateTime? FinishedAt, int? DurationMs,
    int? ViolationCount, int? StoredResultCount, bool IsTruncated, string? ErrorMessage, string? GeneratedSql,
    IReadOnlyList<ResultColumnInfo> Columns, IReadOnlyList<ConditionInfo> Conditions);

public sealed record ReasonDto(string ConditionId, string FieldPath, string Message);

public sealed record ResultItemDto(
    int RowNumber, string RecordKey, string RecordTitle, IReadOnlyDictionary<string, string> Values, IReadOnlyList<ReasonDto> Reasons);

public sealed record PagedResultsDto(int TotalCount, int Page, int PageSize, IReadOnlyList<ResultItemDto> Items);

/// <summary>Mapping between Core models and API DTOs.</summary>
public static class Mapping
{
    private static readonly JsonSerializerOptions Json = DefinitionJson.Options;

    /// <summary>Dates are stored as UTC (datetime2 has no kind); mark them so clients convert to local time.</summary>
    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static DateTime? Utc(DateTime? value) => value is null ? null : Utc(value.Value);

    public static EntityDto ToDto(this EntityMetadata e) => new(e.Key, e.DisplayName, e.DisplayNamePlural, e.Description);

    public static FieldDto ToDto(this ResolvedField f) => new(
        f.Path, f.Label, f.Field.DisplayName,
        f.Hops.Count == 0 ? null : string.Join(" › ", f.Hops.Select(h => h.DisplayName)),
        f.RelationshipPath, f.Field.DataType.ToString(), f.Field.IsMandatory, f.Field.TrueLabel, f.Field.FalseLabel, f.Field.Description);

    public static OperatorDto ToDto(this OperatorInfo o) => new(
        o.Operator.ToString(), o.Label, o.DateLabel, o.Shape.ToString(), o.SupportedTypes.Select(t => t.ToString()).ToList(), o.AllowsFieldOperand);

    public static ValidationDto ToDto(this StoredValidation v) => new(
        v.Id, v.Name, v.Description, v.RootEntityKey, v.Source, v.IsActive,
        JsonDocument.Parse(v.DefinitionJson).RootElement.Clone(), Utc(v.CreatedAt), Utc(v.UpdatedAt));

    public static ValidationSummary ToDto(this ValidationSummary s) => s with
    {
        UpdatedAt = Utc(s.UpdatedAt),
        LastRunStartedAt = Utc(s.LastRunStartedAt)
    };

    public static RunDto ToDto(this RunInfo r)
    {
        var meta = r.ResultMetadataJson is null ? null : JsonSerializer.Deserialize<ResultMetadata>(r.ResultMetadataJson, Json);
        return new RunDto(r.RunId, r.ValidationId, r.Status, Utc(r.StartedAt), Utc(r.FinishedAt), r.DurationMs, r.ViolationCount,
            r.StoredResultCount, r.IsTruncated, r.ErrorMessage, r.GeneratedSql, meta?.Columns ?? [], meta?.Conditions ?? []);
    }

    public static ResultItemDto ToDto(this StoredResult r) => new(
        r.RowNumber, r.RecordKey, r.RecordTitle,
        JsonSerializer.Deserialize<Dictionary<string, string>>(r.RecordDataJson, Json) ?? [],
        JsonSerializer.Deserialize<List<ReasonDto>>(r.ReasonsJson, Json) ?? []);
}
