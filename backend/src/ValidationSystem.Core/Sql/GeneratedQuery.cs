using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Sql;

/// <summary>A typed query parameter. Infrastructure maps DataType to an explicit SqlDbType.</summary>
public sealed record QueryParameter(string Name, object Value, FieldDataType DataType);

/// <summary>A selected field column (alias f0, f1, ...).</summary>
public sealed record QueryColumn(string Alias, ResolvedField Field, bool IsDisplayed);

/// <summary>
/// Output of <see cref="SqlQueryBuilder"/>: a single parameterized SELECT plus everything
/// needed to interpret its rows (which column is which field, which flag is which condition).
/// </summary>
public sealed record GeneratedQuery(
    string Sql,
    IReadOnlyList<QueryParameter> Parameters,
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyDictionary<string, string> ConditionFlagAliases,   // condition id -> flag column alias
    ValidationDefinition Definition,
    EntityMetadata RootEntity)
{
    public const string KeyAlias = "__key";
    public const string TotalAlias = "__total";
    public const string MaxRowsParameter = "@maxRows";
}
