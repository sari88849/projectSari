using System.Reflection;
using System.Text.Json;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Ai;

/// <summary>
/// Everything a (future) AI model needs to map a natural-language request to a definition:
/// task instructions, business entities/fields with Hebrew labels, synonyms and descriptions,
/// allowed operators, and the JSON schema the answer must follow. Built only from the catalog,
/// so the model never sees - and never needs - physical table or column names.
/// </summary>
public sealed record AiContext(
    string Instructions,
    string? RequestedRootEntity,
    IReadOnlyList<AiEntity> Entities,
    IReadOnlyList<AiOperator> Operators,
    JsonElement ResponseSchema);

public sealed record AiEntity(
    string Key, string DisplayName, string DisplayNamePlural, string? Description,
    IReadOnlyList<AiRelationship> Relationships,
    IReadOnlyList<AiField> Fields);

public sealed record AiRelationship(string Key, string DisplayName, string TargetEntity, string? Description);

/// <summary>A field path reachable from the entity (own fields and fields of directly related entities).</summary>
public sealed record AiField(
    string Path, string Label, string FieldLabel, string DataType, string? Description,
    IReadOnlyList<string> Synonyms, string? TrueLabel, string? FalseLabel, IReadOnlyList<string> Operators);

public sealed record AiOperator(string Operator, string Label, string Shape);

/// <summary>The request handed to an AI provider.</summary>
public sealed record AiRequest(string Prompt, AiContext Context);

public static class AiContextBuilder
{
    public const string Instructions = """
        You convert a business user's request (usually Hebrew) into a data-validation rule.
        A rule describes the condition that identifies INVALID records of one root entity.
        Rules:
        - Use only entity keys and field paths that appear in the context. Never invent fields and never output SQL.
        - Match the user's words to field labels, descriptions and synonyms. Paths with a dot go through a relationship.
        - Use only operators listed for the chosen field. Dates are yyyy-MM-dd. Booleans are true/false.
        - To compare two fields use "compareToField" instead of "value".
        - Combine conditions with "logic" (And/Or); nest groups when the request mixes AND and OR.
        - If the request cannot be expressed with the context, return status "unsupported" and explain in "notes".
        - Answer with a single JSON object that follows the response schema exactly.
        """;

    private static readonly Lazy<JsonElement> Schema = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ai-response.schema.json")
            ?? throw new InvalidOperationException("AI response schema resource is missing.");
        return JsonDocument.Parse(stream).RootElement.Clone();
    });

    public static JsonElement ResponseSchema => Schema.Value;

    public static AiContext Build(MetadataCatalog catalog, string? requestedRootEntity)
    {
        var root = catalog.FindEntity(requestedRootEntity);
        var entities = (root is null ? catalog.Entities : [root])
            .OrderBy(e => e.Key)
            .Select(e => new AiEntity(
                e.Key, e.DisplayName, e.DisplayNamePlural, e.Description,
                e.Relationships.Select(r => new AiRelationship(r.Key, r.DisplayName, r.TargetEntityKey, r.Description)).ToList(),
                catalog.GetReachableFields(e.Key, maxHops: 1).Select(ToAiField).ToList()))
            .ToList();

        var operators = OperatorCatalog.Operators
            .Select(o => new AiOperator(o.Operator.ToString(), o.Label, o.Shape.ToString())).ToList();

        return new AiContext(Instructions, root?.Key, entities, operators, ResponseSchema);
    }

    private static AiField ToAiField(ResolvedField f) => new(
        f.Path, f.Label, f.Field.DisplayName, f.Field.DataType.ToString(), f.Field.Description,
        (f.Field.Synonyms ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        f.Field.TrueLabel, f.Field.FalseLabel,
        OperatorCatalog.Operators.Where(o => o.SupportedTypes.Contains(f.Field.DataType)).Select(o => o.Operator.ToString()).ToList());
}
