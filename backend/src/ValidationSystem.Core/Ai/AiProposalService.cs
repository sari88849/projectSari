using System.Text.Json;
using Microsoft.Extensions.Logging;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Results;
using ValidationSystem.Core.Services;
using ValidationSystem.Core.Validation;

namespace ValidationSystem.Core.Ai;

/// <summary>
/// The single extension point for AI. Implementations receive the prompt and the catalog context and
/// return the raw JSON answer (per shared/ai-response.schema.json). Swapping the Python mock for a real
/// LLM means adding another implementation - nothing in the validation engine changes.
/// </summary>
public interface IAiValidationProvider
{
    string Name { get; }
    Task<string> GenerateAsync(AiRequest request, CancellationToken ct = default);
}

/// <summary>Provider could not be reached or failed (mapped to HTTP 503).</summary>
public sealed class AiProviderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A proposal shown to the user for approval. Nothing is saved or executed at this stage.</summary>
public sealed record AiProposal(
    bool IsValid,
    IReadOnlyList<string> Errors,
    string? Name,
    string? Description,
    string? RootEntity,
    JsonElement? Definition,
    string? RuleText,
    IReadOnlyList<ConditionInfo> Conditions,
    double? Confidence,
    IReadOnlyList<string> Notes,
    string RawResponse,
    string Provider);

/// <summary>
/// Natural language -> proposal:
///   catalog -> AI context -> provider -> raw JSON -> translate -> DefinitionValidator (same as Phase A) -> describe.
/// </summary>
public sealed class AiProposalService(
    IMetadataCatalogProvider catalogProvider,
    IAiValidationProvider provider,
    ILogger<AiProposalService> logger)
{
    public const int MaxPromptLength = 1000;

    public async Task<AiContext> GetContextAsync(string? rootEntity, CancellationToken ct = default) =>
        AiContextBuilder.Build(await catalogProvider.GetCatalogAsync(ct), rootEntity);

    public async Task<AiProposal> ProposeAsync(string prompt, string? rootEntity, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            throw new InvalidDefinitionException([new(null, "יש להזין תיאור של הבדיקה")]);
        if (prompt.Length > MaxPromptLength)
            throw new InvalidDefinitionException([new(null, $"התיאור ארוך מדי (עד {MaxPromptLength} תווים)")]);

        var catalog = await catalogProvider.GetCatalogAsync(ct);
        var context = AiContextBuilder.Build(catalog, rootEntity);

        string raw;
        try
        {
            raw = await provider.GenerateAsync(new AiRequest(prompt.Trim(), context), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AiProviderException)
        {
            logger.LogError(ex, "AI provider {Provider} failed", provider.Name);
            throw new AiProviderException("שירות ה-AI אינו זמין כרגע", ex);
        }

        var translation = AiResponseTranslator.Translate(raw);
        var errors = translation.Errors.ToList();
        var definition = translation.Definition;

        // Same catalog validation as any manually built validation.
        if (definition is not null)
        {
            if (rootEntity is not null && !string.Equals(definition.RootEntity, rootEntity, StringComparison.OrdinalIgnoreCase))
                errors.Add($"ה-AI הציע ישות שונה מהמבוקש ({definition.RootEntity})");
            errors.AddRange(new DefinitionValidator(catalog).Validate(definition).Select(e => e.Message));
        }

        var isValid = definition is not null && errors.Count == 0;
        string? ruleText = null;
        IReadOnlyList<ConditionInfo> conditions = [];
        if (isValid)
        {
            var reasons = new ReasonBuilder(catalog);
            ruleText = reasons.DescribeTree(definition!.RootEntity, definition.Condition);
            conditions = Sql.SqlQueryBuilder.Conditions(definition.Condition)
                .Select(c => new ConditionInfo(c.Id, reasons.Describe(definition.RootEntity, c))).ToList();
        }

        logger.LogInformation("AI proposal from {Provider}: valid={Valid}, errors={Errors}", provider.Name, isValid, errors.Count);

        return new AiProposal(
            isValid, errors,
            translation.Name ?? Truncate(prompt.Trim(), 200),
            translation.Description ?? prompt.Trim(),
            definition?.RootEntity,
            definition is null ? null : JsonSerializer.SerializeToElement(definition, DefinitionJson.Options),
            ruleText, conditions, translation.Confidence, translation.Notes, raw, provider.Name);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
