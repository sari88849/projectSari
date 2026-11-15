using System.Text.RegularExpressions;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;
using ValidationSystem.Core.Sql;
using ValidationSystem.Core.Validation;
using static ValidationSystem.Core.Tests.TestCatalog;

namespace ValidationSystem.Core.Tests;

public class SqlQueryBuilderTests
{
    private readonly SqlQueryBuilder _builder = new(Create());

    [Fact]
    public void Simple_and_conditions_use_typed_parameters_and_computed_age()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "Age", ConditionOperator.LessThan, Operand.Const(18)),
            Cond("c2", "Salary", ConditionOperator.GreaterThan, Operand.Const(15000))));

        Assert.StartsWith("SELECT TOP (@maxRows)", q.Sql);
        Assert.Contains("FROM [hr].[Employees] AS t0", q.Sql);
        Assert.DoesNotContain("JOIN", q.Sql);
        Assert.Contains("DATEDIFF(YEAR, t0.[BirthDate], CAST(GETDATE() AS date))", q.Sql);
        Assert.Contains("WHERE (", q.Sql);
        Assert.Contains("< @p0 AND t0.[Salary] > @p1)", q.Sql);

        Assert.Collection(q.Parameters,
            p => { Assert.Equal("@p0", p.Name); Assert.Equal(18m, p.Value); Assert.Equal(FieldDataType.Number, p.DataType); },
            p => { Assert.Equal("@p1", p.Name); Assert.Equal(15000m, p.Value); });

        // One flag column per condition, for failure reasons.
        Assert.Equal(["c1", "c2"], q.ConditionFlagAliases.Keys);
        Assert.Contains("CASE WHEN t0.[Salary] > @p1 THEN 1 ELSE 0 END AS [c1]", q.Sql);
        Assert.Contains("COUNT(*) OVER () AS [__total]", q.Sql);
    }

    [Fact]
    public void Field_to_field_comparison_has_no_parameters()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "EndDate", ConditionOperator.LessThan, Operand.FieldRef("StartDate"))));

        Assert.Contains("t0.[EndDate] < t0.[StartDate]", q.Sql);
        Assert.Empty(q.Parameters);
    }

    [Fact]
    public void Relationship_path_generates_left_join_from_catalog()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "IsActive", ConditionOperator.Equals, Operand.Const(true)),
            Cond("c2", "Department.IsActive", ConditionOperator.Equals, Operand.Const(false))));

        Assert.Contains("LEFT JOIN [hr].[Departments] AS t1 ON t1.[DepartmentId] = t0.[DepartmentId]", q.Sql);
        Assert.Contains("(t0.[IsActive] = @p0 AND t1.[IsActive] = @p1)", q.Sql);
        Assert.Equal(true, q.Parameters[0].Value);
        Assert.Equal(FieldDataType.Boolean, q.Parameters[1].DataType);
    }

    [Fact]
    public void Cross_entity_field_comparison_joins_the_related_entity()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "Salary", ConditionOperator.GreaterThan, Operand.FieldRef("Position.MaxSalary"))));

        Assert.Contains("LEFT JOIN [hr].[Positions] AS t1 ON t1.[PositionId] = t0.[PositionId]", q.Sql);
        Assert.Contains("t0.[Salary] > t1.[MaxSalary]", q.Sql);
    }

    [Fact]
    public void Nested_groups_are_parenthesized_and_joins_are_reused()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "IsActive", ConditionOperator.Equals, Operand.Const(true)),
            Group("g1", LogicalOperator.Or,
                Cond("c2", "Department.IsActive", ConditionOperator.Equals, Operand.Const(false)),
                Cond("c3", "Manager.IsActive", ConditionOperator.Equals, Operand.Const(false)),
                Group("g2", LogicalOperator.And,
                    Cond("c4", "Department.Code", ConditionOperator.Equals, Operand.Const("SUP")),
                    Cond("c5", "Salary", ConditionOperator.IsEmpty)))));

        Assert.Contains(
            "WHERE (t0.[IsActive] = @p0 AND (t1.[IsActive] = @p1 OR t2.[IsActive] = @p2 OR (t1.[Code] = @p3 AND t0.[Salary] IS NULL)))",
            q.Sql);
        // Department joined once even though it is used twice.
        Assert.Single(Regex.Matches(q.Sql, @"\[hr\]\.\[Departments\]"));
        Assert.Contains("LEFT JOIN [hr].[Employees] AS t2 ON t2.[EmployeeId] = t0.[ManagerId]", q.Sql);
        Assert.Equal(5, q.ConditionFlagAliases.Count);
    }

    [Fact]
    public void Multi_hop_relationship_chains_joins()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "Department.Manager.IsActive", ConditionOperator.Equals, Operand.Const(false))));

        Assert.Contains("LEFT JOIN [hr].[Departments] AS t1 ON t1.[DepartmentId] = t0.[DepartmentId]", q.Sql);
        Assert.Contains("LEFT JOIN [hr].[Employees] AS t2 ON t2.[EmployeeId] = t1.[ManagerEmployeeId]", q.Sql);
        Assert.Contains("t2.[IsActive] = @p0", q.Sql);
    }

    [Fact]
    public void Empty_check_on_text_treats_whitespace_as_empty()
    {
        var q = _builder.Build(Definition(LogicalOperator.Or,
            Cond("c1", "Email", ConditionOperator.IsEmpty),
            Cond("c2", "BirthDate", ConditionOperator.IsEmpty)));

        Assert.Contains("(t0.[Email] IS NULL OR LTRIM(RTRIM(t0.[Email])) = N'')", q.Sql);
        Assert.Contains("t0.[BirthDate] IS NULL", q.Sql);
        Assert.Contains(" OR ", q.Sql);
    }

    [Fact]
    public void In_and_between_create_one_parameter_per_value()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "DepartmentId", ConditionOperator.In, Operand.List(1, 2, 3)),
            Cond("c2", "StartDate", ConditionOperator.Between, Operand.Const("2020-01-01"), Operand.Const("2020-12-31"))));

        Assert.Contains("t0.[DepartmentId] IN (@p0, @p1, @p2)", q.Sql);
        Assert.Contains("t0.[StartDate] BETWEEN @p3 AND @p4", q.Sql);
        Assert.Equal(new DateTime(2020, 1, 1), q.Parameters[3].Value);
        Assert.Equal(FieldDataType.Date, q.Parameters[3].DataType);
    }

    [Fact]
    public void Contains_escapes_like_wildcards()
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "Email", ConditionOperator.Contains, Operand.Const("50%_[x]"))));

        Assert.Contains("t0.[Email] LIKE @p0", q.Sql);
        Assert.Equal("%50[%][_][[]x]%", q.Parameters[0].Value);
    }

    [Theory]
    [InlineData("'; DROP TABLE hr.Employees; --")]
    [InlineData("x' OR '1'='1")]
    [InlineData("]; DELETE FROM hr.Departments; --")]
    public void Injection_in_values_only_ever_becomes_a_parameter(string malicious)
    {
        var q = _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", "LastName", ConditionOperator.Equals, Operand.Const(malicious)),
            Cond("c2", "Email", ConditionOperator.StartsWith, Operand.Const(malicious))));

        Assert.DoesNotContain(malicious, q.Sql);
        Assert.DoesNotContain("DROP", q.Sql);
        Assert.DoesNotContain("DELETE", q.Sql);
        Assert.Equal(malicious, q.Parameters[0].Value);
        AssertSingleSelect(q.Sql);
    }

    [Theory]
    [InlineData("Salary]; DROP TABLE hr.Employees; --")]
    [InlineData("Salary; DELETE FROM hr.Employees")]
    [InlineData("Department.IsActive) OR (1=1")]
    [InlineData("sys.objects")]
    public void Injection_in_field_names_is_rejected_before_sql_is_built(string maliciousField)
    {
        var ex = Assert.Throws<InvalidDefinitionException>(() => _builder.Build(Definition(LogicalOperator.And,
            Cond("c1", maliciousField, ConditionOperator.Equals, Operand.Const(1)))));
        Assert.NotEmpty(ex.Errors);
    }

    [Fact]
    public void Injection_in_root_entity_is_rejected()
    {
        var definition = Definition(LogicalOperator.And, Cond("c1", "Salary", ConditionOperator.IsEmpty));
        definition.RootEntity = "Employee; DROP TABLE hr.Employees";
        Assert.Throws<InvalidDefinitionException>(() => _builder.Build(definition));
    }

    [Fact]
    public void Generated_sql_is_always_a_single_select()
    {
        var q = _builder.Build(Definition(LogicalOperator.Or,
            Cond("c1", "Age", ConditionOperator.LessThan, Operand.Const(18)),
            Cond("c2", "Department.Manager.IsActive", ConditionOperator.Equals, Operand.Const(false)),
            Cond("c3", "Salary", ConditionOperator.GreaterThan, Operand.FieldRef("Position.MaxSalary"))));

        AssertSingleSelect(q.Sql);
    }

    [Fact]
    public void Display_fields_and_condition_fields_become_columns_without_duplicates()
    {
        var definition = Definition(LogicalOperator.And,
            Cond("c1", "Salary", ConditionOperator.GreaterThan, Operand.FieldRef("Position.MaxSalary")));
        definition.DisplayFields = ["EmployeeNumber", "salary"];   // case-insensitive, deduplicated

        var q = _builder.Build(definition);

        var paths = q.Columns.Select(c => c.Field.Path).ToList();
        Assert.Equal(["EmployeeNumber", "Salary", "FirstName", "LastName", "Position.MaxSalary"], paths);
        Assert.False(q.Columns.Single(c => c.Field.Path == "FirstName").IsDisplayed);   // title only
        Assert.True(q.Columns.Single(c => c.Field.Path == "Position.MaxSalary").IsDisplayed);
    }

    private static void AssertSingleSelect(string sql)
    {
        Assert.StartsWith("SELECT ", sql);
        Assert.Equal(1, sql.Count(ch => ch == ';'));
        Assert.EndsWith(";", sql);
        foreach (var keyword in new[] { "INSERT", "UPDATE", "DELETE", "MERGE", "DROP", "ALTER", "EXEC", "TRUNCATE", "--", "/*" })
            Assert.DoesNotContain(keyword, sql, StringComparison.OrdinalIgnoreCase);
    }
}
