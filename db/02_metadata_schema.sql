/* =====================================================================
   02 - Business metadata catalog (meta).
   This is the ONLY place where physical table/column names appear.
   Business users see DisplayName (Hebrew) and the engine resolves
   EntityKey / FieldKey / RelationshipKey to physical names through here.
   At runtime the engine also cross-checks this catalog against the real
   database schema (sys.columns), so a typo here cannot reach a query.
   Re-runnable: drops and recreates the meta tables.
   ===================================================================== */
USE ValidationSystemDb;
GO

IF SCHEMA_ID(N'meta') IS NULL EXEC (N'CREATE SCHEMA meta');
GO

DROP TABLE IF EXISTS meta.Relationships;
DROP TABLE IF EXISTS meta.Fields;
DROP TABLE IF EXISTS meta.Entities;
GO

-- A business entity = one table/view exposed to business users.
CREATE TABLE meta.Entities
(
    EntityId          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Entities PRIMARY KEY,
    EntityKey         NVARCHAR(50)      NOT NULL CONSTRAINT UQ_Entities_Key UNIQUE,   -- stable logical name used in definitions
    DisplayName       NVARCHAR(100)     NOT NULL,                                     -- Hebrew, singular
    DisplayNamePlural NVARCHAR(100)     NOT NULL,                                     -- Hebrew, plural
    Description       NVARCHAR(500)     NULL,                                         -- business description (UI + AI context)
    SchemaName        SYSNAME           NOT NULL,
    TableName         SYSNAME           NOT NULL,
    PrimaryKeyColumn  SYSNAME           NOT NULL,
    IsActive          BIT               NOT NULL CONSTRAINT DF_Entities_IsActive DEFAULT (1),
    CONSTRAINT CK_Entities_Key CHECK (EntityKey NOT LIKE N'%[^A-Za-z0-9_]%')
);

/* A business field. Either a physical column (ColumnName) or a computed field
   (ComputedFunction + ComputedSourceFieldKey). ComputedFunction is a NAME from
   a fixed whitelist implemented in the engine - never SQL text. */
CREATE TABLE meta.Fields
(
    FieldId                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Fields PRIMARY KEY,
    EntityId               INT               NOT NULL CONSTRAINT FK_Fields_Entity REFERENCES meta.Entities (EntityId),
    FieldKey               NVARCHAR(50)      NOT NULL,
    DisplayName            NVARCHAR(100)     NOT NULL,
    Description            NVARCHAR(500)     NULL,
    Synonyms               NVARCHAR(300)     NULL,      -- comma separated alternative business terms (used for AI context)
    DataType               NVARCHAR(20)      NOT NULL,
    ColumnName             SYSNAME           NULL,
    ComputedFunction       NVARCHAR(50)      NULL,
    ComputedSourceFieldKey NVARCHAR(50)      NULL,
    TrueLabel              NVARCHAR(50)      NULL,      -- Boolean display labels, e.g. N'פעיל'
    FalseLabel             NVARCHAR(50)      NULL,
    IsMandatory            BIT               NOT NULL CONSTRAINT DF_Fields_IsMandatory DEFAULT (0),  -- business-mandatory (informational)
    IsTitle                BIT               NOT NULL CONSTRAINT DF_Fields_IsTitle DEFAULT (0),      -- part of the record title in results
    IsDefaultDisplay       BIT               NOT NULL CONSTRAINT DF_Fields_IsDefaultDisplay DEFAULT (0),
    DisplayOrder           INT               NOT NULL CONSTRAINT DF_Fields_DisplayOrder DEFAULT (0),
    IsActive               BIT               NOT NULL CONSTRAINT DF_Fields_IsActive DEFAULT (1),
    CONSTRAINT UQ_Fields_EntityKey UNIQUE (EntityId, FieldKey),
    CONSTRAINT CK_Fields_Key CHECK (FieldKey NOT LIKE N'%[^A-Za-z0-9_]%'),
    CONSTRAINT CK_Fields_DataType CHECK (DataType IN (N'String', N'Number', N'Date', N'Boolean')),
    CONSTRAINT CK_Fields_Function CHECK (ComputedFunction IS NULL OR ComputedFunction IN (N'FullYearsSince', N'DaysSince')),
    -- exactly one of: physical column / computed function
    CONSTRAINT CK_Fields_Source CHECK (
        (ColumnName IS NOT NULL AND ComputedFunction IS NULL AND ComputedSourceFieldKey IS NULL)
     OR (ColumnName IS NULL AND ComputedFunction IS NOT NULL AND ComputedSourceFieldKey IS NOT NULL))
);

/* A navigable many-to-one relationship: FromEntity.FromColumn -> ToEntity.ToColumn.
   Many-to-one only, so joining never multiplies the root records. */
CREATE TABLE meta.Relationships
(
    RelationshipId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Relationships PRIMARY KEY,
    FromEntityId    INT               NOT NULL CONSTRAINT FK_Relationships_From REFERENCES meta.Entities (EntityId),
    RelationshipKey NVARCHAR(50)      NOT NULL,
    DisplayName     NVARCHAR(100)     NOT NULL,
    Description     NVARCHAR(500)     NULL,
    ToEntityId      INT               NOT NULL CONSTRAINT FK_Relationships_To REFERENCES meta.Entities (EntityId),
    FromColumn      SYSNAME           NOT NULL,
    ToColumn        SYSNAME           NOT NULL,
    IsActive        BIT               NOT NULL CONSTRAINT DF_Relationships_IsActive DEFAULT (1),
    CONSTRAINT UQ_Relationships_Key UNIQUE (FromEntityId, RelationshipKey),
    CONSTRAINT CK_Relationships_Key CHECK (RelationshipKey NOT LIKE N'%[^A-Za-z0-9_]%')
);
GO

PRINT 'meta schema created.';
GO
