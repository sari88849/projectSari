using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Sql;

namespace ValidationSystem.Core.Results;

/// <summary>One reason a record violates the validation (one failed condition).</summary>
public sealed record FailureReason(string ConditionId, string FieldPath, string Message);

/// <summary>A violating record: key, title, formatted display values (by field path) and all failure reasons.</summary>
public sealed record ViolationRecord(
    string Key,
    string Title,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<FailureReason> Reasons);

/// <summary>A displayed result column.</summary>
public sealed record ResultColumn(string FieldPath, string Label, FieldDataType DataType);

public sealed record ProcessedResults(int TotalViolations, IReadOnlyList<ResultColumn> Columns, IReadOnlyList<ViolationRecord> Records);

/// <summary>
/// Turns raw query rows into business results.
///
/// Failure reasons: each condition has a 0/1 flag column. The condition tree is re-evaluated in memory
/// from those flags, and reasons are collected only from the branches that actually made the record
/// violate the validation. Example: for "A AND (B OR C)" a record where A, B and C are all true gets
/// three reasons; a record where only A and C are true gets two. In "(A AND B) OR C" a record with
/// A=1, B=0, C=1 gets only C - A alone did not cause the violation, so it is not reported.
/// </summary>
public sealed class ResultProcessor(MetadataCatalog catalog)
{
    private readonly ReasonBuilder _reasons = new(catalog);

    /// <param name="rows">Rows keyed by column alias; DB NULLs must already be converted to null.</param>
    public ProcessedResults Process(GeneratedQuery query, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var rootKey = query.RootEntity.Key;
        var aliasByPath = query.Columns.ToDictionary(c => c.Field.Path, c => c.Alias, StringComparer.OrdinalIgnoreCase);
        var columns = query.Columns.Where(c => c.IsDisplayed)
                                   .Select(c => new ResultColumn(c.Field.Path, c.Field.Label, c.Field.Field.DataType))
                                   .ToList();
        var titleColumns = query.RootEntity.Fields.Where(f => f.IsTitle).OrderBy(f => f.DisplayOrder)
                                .Select(f => query.Columns.First(c => c.Field.Path == f.Key)).ToList();

        var records = new List<ViolationRecord>(rows.Count);
        var total = 0;

        foreach (var row in rows)
        {
            total = Convert.ToInt32(row[GeneratedQuery.TotalAlias]);
            var key = Convert.ToString(row[GeneratedQuery.KeyAlias], System.Globalization.CultureInfo.InvariantCulture) ?? "";

            var values = query.Columns.Where(c => c.IsDisplayed)
                .ToDictionary(c => c.Field.Path, c => ValueFormatter.Format(row[c.Alias], c.Field.Field, emptyText: ""));

            var title = string.Join(" ", titleColumns.Select(c => ValueFormatter.Format(row[c.Alias], c.Field.Field, "")))
                              .Trim();

            var reasons = new List<FailureReason>();
            CollectReasons(query.Definition.Condition, row, query, rootKey, aliasByPath, reasons);

            records.Add(new ViolationRecord(key, title.Length > 0 ? title : key, values, reasons));
        }

        return new ProcessedResults(total, columns, records);
    }

    /// <summary>True when this node is satisfied for the row (AND = all children, OR = any child).</summary>
    private static bool IsSatisfied(ConditionNode node, IReadOnlyDictionary<string, object?> row, GeneratedQuery query) => node switch
    {
        Condition c => Convert.ToInt32(row[query.ConditionFlagAliases[c.Id]]) == 1,
        ConditionGroup { Logic: LogicalOperator.And } g => g.Children.All(ch => IsSatisfied(ch, row, query)),
        ConditionGroup g => g.Children.Any(ch => IsSatisfied(ch, row, query)),
        _ => false
    };

    private void CollectReasons(ConditionNode node, IReadOnlyDictionary<string, object?> row, GeneratedQuery query,
                                string rootKey, Dictionary<string, string> aliasByPath, List<FailureReason> reasons)
    {
        if (!IsSatisfied(node, row, query)) return;

        switch (node)
        {
            case ConditionGroup g:
                foreach (var child in g.Children)
                    CollectReasons(child, row, query, rootKey, aliasByPath, reasons);
                break;

            case Condition c:
                var field = catalog.Resolve(rootKey, c.Field);
                var actual = row[aliasByPath[field.Path]];
                object? otherActual = null;
                if (c.Value is { Kind: OperandKind.Field, Field: { } otherPath })
                    otherActual = row[aliasByPath[catalog.Resolve(rootKey, otherPath).Path]];
                reasons.Add(new FailureReason(c.Id, field.Path, _reasons.Explain(rootKey, c, actual, otherActual)));
                break;
        }
    }
}
