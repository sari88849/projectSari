using System.Globalization;
using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Results;

/// <summary>Formats raw values for business users: dd/MM/yyyy dates, grouped numbers, Hebrew boolean labels.</summary>
public static class ValueFormatter
{
    public const string EmptyText = "ריק";

    public static string Format(object? value, FieldMetadata field, string emptyText = EmptyText) => value switch
    {
        null or DBNull => emptyText,
        bool b => b ? field.TrueLabel ?? "כן" : field.FalseLabel ?? "לא",
        DateTime dt => dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        decimal or double or float or int or long or short or byte =>
            System.Convert.ToDecimal(value, CultureInfo.InvariantCulture).ToString("#,##0.##", CultureInfo.InvariantCulture),
        string s when string.IsNullOrWhiteSpace(s) => emptyText,
        _ => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? emptyText
    };
}
