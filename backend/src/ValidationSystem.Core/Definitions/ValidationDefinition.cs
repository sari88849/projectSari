using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValidationSystem.Core.Definitions;

/// <summary>
/// A structured validation definition. It describes the condition that identifies
/// INVALID records of <see cref="RootEntity"/>. It contains no SQL: only catalog keys,
/// operators and values. This same model is produced by the builder UI and by the AI flow.
/// </summary>
public sealed class ValidationDefinition
{
    public string RootEntity { get; set; } = "";
    /// <summary>Root of the condition tree. Typed as the base node so the "type" discriminator is always enforced;
    /// the validator requires it to be a group.</summary>
    public ConditionNode Condition { get; set; } = new ConditionGroup();

    /// <summary>The root group (valid definitions only).</summary>
    [JsonIgnore]
    public ConditionGroup RootGroup => Condition as ConditionGroup
        ?? throw new InvalidOperationException("The root condition must be a group.");
    /// <summary>Optional field paths to show in results. Fields used in conditions are always shown.</summary>
    public List<string>? DisplayFields { get; set; }
}

public enum LogicalOperator { And, Or }

public enum ConditionOperator
{
    Equals, NotEquals,
    GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
    Between, In,
    Contains, StartsWith,
    IsEmpty, IsNotEmpty
}

/// <summary>A node in the condition tree: a group or a single condition (JSON "type" discriminator).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ConditionGroup), "group")]
[JsonDerivedType(typeof(Condition), "condition")]
public abstract class ConditionNode
{
    /// <summary>Stable id, used to map SQL flag columns back to conditions for failure reasons. Never used in SQL.</summary>
    public string Id { get; set; } = "";
}

/// <summary>Children combined with AND / OR. Groups can be nested.</summary>
public sealed class ConditionGroup : ConditionNode
{
    public LogicalOperator Logic { get; set; } = LogicalOperator.And;
    public List<ConditionNode> Children { get; set; } = [];
}

/// <summary>A single comparison: field OPERATOR value [valueTo].</summary>
public sealed class Condition : ConditionNode
{
    public string Field { get; set; } = "";
    public ConditionOperator Operator { get; set; }
    public Operand? Value { get; set; }
    /// <summary>Upper bound for Between.</summary>
    public Operand? ValueTo { get; set; }
}

public enum OperandKind { Constant, List, Field }

/// <summary>Right-hand side of a condition: a constant, a list of constants (In) or another field.</summary>
public sealed class Operand
{
    public OperandKind Kind { get; set; }
    public JsonElement? Value { get; set; }
    public List<JsonElement>? Values { get; set; }
    public string? Field { get; set; }

    public static Operand Const(object? value) => new() { Kind = OperandKind.Constant, Value = JsonSerializer.SerializeToElement(value) };
    public static Operand List(params object?[] values) => new() { Kind = OperandKind.List, Values = values.Select(v => JsonSerializer.SerializeToElement(v)).ToList() };
    public static Operand FieldRef(string path) => new() { Kind = OperandKind.Field, Field = path };
}

/// <summary>Single place for the definition JSON format (camelCase, enums as strings).</summary>
public static class DefinitionJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }, // only named enum values
        AllowOutOfOrderMetadataProperties = true,          // "type" need not be the first property
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,   // reject unknown properties (typos, injected keys)
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep Hebrew readable in storage
    };

    public static string Serialize(ValidationDefinition definition) => JsonSerializer.Serialize(definition, Options);

    /// <summary>Parses definition JSON; throws <see cref="JsonException"/> on malformed input.</summary>
    public static ValidationDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<ValidationDefinition>(json, Options) ?? throw new JsonException("Empty definition.");
}
