using System.Globalization;
using System.Text.Json;
using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Validation;

/// <summary>
/// Converts JSON constants from a definition into typed CLR values matching the field's data type.
/// The typed value becomes a SQL parameter, so the database never sees user text as SQL.
/// </summary>
public static class ValueConverter
{
    public const int MaxStringLength = 4000;
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ"];

    public static bool TryConvert(JsonElement? element, FieldDataType type, out object? value, out string? error)
    {
        value = null;
        error = null;
        if (element is null || element.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            error = "חסר ערך להשוואה";
            return false;
        }

        var e = element.Value;
        switch (type)
        {
            case FieldDataType.Number:
                if (e.ValueKind == JsonValueKind.Number && e.TryGetDecimal(out var d)) { value = d; return true; }
                if (e.ValueKind == JsonValueKind.String &&
                    decimal.TryParse(e.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) { value = d; return true; }
                error = "הערך חייב להיות מספר";
                return false;

            case FieldDataType.Date:
                if (e.ValueKind == JsonValueKind.String &&
                    DateTime.TryParseExact(e.GetString(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    value = dt.Date;
                    return true;
                }
                error = "הערך חייב להיות תאריך בפורמט yyyy-MM-dd";
                return false;

            case FieldDataType.Boolean:
                if (e.ValueKind is JsonValueKind.True or JsonValueKind.False) { value = e.GetBoolean(); return true; }
                if (e.ValueKind == JsonValueKind.String && bool.TryParse(e.GetString(), out var b)) { value = b; return true; }
                error = "הערך חייב להיות כן/לא";
                return false;

            case FieldDataType.String:
                var s = e.ValueKind switch
                {
                    JsonValueKind.String => e.GetString(),
                    JsonValueKind.Number => e.GetRawText(),
                    _ => null
                };
                if (s is null) { error = "הערך חייב להיות טקסט"; return false; }
                if (s.Length > MaxStringLength) { error = $"הערך ארוך מדי (מקסימום {MaxStringLength} תווים)"; return false; }
                value = s;
                return true;

            default:
                error = "סוג נתונים לא נתמך";
                return false;
        }
    }

    public static object Convert(JsonElement? element, FieldDataType type) =>
        TryConvert(element, type, out var value, out var error) ? value! : throw new InvalidOperationException(error);
}
