/* =====================================================================
   06 - Business metadata catalog content.
   Exposing a new table / field / relationship to business users is done
   by adding rows here - no C# changes are needed.
   ===================================================================== */
USE ValidationSystemDb;
GO
SET NOCOUNT ON;

DELETE FROM meta.Relationships;
DELETE FROM meta.Fields;
DELETE FROM meta.Entities;
GO

INSERT INTO meta.Entities (EntityKey, DisplayName, DisplayNamePlural, Description, SchemaName, TableName, PrimaryKeyColumn) VALUES
 (N'Employee',   N'עובד',   N'עובדים',  N'עובדי הארגון, כולל פרטים אישיים, פרטי העסקה ושכר', N'hr', N'Employees',   N'EmployeeId'),
 (N'Department', N'מחלקה',  N'מחלקות',  N'מחלקות הארגון וסטטוס הפעילות שלהן',               N'hr', N'Departments', N'DepartmentId'),
 (N'Position',   N'תפקיד',  N'תפקידים', N'הגדרות תפקידים וטווחי השכר המותרים לכל תפקיד',    N'hr', N'Positions',   N'PositionId');
GO

DECLARE @Emp INT = (SELECT EntityId FROM meta.Entities WHERE EntityKey = N'Employee');
DECLARE @Dep INT = (SELECT EntityId FROM meta.Entities WHERE EntityKey = N'Department');
DECLARE @Pos INT = (SELECT EntityId FROM meta.Entities WHERE EntityKey = N'Position');

INSERT INTO meta.Fields
 (EntityId, FieldKey, DisplayName, Description, Synonyms, DataType, ColumnName, ComputedFunction, ComputedSourceFieldKey, TrueLabel, FalseLabel, IsMandatory, IsTitle, IsDefaultDisplay, DisplayOrder)
