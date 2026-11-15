using System.Text;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Validation;

namespace ValidationSystem.Core.Sql;

/// <summary>
/// Translates a validated definition into ONE parameterized SELECT statement.
///
/// Safety rules enforced here:
///  * The statement is always "SELECT ... FROM ... [LEFT JOIN ...] WHERE ... ORDER BY" - built from fixed fragments.
///  * Table / column names come only from the metadata catalog (validated identifiers) and are bracket-quoted.
///  * Aliases (t0, f0, c0) and parameter names (@p0) are generated here, never taken from input.
///  * Every value is a typed parameter. User text never becomes part of the SQL string.
///
/// Shape of the generated query:
///   SELECT TOP (@maxRows)
///       COUNT(*) OVER () AS [__total],                         -- total violations (before TOP)
///       t0.[PK] AS [__key],
///       &lt;field expr&gt; AS [f0], ...                                -- displayed / referenced fields
///       CASE WHEN &lt;condition&gt; THEN 1 ELSE 0 END AS [c0], ...       -- one flag per condition => failure reasons
///   FROM [schema].[Root] AS t0
///   LEFT JOIN [schema].[Target] AS t1 ON t1.[ToCol] = t0.[FromCol]   -- one join per relationship path
///   WHERE (&lt;condition tree with AND / OR&gt;)
///   ORDER BY t0.[PK]
/// </summary>
public sealed class SqlQueryBuilder(MetadataCatalog catalog)
{
    private readonly DefinitionValidator _validator = new(catalog);

    public GeneratedQuery Build(ValidationDefinition definition)
    {
        _validator.EnsureValid(definition);
        return new Builder(catalog, definition).Build();
    }

    /// <summary>Per-build state (joins, parameters, columns). Keeps the public builder stateless and thread-safe.</summary>
    private sealed class Builder(MetadataCatalog catalog, ValidationDefinition definition)
    {
        private readonly EntityMetadata _root = catalog.FindEntity(definition.RootEntity)!;
        private readonly Dictionary<string, string> _joinAliases = new(StringComparer.OrdinalIgnoreCase) { [""] = "t0" };
        private readonly List<string> _joinClauses = [];
        private readonly List<QueryParameter> _parameters = [];
        private readonly Dictionary<string, string> _predicates = new(StringComparer.Ordinal);   // condition id -> SQL predicate

        public GeneratedQuery Build()
        {
            // 1. Predicates for every condition (parameters are created once per condition).
            var where = GroupSql(definition.RootGroup);

            // 2. Columns: title fields + display fields + every field referenced by a condition.
            var columns = BuildColumns();

            // 3. Flag column per condition, in tree order.
            var flags = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in Conditions(definition.Condition))
                flags[c.Id] = "c" + flags.Count;

            var sql = new StringBuilder();
            sql.AppendLine($"SELECT TOP ({GeneratedQuery.MaxRowsParameter})");
            sql.AppendLine($"    COUNT(*) OVER () AS {Quote(GeneratedQuery.TotalAlias)},");
            sql.Append($"    t0.{Quote(_root.PrimaryKeyColumn)} AS {Quote(GeneratedQuery.KeyAlias)}");
            foreach (var col in columns)
                sql.Append($",\n    {FieldSql(col.Field)} AS {Quote(col.Alias)}");
            foreach (var (id, alias) in flags)
                sql.Append($",\n    CASE WHEN {_predicates[id]} THEN 1 ELSE 0 END AS {Quote(alias)}");
            sql.AppendLine();
            sql.AppendLine($"FROM {Quote(_root.SchemaName)}.{Quote(_root.TableName)} AS t0");
            foreach (var join in _joinClauses) sql.AppendLine(join);
            sql.AppendLine($"WHERE {where}");
            sql.Append($"ORDER BY t0.{Quote(_root.PrimaryKeyColumn)};");

            return new GeneratedQuery(sql.ToString(), _parameters, columns, flags, definition, _root);
        }

        private List<QueryColumn> BuildColumns()
        {
            var columns = new List<QueryColumn>();
            var byPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            void Add(string path, bool displayed)
            {
                var field = catalog.Resolve(_root.Key, path);
                if (byPath.TryGetValue(field.Path, out var index))
                {
                    if (displayed && !columns[index].IsDisplayed) columns[index] = columns[index] with { IsDisplayed = true };
                    return;
                }
                byPath[field.Path] = columns.Count;
                columns.Add(new QueryColumn("f" + columns.Count, field, displayed));
            }

            var display = definition.DisplayFields is { Count: > 0 }
                ? definition.DisplayFields
                : _root.Fields.Where(f => f.IsDefaultDisplay).OrderBy(f => f.DisplayOrder).Select(f => f.Key).ToList();

            foreach (var path in display) Add(path, displayed: true);
            foreach (var f in _root.Fields.Where(f => f.IsTitle).OrderBy(f => f.DisplayOrder)) Add(f.Key, displayed: false);
            foreach (var c in Conditions(definition.Condition))
            {
                Add(c.Field, displayed: true);
                if (c.Value is { Kind: OperandKind.Field, Field: { } other }) Add(other, displayed: true);
            }
            return columns;
        }

        private string GroupSql(ConditionGroup group)
        {
            var parts = group.Children.Select(child => child switch
            {
                ConditionGroup g => GroupSql(g),
                Condition c => _predicates[c.Id] = ConditionSql(c),
                _ => throw new InvalidOperationException("Unknown node type.")
            });
            var separator = group.Logic == LogicalOperator.And ? " AND " : " OR ";
            return "(" + string.Join(separator, parts) + ")";
        }

