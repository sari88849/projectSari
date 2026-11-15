using System.Text.Json;
using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Validation;
using static ValidationSystem.Core.Tests.TestCatalog;

namespace ValidationSystem.Core.Tests;

public class DefinitionValidatorTests
{
    private readonly DefinitionValidator _validator = new(Create());

    private IReadOnlyList<DefinitionError> Validate(params ConditionNode[] children) =>
        _validator.Validate(Definition(LogicalOperator.And, children));

    [Fact]
    public void Valid_definition_has_no_errors()
    {
        Assert.Empty(Validate(
            Cond("c1", "Age", ConditionOperator.LessThan, Operand.Const(18)),
            Group("g1", LogicalOperator.Or,
                Cond("c2", "Department.IsActive", ConditionOperator.Equals, Operand.Const(false)),
                Cond("c3", "EndDate", ConditionOperator.LessThan, Operand.FieldRef("StartDate")))));
    }

    [Fact]
    public void Unknown_root_entity_is_rejected()
    {
        var d = Definition(LogicalOperator.And, Cond("c1", "Salary", ConditionOperator.IsEmpty));
        d.RootEntity = "Customers";
        var error = Assert.Single(_validator.Validate(d));
        Assert.Contains("Customers", error.Message);
    }

    [Theory]
    [InlineData("Bonus")]                    // unknown field
    [InlineData("Supplier.Name")]            // unknown relationship
    [InlineData("Department.Budget")]        // unknown field on related entity
    [InlineData("Manager.Manager.Manager.Manager.IsActive")]   // too many hops
    [InlineData("")]
    public void Unknown_or_invalid_field_paths_are_rejected(string path)
    {
        var errors = Validate(Cond("c1", path, ConditionOperator.IsEmpty));
        Assert.Equal("c1", Assert.Single(errors).NodeId);
    }

    [Fact]
    public void Operator_not_applicable_to_field_type_is_rejected()
    {
        var errors = Validate(Cond("c1", "Salary", ConditionOperator.Contains, Operand.Const("1")));
        Assert.Contains("מכיל", Assert.Single(errors).Message);
    }

    [Theory]
    [InlineData("Salary", "abc")]          // number expected
    [InlineData("StartDate", "31/12/2020")] // ISO date expected
    [InlineData("IsActive", "maybe")]       // boolean expected
    public void Value_of_wrong_type_is_rejected(string field, string value)
    {
        Assert.Single(Validate(Cond("c1", field, ConditionOperator.Equals, Operand.Const(value))));
    }

    [Fact]
    public void Missing_value_is_rejected()
    {
        Assert.Single(Validate(Cond("c1", "Salary", ConditionOperator.GreaterThan)));
    }

    [Fact]
    public void Between_requires_both_bounds()
    {
        Assert.Single(Validate(Cond("c1", "Salary", ConditionOperator.Between, Operand.Const(1))));
    }

    [Fact]
    public void Empty_check_must_not_carry_a_value()
    {
        Assert.Single(Validate(Cond("c1", "Email", ConditionOperator.IsEmpty, Operand.Const("x"))));
    }

    [Fact]
    public void Comparing_fields_of_different_types_is_rejected()
    {
        var error = Assert.Single(Validate(Cond("c1", "Salary", ConditionOperator.GreaterThan, Operand.FieldRef("StartDate"))));
        Assert.Contains("סוגי נתונים שונים", error.Message);
    }

    [Fact]
    public void Field_operand_is_not_allowed_for_list_operators()
    {
        Assert.NotEmpty(Validate(Cond("c1", "DepartmentId", ConditionOperator.In, Operand.FieldRef("Salary"))));
    }

    [Fact]
    public void Empty_group_and_duplicate_ids_are_rejected()
    {
        var errors = Validate(
            Group("g1", LogicalOperator.Or),
            Cond("dup", "Salary", ConditionOperator.IsEmpty),
            Cond("dup", "Email", ConditionOperator.IsEmpty));
        Assert.Equal(2, errors.Count);
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("x'; --")]
    [InlineData("")]
    public void Unsafe_node_ids_are_rejected(string id)
    {
        Assert.NotEmpty(Validate(Cond(id, "Salary", ConditionOperator.IsEmpty)));
    }

    [Fact]
    public void Nesting_deeper_than_limit_is_rejected()
    {
        ConditionNode node = Cond("leaf", "Salary", ConditionOperator.IsEmpty);
        for (var i = 0; i < DefinitionValidator.MaxDepth + 1; i++)
            node = Group("g" + i, LogicalOperator.And, node);
        Assert.Contains(Validate(node), e => e.Message.Contains("עמוק"));
    }

    [Fact]
    public void Too_many_conditions_are_rejected()
    {
        var conditions = Enumerable.Range(0, DefinitionValidator.MaxConditions + 1)
            .Select(i => (ConditionNode)Cond("c" + i, "Salary", ConditionOperator.IsEmpty)).ToArray();
        Assert.Contains(Validate(conditions), e => e.Message.Contains("מספר התנאים"));
    }

    [Fact]
    public void Unknown_display_field_is_rejected()
    {
        var d = Definition(LogicalOperator.And, Cond("c1", "Salary", ConditionOperator.IsEmpty));
        d.DisplayFields = ["Password"];
        Assert.Single(_validator.Validate(d));
    }

    [Theory]
    [InlineData("""{"rootEntity":"Employee","condition":{"type":"group","id":"r","logic":"And","children":[{"type":"condition","id":"c1","field":"Salary","operator":"DropTable"}]}}""")]
    [InlineData("""{"rootEntity":"Employee","condition":{"type":"group","id":"r","logic":"And","children":[{"type":"condition","id":"c1","field":"Salary","operator":99}]}}""")]
    [InlineData("""{"rootEntity":"Employee","condition":{"type":"group","id":"r","logic":"Xor","children":[]}}""")]
    [InlineData("""{"rootEntity":"Employee","condition":{"type":"sql","id":"r","text":"SELECT 1"}}""")]
    [InlineData("""{"rootEntity":"Employee","rawSql":"DELETE FROM hr.Employees","condition":{"type":"group","id":"r","logic":"And","children":[]}}""")]
    public void Unknown_operators_logic_or_node_types_fail_to_parse(string json)
    {
        Assert.ThrowsAny<JsonException>(() => DefinitionJson.Deserialize(json));
    }

    [Fact]
    public void Seed_definition_json_round_trips()
    {
        const string json = """
        {
          "rootEntity": "Employee",
          "condition": { "type": "group", "id": "root", "logic": "And", "children": [
            { "type": "condition", "id": "c1", "field": "IsActive", "operator": "Equals", "value": { "kind": "constant", "value": true } },
            { "type": "group", "id": "g1", "logic": "Or", "children": [
              { "type": "condition", "id": "c2", "field": "Department.IsActive", "operator": "Equals", "value": { "kind": "constant", "value": false } },
              { "id": "c3", "type": "condition", "field": "Salary", "operator": "GreaterThan", "value": { "kind": "field", "field": "Position.MaxSalary" } }
            ] }
          ] }
        }
        """;
        var definition = DefinitionJson.Deserialize(json);
        Assert.Empty(_validator.Validate(definition));

        var again = DefinitionJson.Deserialize(DefinitionJson.Serialize(definition));
        var nested = Assert.IsType<ConditionGroup>(again.RootGroup.Children[1]);
        Assert.Equal(LogicalOperator.Or, nested.Logic);
        Assert.Equal(OperandKind.Field, Assert.IsType<Condition>(nested.Children[1]).Value!.Kind);
    }
}