VALUES
 -- Employee
 (@Emp, N'EmployeeNumber', N'מספר עובד',          N'מזהה העובד במערכת השכר',                 N'מספר עובד,קוד עובד',          N'String',  N'EmployeeNumber', NULL, NULL, NULL, NULL, 1, 0, 1,  10),
 (@Emp, N'FirstName',      N'שם פרטי',            NULL,                                      N'שם',                          N'String',  N'FirstName',      NULL, NULL, NULL, NULL, 1, 1, 1,  20),
 (@Emp, N'LastName',       N'שם משפחה',           NULL,                                      N'משפחה',                       N'String',  N'LastName',       NULL, NULL, NULL, NULL, 1, 1, 1,  30),
 (@Emp, N'NationalId',     N'תעודת זהות',         N'מספר תעודת זהות של העובד',               N'ת.ז,תז,מספר זהות',            N'String',  N'NationalId',     NULL, NULL, NULL, NULL, 1, 0, 0,  40),
 (@Emp, N'Email',          N'דואר אלקטרוני',      N'כתובת הדואר האלקטרוני של העובד',         N'מייל,אימייל,דוא"ל',           N'String',  N'Email',          NULL, NULL, NULL, NULL, 1, 0, 0,  50),
 (@Emp, N'Phone',          N'טלפון',              NULL,                                      N'נייד,מספר טלפון',             N'String',  N'Phone',          NULL, NULL, NULL, NULL, 0, 0, 0,  60),
 (@Emp, N'BirthDate',      N'תאריך לידה',         NULL,                                      N'לידה',                        N'Date',    N'BirthDate',      NULL, NULL, NULL, NULL, 1, 0, 0,  70),
 (@Emp, N'Age',            N'גיל',                N'גיל העובד בשנים מלאות, מחושב מתאריך הלידה', N'בן,בת,קטין,קטינים',        N'Number',  NULL, N'FullYearsSince', N'BirthDate', NULL, NULL, 0, 0, 0,  80),
 (@Emp, N'StartDate',      N'תאריך תחילת העסקה',  NULL,                                      N'תחילת עבודה,תאריך התחלה',     N'Date',    N'StartDate',      NULL, NULL, NULL, NULL, 1, 0, 0,  90),
 (@Emp, N'EndDate',        N'תאריך סיום העסקה',   N'ריק עבור עובד שעדיין מועסק',             N'סיום עבודה,תאריך סיום',       N'Date',    N'EndDate',        NULL, NULL, NULL, NULL, 0, 0, 0, 100),
 (@Emp, N'Seniority',      N'ותק (שנים)',         N'שנות ותק מלאות מתאריך תחילת ההעסקה',     N'ותק',                         N'Number',  NULL, N'FullYearsSince', N'StartDate', NULL, NULL, 0, 0, 0, 110),
 (@Emp, N'Salary',         N'שכר חודשי',          N'שכר ברוטו חודשי בש"ח',                   N'שכר,משכורת',                  N'Number',  N'Salary',         NULL, NULL, NULL, NULL, 0, 0, 0, 120),
 (@Emp, N'IsActive',       N'סטטוס עובד',         N'האם העובד פעיל כיום',                    N'פעיל,לא פעיל',                N'Boolean', N'IsActive',       NULL, NULL, N'פעיל', N'לא פעיל', 1, 0, 0, 130),
 (@Emp, N'DepartmentId',   N'שיוך למחלקה',        N'קוד המחלקה אליה משויך העובד',            N'מחלקה',                       N'Number',  N'DepartmentId',   NULL, NULL, NULL, NULL, 1, 0, 0, 140),
 (@Emp, N'PositionId',     N'שיוך לתפקיד',        N'קוד התפקיד של העובד',                    N'תפקיד',                       N'Number',  N'PositionId',     NULL, NULL, NULL, NULL, 0, 0, 0, 150),
 (@Emp, N'ManagerId',      N'שיוך למנהל ישיר',    N'קוד העובד של המנהל הישיר',               N'מנהל',                        N'Number',  N'ManagerId',      NULL, NULL, NULL, NULL, 0, 0, 0, 160),
 -- Department
 (@Dep, N'Name',              N'שם מחלקה',        NULL,                                      N'מחלקה',                       N'String',  N'Name',              NULL, NULL, NULL, NULL, 1, 1, 1, 10),
 (@Dep, N'Code',              N'קוד מחלקה',       NULL,                                      NULL,                           N'String',  N'Code',              NULL, NULL, NULL, NULL, 1, 0, 1, 20),
 (@Dep, N'IsActive',          N'סטטוס מחלקה',     N'האם המחלקה פעילה',                       N'מחלקה פעילה,מחלקה לא פעילה',  N'Boolean', N'IsActive',          NULL, NULL, N'פעילה', N'לא פעילה', 1, 0, 1, 30),
 (@Dep, N'ManagerEmployeeId', N'שיוך למנהל מחלקה', N'קוד העובד של מנהל המחלקה',             N'מנהל מחלקה',                  N'Number',  N'ManagerEmployeeId', NULL, NULL, NULL, NULL, 0, 0, 0, 40),
 -- Position
 (@Pos, N'Title',     N'שם תפקיד',              NULL,                                       N'תפקיד',                       N'String',  N'Title',     NULL, NULL, NULL, NULL, 1, 1, 1, 10),
 (@Pos, N'MinSalary', N'שכר מינימלי לתפקיד',    N'הרף התחתון של טווח השכר לתפקיד',          N'שכר מינימום',                 N'Number',  N'MinSalary', NULL, NULL, NULL, NULL, 1, 0, 1, 20),
 (@Pos, N'MaxSalary', N'שכר מקסימלי לתפקיד',    N'הרף העליון של טווח השכר לתפקיד',          N'שכר מקסימום,תקרת שכר',        N'Number',  N'MaxSalary', NULL, NULL, NULL, NULL, 1, 0, 1, 30),
 (@Pos, N'IsActive',  N'סטטוס תפקיד',           NULL,                                       NULL,                           N'Boolean', N'IsActive',  NULL, NULL, N'פעיל', N'לא פעיל', 1, 0, 0, 40);

INSERT INTO meta.Relationships (FromEntityId, RelationshipKey, DisplayName, Description, ToEntityId, FromColumn, ToColumn) VALUES
 (@Emp, N'Department', N'מחלקה',        N'המחלקה אליה משויך העובד',  @Dep, N'DepartmentId',      N'DepartmentId'),
 (@Emp, N'Position',   N'תפקיד',        N'התפקיד של העובד',          @Pos, N'PositionId',        N'PositionId'),
 (@Emp, N'Manager',    N'מנהל ישיר',    N'המנהל הישיר של העובד',     @Emp, N'ManagerId',         N'EmployeeId'),
 (@Dep, N'Manager',    N'מנהל המחלקה',  N'העובד המנהל את המחלקה',    @Emp, N'ManagerEmployeeId', N'EmployeeId');
GO

PRINT 'Metadata catalog seeded.';
GO
