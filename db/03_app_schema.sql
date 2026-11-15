/* =====================================================================
   03 - Application schema (app): saved validation definitions and
   execution history with result snapshots.
   Re-runnable: drops and recreates the app tables (resets saved data).
   ===================================================================== */
USE ValidationSystemDb;
GO

IF SCHEMA_ID(N'app') IS NULL EXEC (N'CREATE SCHEMA app');
GO

DROP TABLE IF EXISTS app.ValidationRunResults;
DROP TABLE IF EXISTS app.ValidationRuns;
DROP TABLE IF EXISTS app.Validations;
GO

/* A saved validation. DefinitionJson holds the structured definition
   (root entity + condition tree) - never SQL. */
CREATE TABLE app.Validations
(
    ValidationId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Validations PRIMARY KEY,
    Name           NVARCHAR(200)     NOT NULL,
    Description    NVARCHAR(1000)    NULL,
    RootEntityKey  NVARCHAR(50)      NOT NULL,          -- denormalized from the definition for listing/filtering
    DefinitionJson NVARCHAR(MAX)     NOT NULL,
    Source         NVARCHAR(20)      NOT NULL CONSTRAINT DF_Validations_Source DEFAULT (N'Manual'),
    IsActive       BIT               NOT NULL CONSTRAINT DF_Validations_IsActive DEFAULT (1),
    CreatedAt      DATETIME2(0)      NOT NULL CONSTRAINT DF_Validations_CreatedAt DEFAULT (SYSUTCDATETIME()),
    UpdatedAt      DATETIME2(0)      NOT NULL CONSTRAINT DF_Validations_UpdatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Validations_Json   CHECK (ISJSON(DefinitionJson) = 1),
    CONSTRAINT CK_Validations_Source CHECK (Source IN (N'Manual', N'AI'))
);

/* One execution of a validation. DefinitionSnapshotJson keeps the exact
   definition that ran, so history stays meaningful after later edits.
   GeneratedSql is stored for audit/explainability (values are parameters,
   so no data values appear in it). */
CREATE TABLE app.ValidationRuns
(
    RunId                  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ValidationRuns PRIMARY KEY,
    ValidationId           INT              NOT NULL CONSTRAINT FK_ValidationRuns_Validation REFERENCES app.Validations (ValidationId) ON DELETE CASCADE,
    Status                 NVARCHAR(20)     NOT NULL,
    StartedAt              DATETIME2(3)     NOT NULL,
    FinishedAt             DATETIME2(3)     NULL,
    DurationMs             INT              NULL,
    ViolationCount         INT              NULL,      -- total violating records (may exceed stored rows)
    StoredResultCount      INT              NULL,      -- rows stored in the snapshot (max 5,000)
    IsTruncated            BIT              NOT NULL CONSTRAINT DF_ValidationRuns_IsTruncated DEFAULT (0),
    ErrorMessage           NVARCHAR(2000)   NULL,
    GeneratedSql           NVARCHAR(MAX)    NULL,
    DefinitionSnapshotJson NVARCHAR(MAX)    NOT NULL,
    CONSTRAINT CK_ValidationRuns_Status CHECK (Status IN (N'Running', N'Succeeded', N'Failed'))
);
CREATE INDEX IX_ValidationRuns_Validation ON app.ValidationRuns (ValidationId, StartedAt DESC);

/* Snapshot of one violating record.
   RecordDataJson : { "FieldPath": value, ... } for the displayed fields.
   ReasonsJson    : [ { "conditionId": "...", "message": "..." }, ... ]  - every failed condition.
   SearchText     : flattened title + values + reasons, for server-side LIKE search. */
CREATE TABLE app.ValidationRunResults
(
    ResultId       BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ValidationRunResults PRIMARY KEY,
    RunId          BIGINT         NOT NULL CONSTRAINT FK_ValidationRunResults_Run REFERENCES app.ValidationRuns (RunId) ON DELETE CASCADE,
    RowNumber      INT            NOT NULL,
    RecordKey      NVARCHAR(100)  NOT NULL,
    RecordTitle    NVARCHAR(300)  NULL,
    RecordDataJson NVARCHAR(MAX)  NOT NULL,
    ReasonsJson    NVARCHAR(MAX)  NOT NULL,
    SearchText     NVARCHAR(4000) NULL,
    CONSTRAINT CK_ValidationRunResults_Data    CHECK (ISJSON(RecordDataJson) = 1),
    CONSTRAINT CK_ValidationRunResults_Reasons CHECK (ISJSON(ReasonsJson) = 1)
);
CREATE INDEX IX_ValidationRunResults_Run ON app.ValidationRunResults (RunId, RowNumber);
GO

PRINT 'app schema created.';
GO
