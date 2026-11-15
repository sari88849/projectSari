using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Definitions;

/// <summary>How many / which kind of right-hand operands an operator takes.</summary>
public enum OperandShape { None, Single, Range, List }

/// <summary>Describes an operator for the UI, the validator and the reason builder.</summary>
public sealed record OperatorInfo(
    ConditionOperator Operator,
    string Label,                       // UI label, e.g. "גדול מ"
    string? DateLabel,                  // alternative wording for dates, e.g. "מאוחר מ"
    OperandShape Shape,
    IReadOnlyList<FieldDataType> SupportedTypes,
    bool AllowsFieldOperand)
{
    public string LabelFor(FieldDataType type) => type == FieldDataType.Date && DateLabel is not null ? DateLabel : Label;
}

/// <summary>The approved set of operators. Anything else in a definition is rejected.</summary>
public static class OperatorCatalog
{
    private static readonly FieldDataType[] All = [FieldDataType.String, FieldDataType.Number, FieldDataType.Date, FieldDataType.Boolean];
    private static readonly FieldDataType[] Ordered = [FieldDataType.Number, FieldDataType.Date];

    public static readonly IReadOnlyList<OperatorInfo> Operators =
    [
        new(ConditionOperator.Equals,             "שווה ל",          null,               OperandShape.Single, All, true),
        new(ConditionOperator.NotEquals,          "שונה מ",          null,               OperandShape.Single, All, true),
        new(ConditionOperator.GreaterThan,        "גדול מ",          "מאוחר מ",          OperandShape.Single, Ordered, true),
        new(ConditionOperator.GreaterThanOrEqual, "גדול או שווה ל",  "מאוחר או שווה ל",  OperandShape.Single, Ordered, true),
        new(ConditionOperator.LessThan,           "קטן מ",           "מוקדם מ",          OperandShape.Single, Ordered, true),
        new(ConditionOperator.LessThanOrEqual,    "קטן או שווה ל",   "מוקדם או שווה ל",  OperandShape.Single, Ordered, true),
        new(ConditionOperator.Between,            "בין",             null,               OperandShape.Range,  Ordered, false),
        new(ConditionOperator.In,                 "אחד מהערכים",     null,               OperandShape.List,   [FieldDataType.String, FieldDataType.Number], false),
        new(ConditionOperator.Contains,           "מכיל",            null,               OperandShape.Single, [FieldDataType.String], false),
        new(ConditionOperator.StartsWith,         "מתחיל ב",         null,               OperandShape.Single, [FieldDataType.String], false),
        new(ConditionOperator.IsEmpty,            "ריק / חסר",       null,               OperandShape.None,   All, false),
        new(ConditionOperator.IsNotEmpty,         "לא ריק",          null,               OperandShape.None,   All, false),
    ];

    public static OperatorInfo Get(ConditionOperator op) => Operators.First(o => o.Operator == op);
}
