/* =====================================================================
   04 - Least-privilege execution user.
   validation_reader is a database user WITHOUT a login. The engine runs
   every generated validation query under
       EXECUTE AS USER = 'validation_reader' ... REVERT
   so even if a defect let something other than a SELECT through, the
   database itself would refuse it: this user can only SELECT from hr.
   It has no access to meta/app and no INSERT/UPDATE/DELETE/DDL rights.
   ===================================================================== */
USE ValidationSystemDb;
GO

IF DATABASE_PRINCIPAL_ID(N'validation_reader') IS NULL
    CREATE USER validation_reader WITHOUT LOGIN;
GO

GRANT SELECT ON SCHEMA::hr TO validation_reader;
DENY INSERT, UPDATE, DELETE, ALTER, EXECUTE ON SCHEMA::hr TO validation_reader;
GO

PRINT 'validation_reader configured (SELECT on hr only).';
GO
