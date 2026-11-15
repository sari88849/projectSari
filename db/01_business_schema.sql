/* =====================================================================
   01 - Business schema (hr): the sample data that validations run against.
   The validation engine knows nothing about these tables directly; it only
   reaches them through the metadata catalog (meta schema).
   Re-runnable: drops and recreates the hr tables.
   ===================================================================== */
USE ValidationSystemDb;
GO

IF SCHEMA_ID(N'hr') IS NULL EXEC (N'CREATE SCHEMA hr');
GO

-- Drop in dependency order (Departments <-> Employees reference each other).
IF OBJECT_ID(N'hr.FK_Departments_Manager', N'F') IS NOT NULL
    ALTER TABLE hr.Departments DROP CONSTRAINT FK_Departments_Manager;
DROP TABLE IF EXISTS hr.Employees;
DROP TABLE IF EXISTS hr.Positions;
DROP TABLE IF EXISTS hr.Departments;
GO

CREATE TABLE hr.Departments
(
    DepartmentId      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    Name              NVARCHAR(100)     NOT NULL,
    Code              NVARCHAR(20)      NOT NULL CONSTRAINT UQ_Departments_Code UNIQUE,
    IsActive          BIT               NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT (1),
    ManagerEmployeeId INT               NULL      -- FK added after hr.Employees exists
);

CREATE TABLE hr.Positions
(
    PositionId INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Positions PRIMARY KEY,
    Title      NVARCHAR(100)     NOT NULL,
    MinSalary  DECIMAL(10,2)     NOT NULL,
    MaxSalary  DECIMAL(10,2)     NOT NULL,
    IsActive   BIT               NOT NULL CONSTRAINT DF_Positions_IsActive DEFAULT (1),
    CONSTRAINT CK_Positions_SalaryRange CHECK (MinSalary <= MaxSalary)
);

/* Employees intentionally allows NULLs / inconsistent dates: it models data
   imported from external systems, which is exactly what validations detect. */
CREATE TABLE hr.Employees
(
    EmployeeId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
    EmployeeNumber NVARCHAR(20)      NOT NULL CONSTRAINT UQ_Employees_Number UNIQUE,
    FirstName      NVARCHAR(50)      NOT NULL,
    LastName       NVARCHAR(50)      NOT NULL,
    NationalId     NVARCHAR(9)       NULL,
    Email          NVARCHAR(200)     NULL,
    Phone          NVARCHAR(20)      NULL,
    BirthDate      DATE              NULL,
    StartDate      DATE              NULL,
    EndDate        DATE              NULL,
    Salary         DECIMAL(10,2)     NULL,
    IsActive       BIT               NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1),
    DepartmentId   INT               NULL CONSTRAINT FK_Employees_Department REFERENCES hr.Departments (DepartmentId),
    PositionId     INT               NULL CONSTRAINT FK_Employees_Position   REFERENCES hr.Positions (PositionId),
    ManagerId      INT               NULL CONSTRAINT FK_Employees_Manager    REFERENCES hr.Employees (EmployeeId)
);

ALTER TABLE hr.Departments
    ADD CONSTRAINT FK_Departments_Manager FOREIGN KEY (ManagerEmployeeId) REFERENCES hr.Employees (EmployeeId);

-- Indexes on FK columns: the engine joins through these relationships.
CREATE INDEX IX_Employees_DepartmentId ON hr.Employees (DepartmentId);
CREATE INDEX IX_Employees_PositionId   ON hr.Employees (PositionId);
CREATE INDEX IX_Employees_ManagerId    ON hr.Employees (ManagerId);
GO

PRINT 'hr schema created.';
GO
