using System.Text.Json;
using ValidationSystem.Core.Definitions;

namespace ValidationSystem.Core.Ai;

/// <summary>Parsed AI answer: the translated definition (if any) plus the AI's own metadata.</summary>
public sealed record AiTranslation(
    ValidationDefinition? Definition,
    IReadOnlyList<string> Errors,
    string? Status,
    string? Name,
    string? Description,
    double? Confidence,
    IReadOnlyList<string> Notes);

/// <summary>
/// Translates the AI response format (see shared/ai-response.schema.json) into the engine's
/// <see cref="ValidationDefinition"/> - the same model the builder UI produces. Only the structure is
/// checked here; catalog checks (fields, operators, value types) are done afterwards by the regular
/// DefinitionValidator, exactly as for manually built validations.
/// Condition ids are generated here (c1, c2 ... / g1, g2 ...), never taken from the AI.
/// </summary>
public static class AiResponseTranslator
{
    private static readonly HashSet<string> RootProperties = ["status", "name", "description", "rootEntity", "logic", "conditions", "confidence", "notes"];
    private static readonly HashSet<string> GroupProperties = ["logic", "conditions"];
    private static readonly HashSet<string> ConditionProperties = ["field", "operator", "value", "valueTo", "values", "compareToField"];

    public static AiTranslation Translate(string rawJson)
    {
        var errors = new List<string>();
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new AiTranslation(null, ["תשובת ה-AI אינה JSON תקין"], null, null, null, null, []);
        }

        if (root.ValueKind != JsonValueKind.Object)
            return new AiTranslation(null, ["תשובת ה-AI אינה אובייקט JSON"], null, null, null, null, []);

        CheckProperties(root, RootProperties, "התשובה", errors);
        var status = GetString(root, "status");
        var name = GetString(root, "name");
        var description = GetString(root, "description");
        double? confidence = root.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : null;
        var notes = root.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.Array
            ? n.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

        if (status == "unsupported")
            return new AiTranslation(null, ["לא ניתן היה לתרגם את הבקשה לבדיקה על בסיס הנתונים הקיימים"], status, name, description, confidence, notes);
        if (status != "ok")
            errors.Add("שדה status בתשובת ה-AI חסר או אינו תקין");

        var counters = new Counters();
        var group = ParseGroup(root, "root", counters, errors, depth: 1);
        var rootEntity = GetString(root, "rootEntity");
        if (string.IsNullOrWhiteSpace(rootEntity)) errors.Add("לא צוינה ישות בתשובת ה-AI");

        var definition = errors.Count == 0 && group is not null
            ? new ValidationDefinition { RootEntity = rootEntity!, Condition = group }
            : null;
        return new AiTranslation(definition, errors, status, name, description, confidence, notes);
    }

    private sealed class Counters { public int Conditions; public int Groups; }

    private static ConditionGroup? ParseGroup(JsonElement element, string id, Counters counters, List<string> errors, int depth)
    {
        if (depth > 10) { errors.Add("קינון עמוק מדי בתשובת ה-AI"); return null; }

        var logicText = GetString(element, "logic");
        if (!Enum.TryParse<LogicalOperator>(logicText, ignoreCase: false, out var logic) || !Enum.IsDefined(logic))
        {
            errors.Add($"ערך logic לא תקין: '{logicText}'");
            return null;
        }
        if (!element.TryGetProperty("conditions", out var conditions) || conditions.ValueKind != JsonValueKind.Array || conditions.GetArrayLength() == 0)
        {
            errors.Add("תשובת ה-AI אינה כוללת תנאים");
            return null;
        }

        var group = new ConditionGroup { Id = id, Logic = logic };
        foreach (var item in conditions.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) { errors.Add("תנאי בתשובת ה-AI אינו אובייקט"); continue; }

            if (item.TryGetProperty("conditions", out _))
            {
                CheckProperties(item, GroupProperties, "קבוצת תנאים", errors);
                var child = ParseGroup(item, "g" + ++counters.Groups, counters, errors, depth + 1);
                if (child is not null) group.Children.Add(child);
            }
            else
            {
                var condition = ParseCondition(item, counters, errors);
                if (condition is not null) group.Children.Add(condition);
            }
        }
        return group;
    }

    private static Condition? ParseCondition(JsonElement item, Counters counters, List<string> errors)
    {
        CheckProperties(item, ConditionProperties, "תנאי", errors);
        var field = GetString(item, "field");
        var opText = GetString(item, "operator");
        if (string.IsNullOrWhiteSpace(field)) { errors.Add("תנאי ללא שדה"); return null; }
        if (!Enum.TryParse<ConditionOperator>(opText, ignoreCase: false, out var op) || !Enum.IsDefined(op))
        {
            errors.Add($"אופרטור לא מוכר: '{opText}'");
            return null;
        }

        var condition = new Condition { Id = "c" + ++counters.Conditions, Field = field, Operator = op };

        if (GetString(item, "compareToField") is { } other)
            condition.Value = Operand.FieldRef(other);
        else if (item.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
            condition.Value = new Operand { Kind = OperandKind.List, Values = values.EnumerateArray().Select(v => v.Clone()).ToList() };
        else if (item.TryGetProperty("value", out var value))
            condition.Value = new Operand { Kind = OperandKind.Constant, Value = value.Clone() };

        if (item.TryGetProperty("valueTo", out var valueTo))
            condition.ValueTo = new Operand { Kind = OperandKind.Constant, Value = valueTo.Clone() };

        return condition;
    }

    private static void CheckProperties(JsonElement element, HashSet<string> allowed, string where, List<string> errors)
    {
        foreach (var p in element.EnumerateObject())
            if (!allowed.Contains(p.Name)) errors.Add($"{where} מכיל/ה שדה לא מוכר: '{p.Name}'");
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
