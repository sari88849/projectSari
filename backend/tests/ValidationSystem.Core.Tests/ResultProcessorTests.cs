using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Results;
using ValidationSystem.Core.Sql;
using static ValidationSystem.Core.Tests.TestCatalog;

namespace ValidationSystem.Core.Tests;

/// <summary>
/// Failure reasons are generated from the definition + flag columns. Rows here simulate what
/// SQL Server returns for the generated query (keyed by column alias).
/// </summary>
public class ResultProcessorTests
{
    private readonly SqlQueryBuilder _builder = new(Create());
    private readonly ResultProcessor _processor = new(Create());
    private readonly ReasonBuilder _reasons = new(Create());

    private static Dictionary<string, object?> Row(GeneratedQuery q, object key, Dictionary<string, object?> fields, params string[] satisfied)
    {
        var row = new Dictionary<string, object?> { [GeneratedQuery.TotalAlias] = 1, [GeneratedQuery.KeyAlias] = key };
        foreach (var col in q.Columns) row[col.Alias] = fields.GetValueOrDefault(col.Field.Path);
        foreach (var (id, alias) in q.ConditionFlagAliases) row[alias] = satisfied.Contains(id) ? 1 : 0;
        return row;
    }

    [Fact]
    public void Each_failed_condition_gets_a_hebrew_reason_with_actual_values()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "Age", ConditionOperator.LessThan, Operand.Const(18)),
            Cond("c2", "Salary", ConditionOperator.GreaterThan, Operand.Const(15000))));

        var result = _processor.Process(q, [Row(q, 13,
            new() { ["FirstName"] = "יעל", ["LastName"] = "אזולאי", ["Age"] = 16, ["Salary"] = 16500.00m }, "c1", "c2")]);

        var record = Assert.Single(result.Records);
        Assert.Equal("13", record.Key);
        Assert.Equal("יעל אזולאי", record.Title);
        Assert.Equal(["גיל קטן מ-18 (ערך בפועל: 16)", "שכר חודשי גדול מ-15,000 (ערך בפועל: 16,500)"],
                     record.Reasons.Select(r => r.Message));
        Assert.Equal("16,500", record.Values["Salary"]);
    }

    [Fact]
    public void Field_comparison_reason_shows_both_values_as_dates()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "EndDate", ConditionOperator.LessThan, Operand.FieldRef("StartDate"))));

        var result = _processor.Process(q, [Row(q, 11,
            new() { ["EndDate"] = new DateTime(2013, 6, 30), ["StartDate"] = new DateTime(2014, 1, 1) }, "c1")]);

        Assert.Equal("תאריך סיום העסקה (30/06/2013) מוקדם מ-\"תאריך תחילת העסקה\" (01/01/2014)",
                     Assert.Single(Assert.Single(result.Records).Reasons).Message);
    }

    [Fact]
    public void Boolean_and_relationship_reasons_use_business_labels()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "IsActive", ConditionOperator.Equals, Operand.Const(true)),
            Cond("c2", "Department.IsActive", ConditionOperator.Equals, Operand.Const(false))));

        var result = _processor.Process(q, [Row(q, 18,
            new() { ["IsActive"] = true, ["Department.IsActive"] = false }, "c1", "c2")]);

        Assert.Equal(["סטטוס עובד: פעיל", "מחלקה › סטטוס מחלקה: לא פעילה"],
                     Assert.Single(result.Records).Reasons.Select(r => r.Message));
    }

    [Fact]
    public void Or_group_reports_every_condition_that_failed()
    {
        var q = _builder.Build(Definition(LogicalOperator.Or,
            Cond("c1", "NationalId", ConditionOperator.IsEmpty),
            Cond("c2", "Email", ConditionOperator.IsEmpty),
            Cond("c3", "BirthDate", ConditionOperator.IsEmpty)));

        var result = _processor.Process(q, [Row(q, 25, new() { ["Email"] = "   " }, "c1", "c2")]);

        Assert.Equal(["חסר ערך בשדה \"תעודת זהות\"", "חסר ערך בשדה \"דואר אלקטרוני\""],
                     Assert.Single(result.Records).Reasons.Select(r => r.Message));
    }

    [Fact]
    public void Only_branches_that_caused_the_violation_are_reported()
    {
        // (c1 AND c2) OR c3 : row has c1=1, c2=0, c3=1 -> only c3 explains the violation.
        var q = _builder.Build(Definition(LogicalOperator.Or,
            Group("g1", LogicalOperator.And,
                Cond("c1", "IsActive", ConditionOperator.Equals, Operand.Const(true)),
                Cond("c2", "Salary", ConditionOperator.GreaterThan, Operand.Const(50000))),
            Cond("c3", "Email", ConditionOperator.IsEmpty)));

        var result = _processor.Process(q, [Row(q, 1, new() { ["IsActive"] = true, ["Salary"] = 100m }, "c1", "c3")]);

        Assert.Equal(["c3"], Assert.Single(result.Records).Reasons.Select(r => r.ConditionId));
    }

    [Fact]
    public void Nested_and_or_reports_the_and_part_and_every_true_or_branch()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "IsActive", ConditionOperator.Equals, Operand.Const(true)),
            Group("g1", LogicalOperator.Or,
                Cond("c2", "Department.IsActive", ConditionOperator.Equals, Operand.Const(false)),
                Cond("c3", "Manager.IsActive", ConditionOperator.Equals, Operand.Const(false)))));

        var both = _processor.Process(q, [Row(q, 18, new(), "c1", "c2", "c3")]);
        Assert.Equal(["c1", "c2", "c3"], Assert.Single(both.Records).Reasons.Select(r => r.ConditionId));
        Assert.Equal("מנהל ישיר › סטטוס עובד: לא פעיל", both.Records[0].Reasons[2].Message);

        var managerOnly = _processor.Process(q, [Row(q, 26, new(), "c1", "c3")]);
        Assert.Equal(["c1", "c3"], Assert.Single(managerOnly.Records).Reasons.Select(r => r.ConditionId));
    }

    [Fact]
    public void Total_count_comes_from_window_column_and_columns_follow_display_fields()
    {
        var definition = Definition(LogicalOperator.And, Cond("c1", "Salary", ConditionOperator.GreaterThan, Operand.Const(1)));
        definition.DisplayFields = ["EmployeeNumber"];
        var q = _builder.Build(definition);

        var row = Row(q, 1, new() { ["EmployeeNumber"] = "E1", ["Salary"] = 5m }, "c1");
        row[GeneratedQuery.TotalAlias] = 7200;
        var result = _processor.Process(q, [row]);

        Assert.Equal(7200, result.TotalViolations);
        Assert.Equal(["EmployeeNumber", "Salary"], result.Columns.Select(c => c.FieldPath));
        Assert.Equal("שכר חודשי", result.Columns[1].Label);
    }

    [Fact]
    public void Describe_tree_produces_readable_rule_text()
    {
        var definition = Definition(LogicalOperator.And,
            Cond("c1", "IsActive", ConditionOperator.Equals, Operand.Const(true)),
            Group("g1", LogicalOperator.Or,
                Cond("c2", "Department.IsActive", ConditionOperator.Equals, Operand.Const(false)),
                Cond("c3", "Salary", ConditionOperator.Between, Operand.Const(1000), Operand.Const(2000))));

        Assert.Equal("סטטוס עובד: פעיל וגם (מחלקה › סטטוס מחלקה: לא פעילה או שכר חודשי בין 1,000 ל-2,000)",
                     _reasons.DescribeTree("Employee", definition.Condition));
    }
}
