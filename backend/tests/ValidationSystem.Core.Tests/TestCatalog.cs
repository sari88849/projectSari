using ValidationSystem.Core.Definitions;
using ValidationSystem.Core.Metadata;

namespace ValidationSystem.Core.Tests;

/// <summary>In-memory catalog mirroring db/06_seed_metadata.sql (subset used by tests).</summary>
public static class TestCatalog
{
    public static MetadataCatalog Create() => new(
    [
        new EntityMetadata("Employee", "עובד", "עובדים", null, "hr", "Employees", "EmployeeId",
        [
            new("EmployeeNumber", "מספר עובד", FieldDataType.String, "EmployeeNumber", IsDefaultDisplay: true, DisplayOrder: 10),
            new("FirstName", "שם פרטי", FieldDataType.String, "FirstName", IsTitle: true, IsDefaultDisplay: true, DisplayOrder: 20),
            new("LastName", "שם משפחה", FieldDataType.String, "LastName", IsTitle: true, IsDefaultDisplay: true, DisplayOrder: 30),
            new("NationalId", "תעודת זהות", FieldDataType.String, "NationalId"),
            new("Email", "דואר אלקטרוני", FieldDataType.String, "Email"),
            new("BirthDate", "תאריך לידה", FieldDataType.Date, "BirthDate"),
            new("Age", "גיל", FieldDataType.Number, null, ComputedFunction.FullYearsSince, "BirthDate"),
            new("StartDate", "תאריך תחילת העסקה", FieldDataType.Date, "StartDate"),
            new("EndDate", "תאריך סיום העסקה", FieldDataType.Date, "EndDate"),
            new("Salary", "שכר חודשי", FieldDataType.Number, "Salary"),
            new("IsActive", "סטטוס עובד", FieldDataType.Boolean, "IsActive", TrueLabel: "פעיל", FalseLabel: "לא פעיל"),
            new("DepartmentId", "שיוך למחלקה", FieldDataType.Number, "DepartmentId"),
        ],
        [
            new("Department", "מחלקה", "Department", "DepartmentId", "DepartmentId"),
            new("Position", "תפקיד", "Position", "PositionId", "PositionId"),
            new("Manager", "מנהל ישיר", "Employee", "ManagerId", "EmployeeId"),
        ]),
        new EntityMetadata("Department", "מחלקה", "מחלקות", null, "hr", "Departments", "DepartmentId",
        [
            new("Name", "שם מחלקה", FieldDataType.String, "Name", IsTitle: true, IsDefaultDisplay: true),
            new("Code", "קוד מחלקה", FieldDataType.String, "Code"),
            new("IsActive", "סטטוס מחלקה", FieldDataType.Boolean, "IsActive", TrueLabel: "פעילה", FalseLabel: "לא פעילה"),
        ],
        [
            new("Manager", "מנהל המחלקה", "Employee", "ManagerEmployeeId", "EmployeeId"),
        ]),
        new EntityMetadata("Position", "תפקיד", "תפקידים", null, "hr", "Positions", "PositionId",
        [
            new("Title", "שם תפקיד", FieldDataType.String, "Title", IsTitle: true),
            new("MaxSalary", "שכר מקסימלי לתפקיד", FieldDataType.Number, "MaxSalary"),
        ],
        []),
    ]);

    /// <summary>Shorthand to build a definition with a root AND/OR group.</summary>
    public static ValidationDefinition Definition(LogicalOperator logic, params ConditionNode[] children) => new()
    {
        RootEntity = "Employee",
        Condition = Group("root", logic, children)
    };

    public static ConditionGroup Group(string id, LogicalOperator logic, params ConditionNode[] children) =>
        new() { Id = id, Logic = logic, Children = [.. children] };

    public static Condition Cond(string id, string field, ConditionOperator op, Operand? value = null, Operand? valueTo = null) =>
        new() { Id = id, Field = field, Operator = op, Value = value, ValueTo = valueTo };
}
