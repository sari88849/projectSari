/* =====================================================================
   05 - Sample business data: a mix of valid and deliberately invalid rows.
   Comments mark which example validation each invalid row should violate:
     V1 = under 18 with salary > 15,000
     V2 = employment end date earlier than start date
     V3 = active employee in an inactive department
     V4 = missing mandatory data (national id / email / department / birth date)
     V5 = salary above the maximum of the employee's position   (cross-entity field comparison)
     V6 = active employee whose department OR direct manager is inactive (nested AND/OR)
   Birth dates of young employees are relative to today so "age" checks stay
   correct whenever the scripts are run.
   ===================================================================== */
USE ValidationSystemDb;
GO
SET NOCOUNT ON;

-- Clear existing rows (break the Departments <-> Employees cycle first).
UPDATE hr.Departments SET ManagerEmployeeId = NULL;
UPDATE hr.Employees   SET ManagerId = NULL;
DELETE FROM hr.Employees;
DELETE FROM hr.Departments;
DELETE FROM hr.Positions;
GO

SET IDENTITY_INSERT hr.Departments ON;
INSERT INTO hr.Departments (DepartmentId, Name, Code, IsActive) VALUES
 (1, N'פיתוח תוכנה',     N'DEV', 1),
 (2, N'משאבי אנוש',      N'HR',  1),
 (3, N'כספים',           N'FIN', 1),
 (4, N'שיווק',           N'MKT', 1),
 (5, N'תמיכה טכנית (ישן)', N'SUP', 0),   -- inactive
 (6, N'לוגיסטיקה',       N'LOG', 0);     -- inactive
SET IDENTITY_INSERT hr.Departments OFF;

SET IDENTITY_INSERT hr.Positions ON;
INSERT INTO hr.Positions (PositionId, Title, MinSalary, MaxSalary, IsActive) VALUES
 (1, N'מפתח/ת תוכנה',     12000, 35000, 1),
 (2, N'ראש צוות',         25000, 45000, 1),
 (3, N'רכז/ת משאבי אנוש',  9000, 18000, 1),
 (4, N'חשב/ת',            14000, 30000, 1),
 (5, N'נציג/ת תמיכה',      7000, 12000, 1),
 (6, N'מתמחה',             5000,  9000, 1),
 (7, N'מנהל/ת מחלקה',     30000, 60000, 1);
SET IDENTITY_INSERT hr.Positions OFF;
GO

DECLARE @Today DATE = CAST(GETDATE() AS DATE);

SET IDENTITY_INSERT hr.Employees ON;
INSERT INTO hr.Employees
 (EmployeeId, EmployeeNumber, FirstName, LastName, NationalId, Email, Phone, BirthDate, StartDate, EndDate, Salary, IsActive, DepartmentId, PositionId, ManagerId)
