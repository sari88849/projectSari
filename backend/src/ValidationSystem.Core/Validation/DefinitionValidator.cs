using System.Text.RegularExpressions;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Validation;

/// <summary>A problem found in a definition. NodeId lets the UI highlight the offending condition.</summary>
public sealed record DefinitionError(string? NodeId, string Message);

/// <summary>Thrown when an invalid definition reaches a step that requires a valid one.</summary>
public sealed class InvalidDefinitionException(IReadOnlyList<DefinitionError> errors)
    : Exception("Validation definition is invalid: " + string.Join("; ", errors.Select(e => e.Message)))
{
    public IReadOnlyList<DefinitionError> Errors { get; } = errors;
}

/// <summary>
/// Checks a definition against the approved metadata catalog and operator rules.
/// A definition that passes here can be safely turned into SQL: every entity, field,
/// relationship, operator and value type has been verified.
/// </summary>
public sealed partial class DefinitionValidator(MetadataCatalog catalog)
{
    public const int MaxDepth = 5;
    public const int MaxConditions = 50;
    public const int MaxListValues = 100;
    public const int MaxDisplayFields = 30;

    [GeneratedRegex("^[A-Za-z0-9_-]{1,50}$")]
    private static partial Regex NodeIdRegex();

    public IReadOnlyList<DefinitionError> Validate(ValidationDefinition? definition)
    {
        var errors = new List<DefinitionError>();
        if (definition is null)
        {
            errors.Add(new(null, "הגדרת הבדיקה ריקה"));
            return errors;
        }

        var root = catalog.FindEntity(definition.RootEntity);
        if (root is null)
        {
            errors.Add(new(null, $"הישות '{definition.RootEntity}' אינה קיימת בקטלוג"));
            return errors;     // nothing else can be resolved without a root entity
        }

        if (definition.Condition is not ConditionGroup)
        {
            errors.Add(new(null, "לא הוגדרו תנאים (שורש ההגדרה חייב להיות קבוצת תנאים)"));
            return errors;
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var conditionCount = 0;
        ValidateNode(definition.Condition, depth: 1);

        if (conditionCount > MaxConditions)
            errors.Add(new(null, $"מספר התנאים ({conditionCount}) חורג מהמקסימום ({MaxConditions})"));

        if (definition.DisplayFields is { } display)
        {
            if (display.Count > MaxDisplayFields)
                errors.Add(new(null, $"ניתן להציג עד {MaxDisplayFields} שדות"));
            foreach (var path in display)
                if (catalog.ResolveField(root.Key, path, out var error) is null)
                    errors.Add(new(null, $"שדה תצוגה לא תקין: {error}"));
        }

        return errors;

        void ValidateNode(ConditionNode node, int depth)
        {
            if (!NodeIdRegex().IsMatch(node.Id ?? ""))
                errors.Add(new(null, $"מזהה תנאי לא תקין: '{node.Id}'"));
            else if (!ids.Add(node.Id!))
                errors.Add(new(node.Id, $"מזהה התנאי '{node.Id}' מופיע יותר מפעם אחת"));

            switch (node)
            {
                case ConditionGroup group:
                    if (depth > MaxDepth)
                    {
                        errors.Add(new(group.Id, $"קינון הקבוצות עמוק מדי (מקסימום {MaxDepth} רמות)"));
                        return;
                    }
                    if (!Enum.IsDefined(group.Logic))
                        errors.Add(new(group.Id, "אופרטור לוגי לא תקין (וגם / או)"));
                    if (group.Children is not { Count: > 0 })
                        errors.Add(new(group.Id, "קבוצת תנאים ריקה"));
                    else
                        foreach (var child in group.Children)
                        {
                            if (child is null) { errors.Add(new(group.Id, "תנאי ריק בקבוצה")); continue; }
                            ValidateNode(child, depth + 1);
                        }
                    break;

                case Condition condition:
                    conditionCount++;
                    ValidateCondition(condition);
                    break;
            }
        }

        void ValidateCondition(Condition c)
        {
            var field = catalog.ResolveField(root.Key, c.Field, out var fieldError);
            if (field is null) { errors.Add(new(c.Id, fieldError!)); return; }

            if (c.Operator is not { } opValue) { errors.Add(new(c.Id, $"יש לבחור תנאי עבור השדה '{field.Label}'")); return; }
            if (!Enum.IsDefined(opValue)) { errors.Add(new(c.Id, "אופרטור לא מוכר")); return; }
            var op = OperatorCatalog.Get(opValue);
            var type = field.Field.DataType;

            if (!op.SupportedTypes.Contains(type))
            {
                errors.Add(new(c.Id, $"האופרטור '{op.Label}' אינו מתאים לשדה '{field.Label}'"));
                return;
            }

            switch (op.Shape)
            {
                case OperandShape.None:
                    if (c.Value is not null || c.ValueTo is not null)
                        errors.Add(new(c.Id, $"האופרטור '{op.Label}' אינו מקבל ערך להשוואה"));
                    break;

                case OperandShape.Single:
                    ValidateOperand(c, c.Value, type, op, field);
                    if (c.ValueTo is not null) errors.Add(new(c.Id, "ערך שני מותר רק באופרטור 'בין'"));
                    break;

                case OperandShape.Range:
                    if (c.Value?.Kind != OperandKind.Constant || c.ValueTo?.Kind != OperandKind.Constant)
                    {
                        errors.Add(new(c.Id, "האופרטור 'בין' דורש ערך מינימום וערך מקסימום"));
                        break;
                    }
                    ValidateOperand(c, c.Value, type, op, field);
                    ValidateOperand(c, c.ValueTo, type, op, field);
                    break;

                case OperandShape.List:
                    if (c.Value is not { Kind: OperandKind.List, Values: { Count: > 0 } values })
                    {
                        errors.Add(new(c.Id, "יש לבחור לפחות ערך אחד ברשימה"));
                        break;
                    }
                    if (values.Count > MaxListValues)
                        errors.Add(new(c.Id, $"ניתן לבחור עד {MaxListValues} ערכים"));
                    foreach (var v in values)
                        if (!ValueConverter.TryConvert(v, type, out _, out var err))
                            errors.Add(new(c.Id, $"{field.Label}: {err}"));
                    if (c.ValueTo is not null) errors.Add(new(c.Id, "ערך שני מותר רק באופרטור 'בין'"));
                    break;
            }
        }

        void ValidateOperand(Condition c, Operand? operand, FieldDataType type, OperatorInfo op, ResolvedField field)
        {
            switch (operand?.Kind)
            {
                case OperandKind.Constant:
                    if (!ValueConverter.TryConvert(operand.Value, type, out _, out var err))
                        errors.Add(new(c.Id, $"{field.Label}: {err}"));
                    break;

                case OperandKind.Field when op.AllowsFieldOperand:
                    var other = catalog.ResolveField(root.Key, operand.Field, out var otherError);
                    if (other is null)
                        errors.Add(new(c.Id, otherError!));
                    else if (other.Field.DataType != type)
                        errors.Add(new(c.Id, $"לא ניתן להשוות את '{field.Label}' ל-'{other.Label}' - סוגי נתונים שונים"));
                    break;

                case OperandKind.Field:
                    errors.Add(new(c.Id, $"האופרטור '{op.Label}' אינו תומך בהשוואה לשדה אחר"));
                    break;

                default:
                    errors.Add(new(c.Id, $"חסר ערך להשוואה עבור '{field.Label}'"));
                    break;
            }
        }
    }

    public void EnsureValid(ValidationDefinition definition)
    {
        var errors = Validate(definition);
        if (errors.Count > 0) throw new InvalidDefinitionException(errors);
    }
}
