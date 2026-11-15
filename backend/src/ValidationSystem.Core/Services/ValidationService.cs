using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Results;
using ValidationSystem.Core.Sql;
using ValidationSystem.Core.Validation;

namespace ValidationSystem.Core.Services;

public sealed class ValidationOptions
{
    /// <summary>Maximum violating records fetched and stored per run (snapshot cap).</summary>
    public int MaxStoredResults { get; set; } = 5000;
}

/// <summary>
/// Application service orchestrating the generic engine:
///   parse JSON -> validate against catalog -> generate SQL -> execute (read-only) -> build reasons -> store snapshot.
/// Contains no validation-specific logic: every validation goes through exactly the same path.
/// </summary>
public sealed class ValidationService(
    IMetadataCatalogProvider catalogProvider,
    IValidationRepository repository,
    IQueryExecutor executor,
    ValidationOptions options,
    ILogger<ValidationService> logger)
{
    private static readonly JsonSerializerOptions StorageJson = DefinitionJson.Options;

    // ---------- Definitions ----------

    /// <summary>Parses definition JSON; malformed JSON becomes a business-level validation error.</summary>
    public static ValidationDefinition Parse(JsonElement json)
    {
        try
        {
            return DefinitionJson.Deserialize(json.GetRawText());
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new InvalidDefinitionException([new(null, "מבנה הגדרת הבדיקה אינו תקין: " + ex.Message)]);
        }
    }

    public async Task<DefinitionPreview> PreviewAsync(JsonElement json, CancellationToken ct = default)
    {
        ValidationDefinition definition;
        try { definition = Parse(json); }
        catch (InvalidDefinitionException ex) { return new(false, ex.Errors, null, [], null); }

        var catalog = await catalogProvider.GetCatalogAsync(ct);
        var errors = new DefinitionValidator(catalog).Validate(definition);
        if (errors.Count > 0) return new(false, errors, null, [], null);

        var reasons = new ReasonBuilder(catalog);
        var query = new SqlQueryBuilder(catalog).Build(definition);
        return new(true, [], reasons.DescribeTree(definition.RootEntity, definition.Condition), DescribeConditions(catalog, definition), query.Sql);
    }

    // ---------- CRUD ----------

    public Task<IReadOnlyList<ValidationSummary>> ListAsync(string? search, string? rootEntity, CancellationToken ct = default) =>
        repository.ListAsync(search, rootEntity, ct);

    public async Task<StoredValidation> GetAsync(int id, CancellationToken ct = default) =>
        await repository.GetAsync(id, ct) ?? throw new NotFoundException($"בדיקה {id} לא נמצאה");

    public async Task<StoredValidation> CreateAsync(string name, string? description, JsonElement definitionJson, string source, CancellationToken ct = default)
    {
        var (definition, json) = await ValidateForSaveAsync(name, description, definitionJson, source, ct);
        var now = DateTime.UtcNow;
        var toStore = new StoredValidation(0, name.Trim(), description?.Trim(), definition.RootEntity, json, source, true, now, now);
        var id = await repository.CreateAsync(toStore, ct);
        return toStore with { Id = id };
    }

    public async Task<StoredValidation> UpdateAsync(int id, string name, string? description, JsonElement definitionJson, CancellationToken ct = default)
    {
        var existing = await GetAsync(id, ct);
        var (definition, json) = await ValidateForSaveAsync(name, description, definitionJson, existing.Source, ct);
        var updated = existing with
        {
            Name = name.Trim(), Description = description?.Trim(), RootEntityKey = definition.RootEntity,
            DefinitionJson = json, UpdatedAt = DateTime.UtcNow
        };
        if (!await repository.UpdateAsync(updated, ct)) throw new NotFoundException($"בדיקה {id} לא נמצאה");
        return updated;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        if (!await repository.DeleteAsync(id, ct)) throw new NotFoundException($"בדיקה {id} לא נמצאה");
    }

    private async Task<(ValidationDefinition, string)> ValidateForSaveAsync(
        string name, string? description, JsonElement definitionJson, string source, CancellationToken ct)
    {
        var errors = new List<DefinitionError>();
        if (string.IsNullOrWhiteSpace(name)) errors.Add(new(null, "יש להזין שם לבדיקה"));
        else if (name.Trim().Length > 200) errors.Add(new(null, "שם הבדיקה ארוך מדי (עד 200 תווים)"));
        if (description?.Length > 1000) errors.Add(new(null, "התיאור ארוך מדי (עד 1000 תווים)"));
        if (source is not (ValidationSource.Manual or ValidationSource.Ai)) errors.Add(new(null, "מקור הבדיקה אינו תקין"));

        var definition = Parse(definitionJson);
        var catalog = await catalogProvider.GetCatalogAsync(ct);
        errors.AddRange(new DefinitionValidator(catalog).Validate(definition));
        if (errors.Count > 0) throw new InvalidDefinitionException(errors);

        // Store the canonical form (normalized serialization) rather than the raw request text.
        return (definition, DefinitionJson.Serialize(definition));
    }

    // ---------- Execution ----------

    /// <summary>
    /// Executes a saved validation and stores the run. Failures are recorded on the run (status Failed)
    /// and returned, not thrown, so the UI can show the latest status of every validation.
    /// </summary>
    public async Task<RunInfo> ExecuteAsync(int validationId, CancellationToken ct = default)
    {
        var validation = await GetAsync(validationId, ct);
        var startedAt = DateTime.UtcNow;
        var runId = await repository.StartRunAsync(validationId, validation.DefinitionJson, startedAt, ct);
        var stopwatch = Stopwatch.StartNew();
        GeneratedQuery? query = null;

        try
        {
            var catalog = await catalogProvider.GetCatalogAsync(ct);
            var definition = DefinitionJson.Deserialize(validation.DefinitionJson);
            query = new SqlQueryBuilder(catalog).Build(definition);          // validates against the catalog first

            var rows = await executor.ExecuteAsync(query, options.MaxStoredResults, ct);
            var processed = new ResultProcessor(catalog).Process(query, rows);
            stopwatch.Stop();

            var results = processed.Records.Select((r, i) => ToStored(r, i + 1)).ToList();
            var metadata = new ResultMetadata(
                processed.Columns.Select(c => new ResultColumnInfo(c.FieldPath, c.Label, c.DataType.ToString())).ToList(),
                DescribeConditions(catalog, definition));

            await repository.CompleteRunAsync(new RunCompletion(
                runId, DateTime.UtcNow, (int)stopwatch.ElapsedMilliseconds, processed.TotalViolations,
                processed.TotalViolations > results.Count, query.Sql, JsonSerializer.Serialize(metadata, StorageJson)), results, ct);

            logger.LogInformation("Validation {Id} run {RunId}: {Count} violations in {Ms} ms",
                validationId, runId, processed.TotalViolations, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogError(ex, "Validation {Id} run {RunId} failed", validationId, runId);
            await repository.FailRunAsync(runId, DateTime.UtcNow, (int)stopwatch.ElapsedMilliseconds, ToBusinessError(ex), query?.Sql, CancellationToken.None);
        }

        return (await repository.GetRunAsync(runId, ct))!;
    }

    /// <summary>User-facing error text. Technical details go to the log only.</summary>
    private static string ToBusinessError(Exception ex) => ex switch
    {
        InvalidDefinitionException ide => "הגדרת הבדיקה אינה תקינה מול הקטלוג הנוכחי: " + string.Join("; ", ide.Errors.Select(e => e.Message)),
        JsonException => "הגדרת הבדיקה השמורה פגומה",
        TimeoutException => "זמן הריצה של הבדיקה חרג מהמותר",   // executor translates SQL timeouts to TimeoutException
        _ => "אירעה שגיאה בהרצת הבדיקה מול מסד הנתונים"
    };

    private static StoredResult ToStored(ViolationRecord record, int rowNumber)
    {
        var dataJson = JsonSerializer.Serialize(record.Values, StorageJson);
        var reasonsJson = JsonSerializer.Serialize(record.Reasons, StorageJson);
        var search = string.Join(" | ", new[] { record.Title }
            .Concat(record.Values.Values)
            .Concat(record.Reasons.Select(r => r.Message)));
        if (search.Length > 4000) search = search[..4000];
        return new StoredResult(rowNumber, record.Key, record.Title, dataJson, reasonsJson, search);
    }

    private static List<ConditionInfo> DescribeConditions(MetadataCatalog catalog, ValidationDefinition definition)
    {
        var reasons = new ReasonBuilder(catalog);
        return SqlQueryBuilder.Conditions(definition.Condition)
            .Select(c => new ConditionInfo(c.Id, reasons.Describe(definition.RootEntity, c)))
            .ToList();
    }

    // ---------- Runs & results ----------

    public async Task<RunInfo> GetRunAsync(long runId, CancellationToken ct = default) =>
        await repository.GetRunAsync(runId, ct) ?? throw new NotFoundException($"ריצה {runId} לא נמצאה");

    public async Task<IReadOnlyList<RunInfo>> ListRunsAsync(int validationId, CancellationToken ct = default)
    {
        await GetAsync(validationId, ct);
        return await repository.ListRunsAsync(validationId, 20, ct);
    }

    public async Task<PagedResults> GetResultsAsync(long runId, ResultQuery query, CancellationToken ct = default)
    {
        await GetRunAsync(runId, ct);
        var safe = query with
        {
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 500),
            Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            ConditionId = string.IsNullOrWhiteSpace(query.ConditionId) ? null : query.ConditionId
        };
        return await repository.GetResultsAsync(runId, safe, ct);
    }
}
