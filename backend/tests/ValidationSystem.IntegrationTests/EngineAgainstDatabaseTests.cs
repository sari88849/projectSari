using Microsoft.Data.SqlClient;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Results;
using ValidationSystem.Core.Sql;

namespace ValidationSystem.IntegrationTests;

/// <summary>
/// Ad-hoc definitions (not saved) run directly through builder + executor against SQL Server:
/// complex AND/OR, multi-hop relationships, operators, and SQL injection attempts.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class EngineAgainstDatabaseTests(DatabaseFixture fx)
{
    private static Condition Cond(string id, string field, ConditionOperator op, Operand? value = null, Operand? valueTo = null) =>
        new() { Id = id, Field = field, Operator = op, Value = value, ValueTo = valueTo };

    private static ConditionGroup Group(string id, LogicalOperator logic, params ConditionNode[] children) =>
        new() { Id = id, Logic = logic, Children = [.. children] };

    private async Task<(int Total, List<ViolationRecord> Records)> RunAsync(ConditionGroup root, string entity = "Employee")
    {
        var catalog = await fx.Catalog.GetCatalogAsync();
        var query = new SqlQueryBuilder(catalog).Build(new ValidationDefinition { RootEntity = entity, Condition = root });
        var rows = await fx.Executor.ExecuteAsync(query, 5000);
        var processed = new ResultProcessor(catalog).Process(query, rows);
        return (processed.TotalViolations, processed.Records.ToList());
    }

    private async Task<GeneratedQuery> AnyGeneratedQueryAsync()
    {
        var catalog = await fx.Catalog.GetCatalogAsync();
        return new SqlQueryBuilder(catalog).Build(new ValidationDefinition
        {
            RootEntity = "Employee",
            Condition = Group("root", LogicalOperator.And, Cond("c1", "Salary", ConditionOperator.IsEmpty))
        });
    }

    private static int[] Keys(List<ViolationRecord> records) => records.Select(r => int.Parse(r.Key)).ToArray();

    [Fact]
    public async Task Or_of_and_groups_returns_union_with_only_relevant_reasons()
    {
        // (Age < 18 AND Salary > 15000) OR EndDate < StartDate
        var (total, records) = await RunAsync(Group("root", LogicalOperator.Or,
            Group("g1", LogicalOperator.And,
                Cond("c1", "Age", ConditionOperator.LessThan, Operand.Const(18)),
                Cond("c2", "Salary", ConditionOperator.GreaterThan, Operand.Const(15000))),
            Cond("c3", "EndDate", ConditionOperator.LessThan, Operand.FieldRef("StartDate"))));

        Assert.Equal(4, total);
        Assert.Equal([11, 12, 13, 14], Keys(records));
        Assert.Equal(["c3"], records.Single(r => r.Key == "11").Reasons.Select(r => r.ConditionId));
        Assert.Equal(["c1", "c2"], records.Single(r => r.Key == "13").Reasons.Select(r => r.ConditionId));
    }

    [Fact]
    public async Task And_with_nested_or_and_negative_branches()
    {
        // IsActive AND (Salary >= 50000 OR (Department.Code = 'HR' AND Salary < 12000))
        var (_, records) = await RunAsync(Group("root", LogicalOperator.And,
            Cond("c1", "IsActive", ConditionOperator.Equals, Operand.Const(true)),
            Group("g1", LogicalOperator.Or,
                Cond("c2", "Salary", ConditionOperator.GreaterThanOrEqual, Operand.Const(50000)),
                Group("g2", LogicalOperator.And,
                    Cond("c3", "Department.Code", ConditionOperator.Equals, Operand.Const("HR")),
                    Cond("c4", "Salary", ConditionOperator.LessThan, Operand.Const(12000))))));

        // E1001 55,000; E1009 52,000; HR & < 12,000: E1015 9,000, E1024 11,000
        Assert.Equal([1, 9, 15, 24], Keys(records));
        Assert.Equal(["c1", "c3", "c4"], records.Single(r => r.Key == "24").Reasons.Select(r => r.ConditionId));
    }

    [Fact]
    public async Task Two_hop_relationship_is_joined_dynamically()
    {
        // Employees whose department's manager is inactive (Employee -> Department -> Employee)
        var (_, records) = await RunAsync(Group("root", LogicalOperator.And,
            Cond("c1", "Department.Manager.IsActive", ConditionOperator.Equals, Operand.Const(false))));

        Assert.Equal([5, 18, 20], Keys(records));
        Assert.Equal("מחלקה › מנהל המחלקה › סטטוס עובד: לא פעיל", records[0].Reasons.Single().Message);
    }

    [Fact]
    public async Task Missing_relationship_does_not_flag_record()
    {
        // E1023 has no department: "Department.IsActive = false" is UNKNOWN for it, so it must not appear.
        var (_, records) = await RunAsync(Group("root", LogicalOperator.And,
            Cond("c1", "Department.IsActive", ConditionOperator.Equals, Operand.Const(false))));
        Assert.DoesNotContain(23, Keys(records));
        Assert.Equal([5, 18, 19, 20, 30], Keys(records));
    }

    [Fact]
    public async Task Between_in_and_starts_with_operators()
    {
        var between = await RunAsync(Group("root", LogicalOperator.And,
            Cond("c1", "Salary", ConditionOperator.Between, Operand.Const(15000), Operand.Const(16500))));
        Assert.Equal([11, 12, 13, 16, 28], Keys(between.Records));

        var inList = await RunAsync(Group("root", LogicalOperator.And,
            Cond("c1", "DepartmentId", ConditionOperator.In, Operand.List(5, 6))));
        Assert.Equal([5, 18, 19, 20, 30], Keys(inList.Records));

        var startsWith = await RunAsync(Group("root", LogicalOperator.And,
            Cond("c1", "LastName", ConditionOperator.StartsWith, Operand.Const("כ"))));
        Assert.Equal([1, 29], Keys(startsWith.Records));
        Assert.Equal("שם משפחה מתחיל ב-\"כ\" (ערך בפועל: כהן)", startsWith.Records[0].Reasons.Single().Message);
    }

    [Theory]
    [InlineData("x' OR '1'='1")]
    [InlineData("'; DELETE FROM hr.Employees; --")]
    [InlineData("%")]
    public async Task Injection_values_match_literally_and_change_nothing(string malicious)
    {
        var (total, _) = await RunAsync(Group("root", LogicalOperator.Or,
            Cond("c1", "LastName", ConditionOperator.Equals, Operand.Const(malicious)),
            Cond("c2", "Email", ConditionOperator.Contains, Operand.Const(malicious))));

        Assert.Equal(0, total);
        Assert.Equal(30, await CountEmployeesAsync());
    }

    [Fact]
    public async Task Executor_refuses_anything_but_a_single_select()
    {
        var real = await AnyGeneratedQueryAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM hr.Employees;",
            "SELECT 1; DELETE FROM hr.Employees;",
            "SELECT 1 AS [__total] -- comment;",
        })
            await Assert.ThrowsAsync<InvalidOperationException>(() => fx.Executor.ExecuteAsync(real with { Sql = sql }, 10));

        Assert.Equal(30, await CountEmployeesAsync());
    }

    [Fact]
    public async Task Database_permissions_block_anything_beyond_reading_business_data()
    {
        // Even a SELECT that slipped past every application check runs as validation_reader:
        // reading the app schema or creating a table (SELECT INTO) is denied by SQL Server itself.
        var real = await AnyGeneratedQueryAsync();
        var readApp = real with { Sql = "SELECT TOP (@maxRows) 1 AS [__total], v.ValidationId AS [__key] FROM app.Validations AS v;" };
        var selectInto = real with { Sql = "SELECT TOP (@maxRows) e.EmployeeId INTO hr.Stolen FROM hr.Employees AS e;" };

        var ex1 = await Assert.ThrowsAsync<SqlException>(() => fx.Executor.ExecuteAsync(readApp, 10));
        Assert.Contains("permission was denied", ex1.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<SqlException>(() => fx.Executor.ExecuteAsync(selectInto, 10));

        // The connection pool must not leak the impersonated context to the next caller.
        await using var c = await fx.Db.OpenAsync(default);
        await using var cmd = new SqlCommand("SELECT USER_NAME();", c);
        Assert.NotEqual("validation_reader", (string)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Row_cap_limits_fetched_rows_but_total_count_is_exact()
    {
        var catalog = await fx.Catalog.GetCatalogAsync();
        var query = new SqlQueryBuilder(catalog).Build(new ValidationDefinition
        {
            RootEntity = "Employee",
            Condition = Group("root", LogicalOperator.And, Cond("c1", "Salary", ConditionOperator.IsNotEmpty))
        });
        var rows = await fx.Executor.ExecuteAsync(query, maxRows: 5);
        var processed = new ResultProcessor(catalog).Process(query, rows);

        Assert.Equal(5, processed.Records.Count);
        Assert.Equal(30, processed.TotalViolations);
    }

    private async Task<int> CountEmployeesAsync()
    {
        await using var c = await fx.Db.OpenAsync(default);
        await using var cmd = new SqlCommand("SELECT COUNT(*) FROM hr.Employees;", c);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