VALUES
 -- Department managers (valid)
 ( 1, N'E1001', N'דוד',    N'כהן',    N'012345674', N'david.cohen@company.co.il',   N'050-1111111', '1975-03-12', '2005-01-01', NULL,         55000, 1, 1, 7, NULL),
 ( 2, N'E1002', N'שרה',    N'לוי',    N'023456785', N'sara.levi@company.co.il',     N'050-2222222', '1980-07-22', '2010-04-01', NULL,         48000, 1, 2, 7, NULL),
 ( 3, N'E1003', N'משה',    N'מזרחי',  N'034567896', N'moshe.mizrahi@company.co.il', N'050-3333333', '1978-11-05', '2008-09-15', NULL,         42000, 1, 3, 7, NULL),
 ( 4, N'E1004', N'רחל',    N'אברהם',  N'045678907', N'rachel.avraham@company.co.il',N'050-4444444', '1983-02-14', '2012-01-10', NULL,         38000, 1, 4, 7, NULL),
 -- Former manager of the inactive support department (inactive employee, valid data)
 ( 5, N'E1005', N'יוסף',   N'פרץ',    N'056789018', N'yosef.peretz@company.co.il',  N'050-5555555', '1970-06-30', '2000-05-01', '2022-12-31', 40000, 0, 5, 7, NULL),
 -- Valid employees
 ( 6, N'E1006', N'נועה',   N'ביטון',  N'067890129', N'noa.biton@company.co.il',     N'052-6666666', '1990-01-20', '2015-03-01', NULL,         30000, 1, 1, 2, 1),
 ( 7, N'E1007', N'איתי',   N'שלום',   N'078901230', N'itay.shalom@company.co.il',   N'052-7777777', '1992-09-09', '2018-06-01', NULL,         22000, 1, 1, 1, 6),
 ( 8, N'E1008', N'מיכל',   N'דהן',    N'089012341', N'michal.dahan@company.co.il',  N'052-8888888', '1995-12-01', '2020-02-15', NULL,         19500, 1, 1, 1, 6),
 -- V5: salary 52,000 > developer max 35,000
 ( 9, N'E1009', N'אורי',   N'גבאי',   N'090123452', N'uri.gabay@company.co.il',     N'052-9999999', '1988-04-17', '2016-08-01', NULL,         52000, 1, 1, 1, 6),
 (10, N'E1010', N'תמר',    N'חדד',    N'101234563', N'tamar.hadad@company.co.il',   N'053-1010101', '1993-05-25', '2019-11-01', NULL,         14000, 1, 2, 3, 2),
 -- V2: end date before start date (inactive)
 (11, N'E1011', N'אבי',    N'אוחיון', N'112345674', N'avi.ohayon@company.co.il',    N'053-1111112', '1985-08-08', '2014-01-01', '2013-06-30', 16000, 0, 3, 4, 3),
 -- V2: end date before start date (inactive)
 (12, N'E1012', N'הילה',   N'ששון',   N'123456785', N'hila.sasson@company.co.il',   N'053-1212121', '1991-03-03', '2017-05-01', '2016-12-31', 15000, 0, 3, 4, 3),
 -- V1 (age 16, salary 16,500) + V5 (intern max 9,000)
 (13, N'E1013', N'יעל',    N'אזולאי', N'134567896', N'yael.azulay@company.co.il',   N'054-1313131', DATEADD(DAY, -30, DATEADD(YEAR, -16, @Today)), DATEADD(MONTH, -6, @Today), NULL, 16500, 1, 1, 6, 6),
 -- V1 (age 17, salary 18,000) + V5
 (14, N'E1014', N'עומר',   N'נחום',   N'145678907', N'omer.nahum@company.co.il',    N'054-1414141', DATEADD(DAY, -100, DATEADD(YEAR, -17, @Today)), DATEADD(MONTH, -3, @Today), NULL, 18000, 1, 4, 6, 4),
 -- Age 17 but salary 9,000: NOT V1 (valid)
 (15, N'E1015', N'שירה',   N'קליין',  N'156789018', N'shira.klein@company.co.il',   N'054-1515151', DATEADD(DAY, -10, DATEADD(YEAR, -17, @Today)), DATEADD(MONTH, -2, @Today), NULL,  9000, 1, 2, 6, 2),
 -- Boundary: age 17 with salary exactly 15,000 -> NOT V1 (needs > 15,000); but V5 (15,000 > 9,000)
 (16, N'E1016', N'גיא',    N'רוזן',   N'167890129', N'guy.rosen@company.co.il',     N'054-1616161', DATEADD(DAY, -200, DATEADD(YEAR, -17, @Today)), DATEADD(MONTH, -4, @Today), NULL, 15000, 1, 4, 6, 4),
 -- Boundary: turns exactly 18 today, salary 20,000 -> NOT V1 (age is 18, not < 18)
 (17, N'E1017', N'ליאור',  N'ברק',    N'178901230', N'lior.barak@company.co.il',    N'054-1717171', DATEADD(YEAR, -18, @Today), DATEADD(MONTH, -1, @Today), NULL, 20000, 1, 1, 1, 6),
 -- V3 (active, department 5 inactive) + V6 (department AND manager inactive -> two reasons)
 (18, N'E1018', N'רוני',   N'אלון',   N'189012341', N'roni.alon@company.co.il',     N'055-1818181', '1987-10-10', '2011-03-01', NULL,          9500, 1, 5, 5, 5),
 -- V3 (active, department 6 inactive) + V4 (email missing) + V6
 (19, N'E1019', N'דנה',    N'פרידמן', N'190123452', NULL,                           N'055-1919191', '1994-02-02', '2021-07-01', NULL,         10000, 1, 6, 5, NULL),
 -- Inactive employee in an inactive department: NOT V3
 (20, N'E1020', N'עמית',   N'שגיא',   N'201234563', N'amit.sagi@company.co.il',     N'055-2020202', '1982-12-12', '2009-01-01', '2023-05-31',  8000, 0, 5, 5, 5),
 -- V4: national id missing
 (21, N'E1021', N'קרן',    N'מלכה',   NULL,         N'keren.malka@company.co.il',   N'056-2121212', '1989-06-06', '2019-01-01', NULL,         21000, 1, 1, 1, 6),
 -- V4: email is an empty string
 (22, N'E1022', N'בני',    N'שמעוני', N'223456785', N'',                            N'056-2222222', '1986-09-19', '2013-10-01', NULL,         17000, 1, 3, 4, 3),
 -- V4: no department assigned
 (23, N'E1023', N'גלית',   N'חזן',    N'234567896', N'galit.hazan@company.co.il',   N'056-2323232', '1990-11-11', '2018-04-01', NULL,         12000, 1, NULL, 3, 2),
 -- V4: birth date missing
 (24, N'E1024', N'אלון',   N'וקנין',  N'245678907', N'alon.vaknin@company.co.il',   N'056-2424242', NULL,         '2020-09-01', NULL,         11000, 1, 2, 3, 2),
 -- V4 with two reasons: national id missing AND email is whitespace only
 (25, N'E1025', N'אסף',    N'טל',     NULL,         N'   ',                         N'057-2525252', '1984-04-04', '2016-02-01', NULL,         20000, 1, 4, 1, 4),
 -- V6: active employee whose direct manager (E1005) is inactive; department is active
 (26, N'E1026', N'מאיה',   N'גולן',   N'267890129', N'maya.golan@company.co.il',    N'057-2626262', '1991-07-07', '2015-05-01', NULL,         25000, 1, 1, 1, 5),
 -- Valid employees
 (27, N'E1027', N'נתן',    N'פישר',   N'278901230', N'natan.fisher@company.co.il',  N'057-2727272', '1983-03-30', '2012-06-01', NULL,         25000, 1, 3, 4, 3),
 (28, N'E1028', N'אביגיל', N'שטרן',   N'289012341', N'avigail.stern@company.co.il', N'058-2828282', '1996-08-15', '2021-03-01', NULL,         16000, 1, 2, 3, 2),
 (29, N'E1029', N'רועי',   N'כץ',     N'290123452', N'roi.katz@company.co.il',      N'058-2929292', '1993-01-25', '2019-09-01', NULL,         24000, 1, 4, 1, 4),
 -- Inactive, no position (LEFT JOIN yields NULL position -> never flagged by V5)
 (30, N'E1030', N'ענבל',   N'שפירא',  N'301234563', N'inbal.shapira@company.co.il', N'058-3030303', '1989-05-05', '2019-01-01', '2024-01-31',  9000, 0, 6, NULL, NULL);
SET IDENTITY_INSERT hr.Employees OFF;

UPDATE hr.Departments SET ManagerEmployeeId = 1 WHERE DepartmentId = 1;
UPDATE hr.Departments SET ManagerEmployeeId = 2 WHERE DepartmentId = 2;
UPDATE hr.Departments SET ManagerEmployeeId = 3 WHERE DepartmentId = 3;
UPDATE hr.Departments SET ManagerEmployeeId = 4 WHERE DepartmentId = 4;
UPDATE hr.Departments SET ManagerEmployeeId = 5 WHERE DepartmentId = 5;
-- Department 6 (Logistics) has no manager.
GO

PRINT 'Business sample data seeded.';
GO
