/* =====================================================================
   00 - Create the ValidationSystemDb database (only if it does not exist).
   Does not touch any other database on the instance.
   ===================================================================== */
IF DB_ID(N'ValidationSystemDb') IS NULL
BEGIN
    CREATE DATABASE ValidationSystemDb;
    PRINT 'Database ValidationSystemDb created.';
END
ELSE
    PRINT 'Database ValidationSystemDb already exists.';
GO