        private string ConditionSql(Condition c)
        {
            var field = catalog.Resolve(_root.Key, c.Field);
            var type = field.Field.DataType;
            var left = FieldSql(field);

            return c.Operator switch
            {
                ConditionOperator.Equals => $"{left} = {Right(c.Value!, type)}",
                ConditionOperator.NotEquals => $"{left} <> {Right(c.Value!, type)}",
                ConditionOperator.GreaterThan => $"{left} > {Right(c.Value!, type)}",
                ConditionOperator.GreaterThanOrEqual => $"{left} >= {Right(c.Value!, type)}",
                ConditionOperator.LessThan => $"{left} < {Right(c.Value!, type)}",
                ConditionOperator.LessThanOrEqual => $"{left} <= {Right(c.Value!, type)}",
                ConditionOperator.Between => $"{left} BETWEEN {Right(c.Value!, type)} AND {Right(c.ValueTo!, type)}",
                ConditionOperator.In => $"{left} IN ({string.Join(", ", c.Value!.Values!.Select(v => AddParameter(ValueConverter.Convert(v, type), type)))})",
                ConditionOperator.Contains => $"{left} LIKE {AddParameter("%" + EscapeLike(ConstString(c.Value!)) + "%", type)}",
                ConditionOperator.StartsWith => $"{left} LIKE {AddParameter(EscapeLike(ConstString(c.Value!)) + "%", type)}",
                // "Empty" for text also covers '' and whitespace-only values.
                ConditionOperator.IsEmpty => type == FieldDataType.String
                    ? $"({left} IS NULL OR LTRIM(RTRIM({left})) = N'')"
                    : $"{left} IS NULL",
                ConditionOperator.IsNotEmpty => type == FieldDataType.String
                    ? $"({left} IS NOT NULL AND LTRIM(RTRIM({left})) <> N'')"
                    : $"{left} IS NOT NULL",
                _ => throw new InvalidOperationException($"Unsupported operator {c.Operator}.")
            };
        }

        /// <summary>Right-hand side: another field's expression, or a new typed parameter.</summary>
        private string Right(Operand operand, FieldDataType type) => operand.Kind switch
        {
            OperandKind.Field => FieldSql(catalog.Resolve(_root.Key, operand.Field!)),
            OperandKind.Constant => AddParameter(ValueConverter.Convert(operand.Value, type), type),
            _ => throw new InvalidOperationException("Unexpected operand kind.")
        };

        private static string ConstString(Operand operand) => (string)ValueConverter.Convert(operand.Value, FieldDataType.String);

        private string AddParameter(object value, FieldDataType type)
        {
            var name = "@p" + _parameters.Count;
            _parameters.Add(new QueryParameter(name, value, type));
            return name;
        }

        /// <summary>SQL expression for a field: alias.[column], or a fixed template for a whitelisted computed function.</summary>
        private string FieldSql(ResolvedField field)
        {
            var alias = EnsureJoins(field.Hops);
            var f = field.Field;
            if (!f.IsComputed) return $"{alias}.{Quote(f.ColumnName!)}";

            var source = $"{alias}.{Quote(MetadataCatalog.FindField(field.OwnerEntity, f.FunctionSourceFieldKey!)!.ColumnName!)}";
            const string today = "CAST(GETDATE() AS date)";
            return f.Function switch
            {
                // Full years: year difference minus one if this year's anniversary has not arrived yet.
                ComputedFunction.FullYearsSince =>
                    $"(DATEDIFF(YEAR, {source}, {today}) - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, {source}, {today}), {source}) > {today} THEN 1 ELSE 0 END)",
                ComputedFunction.DaysSince => $"DATEDIFF(DAY, {source}, {today})",
                _ => throw new InvalidOperationException($"Unsupported computed function {f.Function}.")
            };
        }

        /// <summary>
        /// Adds one LEFT JOIN per distinct relationship path (e.g. "Department", "Department.Manager")
        /// and returns the alias of the last entity. LEFT JOIN keeps root records whose relation is missing;
        /// conditions on a missing relation evaluate to UNKNOWN and therefore do not flag the record.
        /// </summary>
        private string EnsureJoins(IReadOnlyList<RelationshipMetadata> hops)
        {
            var path = "";
            var alias = "t0";
            foreach (var rel in hops)
            {
                var nextPath = path.Length == 0 ? rel.Key : path + "." + rel.Key;
                if (!_joinAliases.TryGetValue(nextPath, out var nextAlias))
                {
                    nextAlias = "t" + _joinAliases.Count;
                    var target = catalog.FindEntity(rel.TargetEntityKey)!;
                    _joinClauses.Add(
                        $"LEFT JOIN {Quote(target.SchemaName)}.{Quote(target.TableName)} AS {nextAlias} " +
                        $"ON {nextAlias}.{Quote(rel.ToColumn)} = {alias}.{Quote(rel.FromColumn)}");
                    _joinAliases[nextPath] = nextAlias;
                }
                path = nextPath;
                alias = nextAlias;
            }
            return alias;
        }
    }

    /// <summary>All conditions of a tree in document order.</summary>
    public static IEnumerable<Condition> Conditions(ConditionNode node) => node switch
    {
        Condition c => [c],
        ConditionGroup g => g.Children.SelectMany(Conditions),
        _ => []
    };

    /// <summary>Bracket-quotes an identifier (catalog identifiers are already restricted to [A-Za-z0-9_]).</summary>
    public static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    /// <summary>Escapes LIKE wildcards so user text is matched literally.</summary>
    public static string EscapeLike(string value) =>
        value.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
