using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Tests;

public class MetadataCatalogTests
{
    [Fact]
    public void Resolves_paths_case_insensitively_to_canonical_form_and_hebrew_label()
    {
        var field = TestCatalog.Create().Resolve("employee", "department.manager.isactive");
        Assert.Equal("Department.Manager.IsActive", field.Path);
        Assert.Equal("מחלקה › מנהל המחלקה › סטטוס עובד", field.Label);
        Assert.Equal("Department.Manager", field.RelationshipPath);
    }

    [Fact]
    public void Reachable_fields_include_related_entities()
    {
        var paths = TestCatalog.Create().GetReachableFields("Employee", maxHops: 1).Select(f => f.Path).ToList();
        Assert.Contains("Salary", paths);
        Assert.Contains("Department.IsActive", paths);
        Assert.Contains("Position.MaxSalary", paths);
        Assert.DoesNotContain("Department.Manager.IsActive", paths);   // needs 2 hops
    }

    [Theory]
    [InlineData("Employees]; DROP TABLE x;--")]
    [InlineData("Emp loyees")]
    public void Catalog_with_unsafe_identifier_is_refused(string tableName)
    {
        Assert.Throws<InvalidOperationException>(() => new MetadataCatalog(
        [
            new EntityMetadata("Employee", "עובד", "עובדים", null, "hr", tableName, "EmployeeId",
                [new("Salary", "שכר", FieldDataType.Number, "Salary")], [])
        ]));
    }

    [Fact]
    public void Computed_field_must_reference_a_date_column()
    {
        Assert.Throws<InvalidOperationException>(() => new MetadataCatalog(
        [
            new EntityMetadata("Employee", "עובד", "עובדים", null, "hr", "Employees", "EmployeeId",
            [
                new("Salary", "שכר", FieldDataType.Number, "Salary"),
                new("Age", "גיל", FieldDataType.Number, null, ComputedFunction.FullYearsSince, "Salary")
            ], [])
        ]));
    }

    [Fact]
    public void Relationship_to_unknown_entity_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => new MetadataCatalog(
        [
            new EntityMetadata("Employee", "עובד", "עובדים", null, "hr", "Employees", "EmployeeId",
                [new("Salary", "שכר", FieldDataType.Number, "Salary")],
                [new("Branch", "סניף", "Branch", "BranchId", "BranchId")])
        ]));
    }
}
