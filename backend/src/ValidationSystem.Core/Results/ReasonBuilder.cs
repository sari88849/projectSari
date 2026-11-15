using System.Text.Json;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Validation;

namespace ValidationSystem.Core.Results;

/// <summary>
/// Produces Hebrew, business-level text from a definition - generically, from the catalog labels
/// and the operator catalog. Nothing here is specific to any validation.
///   Describe(...)  - the rule itself, e.g. "גיל קטן מ-18"                 (builder preview, AI proposal)
///   Explain(...)   - why a record failed,  e.g. "גיל קטן מ-18 (ערך בפועל: 16)" (results)
/// </summary>
public sealed class ReasonBuilder(MetadataCatalog catalog)
{
    /// <summary>Human readable text of a whole condition tree, e.g. "(גיל קטן מ-18 וגם שכר חודשי גדול מ-15,000)".</summary>
    public string DescribeTree(string rootEntity, ConditionNode node, bool isRoot = true) => node switch
    {
        Condition c => Describe(rootEntity, c),
        ConditionGroup g => Wrap(string.Join(g.Logic == LogicalOperator.And ? " וגם " : " או ",
                                              g.Children.Select(ch => DescribeTree(rootEntity, ch, false))),
                                  !isRoot && g.Children.Count > 1),
        _ => ""
    };

    public string Describe(string rootEntity, Condition c) => Build(rootEntity, c, actual: null, otherActual: null, withActual: false);

    /// <summary>Failure reason for one record. <paramref name="actual"/> is the record's value of the condition field.</summary>
    public string Explain(string rootEntity, Condition c, object? actual, object? otherActual) =>
        Build(rootEntity, c, actual, otherActual, withActual: true);

    private string Build(string rootEntity, Condition c, object? actual, object? otherActual, bool withActual)
    {
        var field = catalog.Resolve(rootEntity, c.Field);
        var meta = field.Field;
        var label = field.Label;
        var op = OperatorCatalog.Get(c.Operator!.Value);   // definitions reaching here are validated
        var phrase = op.LabelFor(meta.DataType);
        var actualText = ValueFormatter.Format(actual, meta);
        string WithActual(string text) => withActual ? $"{text} (ערך בפועל: {actualText})" : text;

        switch (c.Operator)
        {
            case ConditionOperator.IsEmpty:
                return $"חסר ערך בשדה \"{label}\"";

            case ConditionOperator.IsNotEmpty:
                return withActual ? $"קיים ערך בשדה \"{label}\" ({actualText})" : $"קיים ערך בשדה \"{label}\"";

            case ConditionOperator.Between:
                return WithActual($"{label} בין {Constant(c.Value!, meta)} ל-{Constant(c.ValueTo!, meta)}");

            case ConditionOperator.In:
                return WithActual($"{label} אחד מהערכים: {string.Join(", ", c.Value!.Values!.Select(v => Constant(v, meta)))}");

            case ConditionOperator.Contains:
                return WithActual($"{label} מכיל {Constant(c.Value!, meta)}");
        }

        // Single operand: comparison with another field, or with a constant.
        if (c.Value!.Kind == OperandKind.Field)
        {
            var other = catalog.Resolve(rootEntity, c.Value.Field!);
            return withActual
                ? $"{label} ({actualText}) {phrase}-\"{other.Label}\" ({ValueFormatter.Format(otherActual, other.Field)})"
                : $"{label} {phrase}-\"{other.Label}\"";
        }

        // Boolean equality reads naturally as "label: value", e.g. "סטטוס מחלקה: לא פעילה".
        if (c.Operator == ConditionOperator.Equals && meta.DataType == FieldDataType.Boolean)
            return $"{label}: {ValueFormatter.Format(ValueConverter.Convert(c.Value.Value, meta.DataType), meta)}";

        var text = $"{label} {phrase}-{Constant(c.Value, meta)}";
        // For equality the actual value is, by definition, the compared value - no need to repeat it.
        return c.Operator == ConditionOperator.Equals ? text : WithActual(text);
    }

    private static string Constant(Operand operand, FieldMetadata field) => Constant(operand.Value, field);

    private static string Constant(JsonElement? element, FieldMetadata field)
    {
        var text = ValueFormatter.Format(ValueConverter.Convert(element, field.DataType), field);
        return field.DataType is FieldDataType.String or FieldDataType.Boolean ? $"\"{text}\"" : text;
    }

    private static string Wrap(string text, bool parenthesize) => parenthesize ? $"({text})" : text;
}
