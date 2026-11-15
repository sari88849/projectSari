/* =====================================================================
   07 - Example saved validations.
   Each validation is pure data: a structured JSON definition that the
   generic engine turns into a parameterized SELECT. No SQL is stored.

   Definition format (summary - see docs for the full JSON schema):
   {
     "rootEntity": "<EntityKey>",
     "condition":  <group>,                          -- root is always a group
     "displayFields": ["<field path>", ...]          -- optional
   }
   group     : { "type": "group", "id": "...", "logic": "And" | "Or", "children": [ <group> | <condition>, ... ] }
   condition : { "type": "condition", "id": "...", "field": "<field path>", "operator": "<Operator>",
                 "value": <operand>?, "valueTo": <operand>? }
   operand   : { "kind": "constant", "value": <json value> }
             | { "kind": "list",     "values": [ ... ] }               -- for In
             | { "kind": "field",    "field": "<field path>" }         -- compare two fields
   field path: "<FieldKey>" on the root entity, or "<RelationshipKey>.<...>.<FieldKey>"
               e.g. "Department.IsActive", "Position.MaxSalary", "Department.Manager.IsActive"
   ===================================================================== */
USE ValidationSystemDb;
GO
SET NOCOUNT ON;

DELETE FROM app.Validations;   -- cascades to runs and run results
GO

INSERT INTO app.Validations (Name, Description, RootEntityKey, DefinitionJson) VALUES

-- V1: two conditions combined with AND, one of them on a computed field (Age)
(N'עובדים קטינים עם שכר גבוה',
 N'עובדים מתחת לגיל 18 ששכרם החודשי גבוה מ-15,000 ש"ח',
 N'Employee',
 N'{
  "rootEntity": "Employee",
  "condition": { "type": "group", "id": "root", "logic": "And", "children": [
    { "type": "condition", "id": "c1", "field": "Age",    "operator": "LessThan",    "value": { "kind": "constant", "value": 18 } },
    { "type": "condition", "id": "c2", "field": "Salary", "operator": "GreaterThan", "value": { "kind": "constant", "value": 15000 } }
  ] },
  "displayFields": ["EmployeeNumber", "FirstName", "LastName", "Age", "Salary"]
}'),

-- V2: comparison between two fields of the same record
(N'תאריך סיום לפני תאריך התחלה',
 N'עובדים שתאריך סיום ההעסקה שלהם מוקדם מתאריך תחילת ההעסקה',
 N'Employee',
 N'{
  "rootEntity": "Employee",
  "condition": { "type": "group", "id": "root", "logic": "And", "children": [
    { "type": "condition", "id": "c1", "field": "EndDate", "operator": "LessThan", "value": { "kind": "field", "field": "StartDate" } }
  ] },
  "displayFields": ["EmployeeNumber", "FirstName", "LastName", "StartDate", "EndDate"]
}'),

-- V3: condition through a relationship (Employee -> Department)
(N'עובדים פעילים במחלקות לא פעילות',
 N'עובדים פעילים המשויכים למחלקה שאינה פעילה',
 N'Employee',
 N'{
  "rootEntity": "Employee",
  "condition": { "type": "group", "id": "root", "logic": "And", "children": [
    { "type": "condition", "id": "c1", "field": "IsActive",            "operator": "Equals", "value": { "kind": "constant", "value": true } },
    { "type": "condition", "id": "c2", "field": "Department.IsActive", "operator": "Equals", "value": { "kind": "constant", "value": false } }
  ] },
  "displayFields": ["EmployeeNumber", "FirstName", "LastName", "Department.Name"]
}'),

-- V4: OR of null/empty checks; a record may fail several of them
(N'נתוני חובה חסרים',
 N'עובדים שחסר להם לפחות אחד מנתוני החובה: תעודת זהות, דואר אלקטרוני, שיוך למחלקה או תאריך לידה',
 N'Employee',
 N'{
  "rootEntity": "Employee",
  "condition": { "type": "group", "id": "root", "logic": "Or", "children": [
    { "type": "condition", "id": "c1", "field": "NationalId",   "operator": "IsEmpty" },
    { "type": "condition", "id": "c2", "field": "Email",        "operator": "IsEmpty" },
    { "type": "condition", "id": "c3", "field": "DepartmentId", "operator": "IsEmpty" },
    { "type": "condition", "id": "c4", "field": "BirthDate",    "operator": "IsEmpty" }
  ] },
  "displayFields": ["EmployeeNumber", "FirstName", "LastName", "NationalId", "Email", "BirthDate"]
}'),

-- V5: field-to-field comparison across a relationship (Employee.Salary vs Position.MaxSalary)
(N'שכר מעל תקרת התפקיד',
 N'עובדים ששכרם החודשי גבוה מהשכר המקסימלי שהוגדר לתפקיד שלהם',
 N'Employee',
 N'{
  "rootEntity": "Employee",
  "condition": { "type": "group", "id": "root", "logic": "And", "children": [
    { "type": "condition", "id": "c1", "field": "Salary", "operator": "GreaterThan", "value": { "kind": "field", "field": "Position.MaxSalary" } }
  ] },
  "displayFields": ["EmployeeNumber", "FirstName", "LastName", "Position.Title", "Salary", "Position.MaxSalary"]
}'),

-- V6: nested groups - AND containing an OR, through two different relationships
(N'עובד פעיל תחת מחלקה או מנהל לא פעילים',
 N'עובדים פעילים שהמחלקה שלהם או המנהל הישיר שלהם אינם פעילים',
 N'Employee',
 N'{
  "rootEntity": "Employee",
  "condition": { "type": "group", "id": "root", "logic": "And", "children": [
    { "type": "condition", "id": "c1", "field": "IsActive", "operator": "Equals", "value": { "kind": "constant", "value": true } },
    { "type": "group", "id": "g1", "logic": "Or", "children": [
      { "type": "condition", "id": "c2", "field": "Department.IsActive", "operator": "Equals", "value": { "kind": "constant", "value": false } },
      { "type": "condition", "id": "c3", "field": "Manager.IsActive",    "operator": "Equals", "value": { "kind": "constant", "value": false } }
    ] }
  ] },
  "displayFields": ["EmployeeNumber", "FirstName", "LastName", "Department.Name", "Manager.LastName"]
}'),

-- V7: a different root entity (Department -> Manager relationship)
(N'מחלקות שמנהלן אינו פעיל',
 N'מחלקות שהעובד המוגדר כמנהל שלהן אינו פעיל',
 N'Department',
 N'{
  "rootEntity": "Department",
  "condition": { "type": "group", "id": "root", "logic": "And", "children": [
    { "type": "condition", "id": "c1", "field": "Manager.IsActive", "operator": "Equals", "value": { "kind": "constant", "value": false } }
  ] },
  "displayFields": ["Code", "Name", "IsActive", "Manager.FirstName", "Manager.LastName"]
}');
GO

PRINT 'Example validations seeded.';
GO
