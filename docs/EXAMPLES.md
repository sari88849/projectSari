# Example validations (controls)

These are the assignment's configurable, executable *controls* (בקרות), called **validations** in the implementation. All examples are stored as data in `app.Validations` (see `db/07_seed_validations.sql`). They run through the same generic engine; none of them has dedicated code. Expected results refer to the seed data in `db/05_seed_business_data.sql`. They are checked by `db/99_verify.sql` (hand-written SQL) and by `ExampleValidationsTests` (the generated SQL against SQL Server). V1–V5 are also produced from Hebrew text through the AI flow in `AiEndToEndTests`, which checks that they return the same records.

## 1. The four required examples

### V1: Employees younger than 18 with a salary greater than 15,000
`Age` is a computed field, `FullYearsSince(BirthDate)`, defined in metadata.
```json
{ "type": "group", "id": "root", "logic": "And", "children": [
  { "type": "condition", "id": "c1", "field": "Age",    "operator": "LessThan",    "value": { "kind": "constant", "value": 18 } },
  { "type": "condition", "id": "c2", "field": "Salary", "operator": "GreaterThan", "value": { "kind": "constant", "value": 15000 } } ] }
```
**Result:** E1013, E1014.
**Not returned (edge cases):** E1015 (age 17, salary 9,000), E1016 (age 17, salary *exactly* 15,000), E1017 (turns 18 today).
**Reasons (E1013):** `גיל קטן מ-18 (ערך בפועל: 16)` and `שכר חודשי גדול מ-15,000 (ערך בפועל: 16,500)`.

### V2: Employment end date earlier than start date
A comparison between two fields.
```json
{ "type": "condition", "id": "c1", "field": "EndDate", "operator": "LessThan", "value": { "kind": "field", "field": "StartDate" } }
```
**Result:** E1011, E1012. Employees without an end date are not flagged.
**Reason:** `תאריך סיום העסקה (30/06/2013) מוקדם מ-"תאריך תחילת העסקה" (01/01/2014)`.

### V3: Active employees assigned to inactive departments
A condition through a relationship.
```json
{ "type": "condition", "id": "c1", "field": "IsActive",            "operator": "Equals", "value": { "kind": "constant", "value": true } },
{ "type": "condition", "id": "c2", "field": "Department.IsActive", "operator": "Equals", "value": { "kind": "constant", "value": false } }
```
**Result:** E1018, E1019.
**Not returned:** E1020 / E1030 (inactive employees in inactive departments), E1023 (no department).
**Reasons:** `סטטוס עובד: פעיל` and `מחלקה › סטטוס מחלקה: לא פעילה`.

### V4: Records missing mandatory business data
An OR of empty checks; a record can fail several of them.
```json
{ "type": "group", "id": "root", "logic": "Or", "children": [
  { "type": "condition", "id": "c1", "field": "NationalId",   "operator": "IsEmpty" },
  { "type": "condition", "id": "c2", "field": "Email",        "operator": "IsEmpty" },
  { "type": "condition", "id": "c3", "field": "DepartmentId", "operator": "IsEmpty" },
  { "type": "condition", "id": "c4", "field": "BirthDate",    "operator": "IsEmpty" } ] }
```
**Result:** E1019 (email NULL), E1021 (national id NULL), E1022 (email `''`), E1023 (no department), E1024 (no birth date), E1025.
**E1025 gets two reasons:** `חסר ערך בשדה "תעודת זהות"` and `חסר ערך בשדה "דואר אלקטרוני"`. Its email is whitespace only, which counts as empty.

## 2. Additional examples

| # | Validation | Shows | Result |
|---|---|---|---|
| V5 | Salary above the employee's position maximum: `Salary > Position.MaxSalary` | Field-to-field comparison across entities | E1009, E1013, E1014, E1016 |
| V6 | `IsActive = true AND (Department.IsActive = false OR Manager.IsActive = false)` | Nested groups, two relationships, multiple reasons | E1018 (3 reasons), E1019, E1026 (2 reasons) |
| V7 | Departments whose manager is inactive: `Manager.IsActive = false` on root entity **Department** | A different root entity | SUP (תמיכה טכנית) |

Ad-hoc definitions covered by the integration tests (`EngineAgainstDatabaseTests`):
- `(Age < 18 AND Salary > 15000) OR EndDate < StartDate` returns 11, 12, 13, 14. Record 11 reports only the OR branch that applies to it.
- `IsActive AND (Salary >= 50000 OR (Department.Code = 'HR' AND Salary < 12000))` returns 1, 9, 15, 24.
- `Department.Manager.IsActive = false` (two hops) returns 5, 18, 20.
- `Salary BETWEEN 15000 AND 16500`, `DepartmentId IN (5, 6)`, and `LastName StartsWith 'כ'`.
- Injection values such as `x' OR '1'='1` and `'; DELETE FROM hr.Employees; --` match literally and return 0 rows; the table is unchanged.

## 3. Building V3 in the UI (no SQL)

1. Go to **בדיקה חדשה** (new validation), enter a name, and choose the entity **עובדים** (employees).
2. Condition 1: **שדה** (field) = `סטטוס עובד` (employee status), **תנאי** (condition) = `שווה ל` (equals), **ערך** (value) = `פעיל` (active).
3. **הוספת תנאי** (add condition). Condition 2: field `סטטוס מחלקה` (department status, listed under the "מחלקה" group), equals, value `לא פעילה` (inactive).
4. The preview shows: *סטטוס עובד: פעיל וגם מחלקה › סטטוס מחלקה: לא פעילה*. Click **שמירה והרצה** (save and run). The results show 2 records, each with its reasons.

To build V2 instead, choose field `תאריך סיום העסקה` (employment end date) and condition `מוקדם מ` (earlier than), switch the toggle from **ערך** (value) to **שדה** (field), and pick `תאריך תחילת העסקה` (employment start date).

## 4. Adding a new entity without code

Example: expose a new table `hr.Projects`, with a many-to-one relationship from employees.

```sql
INSERT INTO meta.Entities (EntityKey, DisplayName, DisplayNamePlural, Description, SchemaName, TableName, PrimaryKeyColumn)
VALUES (N'Project', N'פרויקט', N'פרויקטים', N'פרויקטים בארגון', N'hr', N'Projects', N'ProjectId');

DECLARE @P INT = SCOPE_IDENTITY();
DECLARE @E INT = (SELECT EntityId FROM meta.Entities WHERE EntityKey = N'Employee');

INSERT INTO meta.Fields (EntityId, FieldKey, DisplayName, DataType, ColumnName, IsTitle, IsDefaultDisplay)
VALUES (@P, N'Name',    N'שם פרויקט',  N'String', N'Name',    1, 1),
       (@P, N'EndDate', N'תאריך סיום', N'Date',   N'EndDate', 0, 1);

INSERT INTO meta.Relationships (FromEntityId, RelationshipKey, DisplayName, ToEntityId, FromColumn, ToColumn)
VALUES (@E, N'Project', N'פרויקט', @P, N'ProjectId', N'ProjectId');
```

Then call `POST /api/metadata/refresh`. The new fields appear in the builder (e.g. `פרויקט › תאריך סיום`, project › end date) and can be used in conditions such as `Project.EndDate < EndDate`. Grant `SELECT` to `validation_reader` if the table is in a different schema.
