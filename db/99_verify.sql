/* =====================================================================
   99 - Verification of the database phase.
   1. Re-checks every example validation with HAND-WRITTEN SQL and compares
      the violating records to the expected list documented in
      05_seed_business_data.sql. The same expectations are later used by the
      engine's integration tests (generated SQL must return the same rows).
   2. Verifies the metadata catalog matches the physical schema.
   3. Verifies validation_reader can only SELECT from hr.
   Any mismatch raises an error (sqlcmd -b makes the run fail).
   Uses FOR XML PATH (not STRING_AGG) so it also runs on SQL Server 2016 / LocalDB 2016.
   ===================================================================== */
USE ValidationSystemDb;
GO
SET NOCOUNT ON;

DECLARE @Today DATE = CAST(GETDATE() AS DATE);
DECLARE @Checks TABLE (Name NVARCHAR(10), Expected NVARCHAR(400), Actual NVARCHAR(400));

-- Age in full years (same semantics the engine's FullYearsSince function will use)
;WITH E AS (
    SELECT e.*,
           DATEDIFF(YEAR, e.BirthDate, @Today)
             - CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, e.BirthDate, @Today), e.BirthDate) > @Today THEN 1 ELSE 0 END AS Age
    FROM hr.Employees e
)
INSERT INTO @Checks
SELECT N'V1', N'E1013,E1014',
       (SELECT STUFF((SELECT N',' + EmployeeNumber FROM E WHERE Age < 18 AND Salary > 15000
        ORDER BY EmployeeNumber FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''));

INSERT INTO @Checks
SELECT N'V2', N'E1011,E1012',
       (SELECT STUFF((SELECT N',' + EmployeeNumber FROM hr.Employees WHERE EndDate < StartDate
        ORDER BY EmployeeNumber FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''));

INSERT INTO @Checks
SELECT N'V3', N'E1018,E1019',
       (SELECT STUFF((SELECT N',' + e.EmployeeNumber
        FROM hr.Employees e LEFT JOIN hr.Departments d ON d.DepartmentId = e.DepartmentId
        WHERE e.IsActive = 1 AND d.IsActive = 0
        ORDER BY e.EmployeeNumber FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''));

INSERT INTO @Checks
SELECT N'V4', N'E1019,E1021,E1022,E1023,E1024,E1025',
       (SELECT STUFF((SELECT N',' + EmployeeNumber FROM hr.Employees
        WHERE NULLIF(LTRIM(RTRIM(NationalId)), N'') IS NULL
           OR NULLIF(LTRIM(RTRIM(Email)), N'') IS NULL
           OR DepartmentId IS NULL
           OR BirthDate IS NULL
        ORDER BY EmployeeNumber FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''));

INSERT INTO @Checks
SELECT N'V5', N'E1009,E1013,E1014,E1016',
       (SELECT STUFF((SELECT N',' + e.EmployeeNumber
        FROM hr.Employees e LEFT JOIN hr.Positions p ON p.PositionId = e.PositionId
        WHERE e.Salary > p.MaxSalary
        ORDER BY e.EmployeeNumber FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''));

INSERT INTO @Checks
SELECT N'V6', N'E1018,E1019,E1026',
       (SELECT STUFF((SELECT N',' + e.EmployeeNumber
        FROM hr.Employees e
        LEFT JOIN hr.Departments d ON d.DepartmentId = e.DepartmentId
        LEFT JOIN hr.Employees   m ON m.EmployeeId   = e.ManagerId
        WHERE e.IsActive = 1 AND (d.IsActive = 0 OR m.IsActive = 0)
        ORDER BY e.EmployeeNumber FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''));

INSERT INTO @Checks
SELECT N'V7', N'SUP',
       (SELECT STUFF((SELECT N',' + d.Code
        FROM hr.Departments d LEFT JOIN hr.Employees m ON m.EmployeeId = d.ManagerEmployeeId
        WHERE m.IsActive = 0
        ORDER BY d.Code FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N''));

SELECT Name, Expected, Actual, CASE WHEN Expected = ISNULL(Actual, N'') THEN N'OK' ELSE N'MISMATCH' END AS Result
FROM @Checks ORDER BY Name;

IF EXISTS (SELECT 1 FROM @Checks WHERE Expected <> ISNULL(Actual, N''))
    THROW 50001, 'Example validation expectations do not match the seed data.', 1;

-- Saved validations: 7 definitions, all valid JSON with a root entity known to the catalog.
IF (SELECT COUNT(*) FROM app.Validations v
    JOIN meta.Entities en ON en.EntityKey = JSON_VALUE(v.DefinitionJson, '$.rootEntity')
    WHERE en.EntityKey = v.RootEntityKey) <> 7
    THROW 50002, 'Saved validations are missing or reference an unknown root entity.', 1;
GO

-- Metadata catalog vs. physical schema: every catalog table/column must exist.
DECLARE @Broken NVARCHAR(MAX) =
(
    SELECT STUFF((SELECT N'; ' + Item FROM (
        SELECT N'Entity ' + e.EntityKey AS Item
        FROM meta.Entities e
        WHERE COL_LENGTH(QUOTENAME(e.SchemaName) + N'.' + QUOTENAME(e.TableName), e.PrimaryKeyColumn) IS NULL
        UNION ALL
        SELECT N'Field ' + e.EntityKey + N'.' + f.FieldKey
        FROM meta.Fields f JOIN meta.Entities e ON e.EntityId = f.EntityId
        WHERE f.ColumnName IS NOT NULL
          AND COL_LENGTH(QUOTENAME(e.SchemaName) + N'.' + QUOTENAME(e.TableName), f.ColumnName) IS NULL
        UNION ALL
        SELECT N'Computed ' + e.EntityKey + N'.' + f.FieldKey
        FROM meta.Fields f JOIN meta.Entities e ON e.EntityId = f.EntityId
        WHERE f.ComputedSourceFieldKey IS NOT NULL
          AND NOT EXISTS (SELECT 1 FROM meta.Fields s WHERE s.EntityId = f.EntityId AND s.FieldKey = f.ComputedSourceFieldKey AND s.ColumnName IS NOT NULL)
        UNION ALL
        SELECT N'Relationship ' + fe.EntityKey + N'.' + r.RelationshipKey
        FROM meta.Relationships r
        JOIN meta.Entities fe ON fe.EntityId = r.FromEntityId
        JOIN meta.Entities te ON te.EntityId = r.ToEntityId
        WHERE COL_LENGTH(QUOTENAME(fe.SchemaName) + N'.' + QUOTENAME(fe.TableName), r.FromColumn) IS NULL
           OR COL_LENGTH(QUOTENAME(te.SchemaName) + N'.' + QUOTENAME(te.TableName), r.ToColumn) IS NULL
    ) x FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N'')
);
IF @Broken IS NOT NULL
BEGIN
    DECLARE @Msg NVARCHAR(2048) = N'Metadata catalog references missing objects: ' + @Broken;
    THROW 50003, @Msg, 1;
END
PRINT 'Metadata catalog matches the physical schema.';
GO

-- Least privilege: validation_reader can SELECT hr, but cannot modify data or read meta/app.
DECLARE @Count INT, @Denied INT = 0;
EXECUTE AS USER = N'validation_reader';
    SELECT @Count = COUNT(*) FROM hr.Employees;
    BEGIN TRY UPDATE hr.Employees SET Salary = Salary WHERE 1 = 0; END TRY BEGIN CATCH SET @Denied += 1; END CATCH
    BEGIN TRY DELETE FROM hr.Departments WHERE 1 = 0;               END TRY BEGIN CATCH SET @Denied += 1; END CATCH
    BEGIN TRY SELECT @Count = COUNT(*) FROM app.Validations;        END TRY BEGIN CATCH SET @Denied += 1; END CATCH
    BEGIN TRY SELECT @Count = COUNT(*) FROM meta.Fields;            END TRY BEGIN CATCH SET @Denied += 1; END CATCH
REVERT;
IF @Denied <> 4
    THROW 50004, 'validation_reader has more permissions than expected.', 1;
PRINT 'validation_reader: SELECT on hr allowed; UPDATE/DELETE and meta/app access denied (4/4).';
GO

PRINT 'ALL DATABASE CHECKS PASSED.';
GO
