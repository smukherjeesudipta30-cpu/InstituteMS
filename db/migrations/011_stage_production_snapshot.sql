/* =====================================================================
   011_stage_production_snapshot.sql
   ---------------------------------------------------------------------
   Takes a faithful, read-only snapshot of the LIVE production database
   into a local staging database, so every later migration reads from a
   copy of production rather than the older restored backup.

   Source : AVIJAN_PROD  (linked server -> ph19987796653_ on GoDaddy)
   Target : ph19987796653_prod  (local staging mirror)

   Nothing is ever written to production. The linked server is created
   with 'rpc out' = false, so no remote procedure can be executed through
   it; only SELECT is possible.

   The older restored copy, ph19987796653_, is left untouched as a
   reference point for comparing what changed since 31-05-2026.

   SELECT ... INTO reproduces each source column's own type and width,
   which matters: production is wider than the old backup in several
   places (Teacher.LoginId varchar(50) vs 20, Users.Email 250 vs 50), so
   copying into the old schema would have silently truncated real data.

   Run:  sqlcmd -S . -I -i migrations\011_stage_production_snapshot.sql
   ===================================================================== */
SET NOCOUNT ON;

IF DB_ID('ph19987796653_prod') IS NULL
    CREATE DATABASE ph19987796653_prod;
GO

USE ph19987796653_prod;
GO

/* Dropped and recreated on every run, so the snapshot is never a merge
   of two different points in time. */
DECLARE @sql nvarchar(max) = N'';

SELECT @sql = @sql + N'DROP TABLE dbo.' + QUOTENAME(name) + N';' + CHAR(10)
FROM sys.tables;

EXEC sp_executesql @sql;
GO

/* Four tables live in the 'ph19987796653' schema on production, not dbo:
   dailyRoutine, MailLog_registration_time, OIncome and TecExpense. The
   older restored backup had all of them in dbo, so any script that
   assumed dbo would have missed OIncome and TecExpense entirely - two
   tables the income report depends on. They are flattened into dbo here
   so downstream scripts have one place to look. */

/* ------------------------------------------------------------ copy ---
   One statement per table. Branch is included even though it is empty in
   production, so the mirror is complete; the migration into the
   application schema is what skips it.                                  */

SELECT * INTO dbo.AttendanceMaster           FROM AVIJAN_PROD.ph19987796653_.dbo.AttendanceMaster;
SELECT * INTO dbo.Branch                     FROM AVIJAN_PROD.ph19987796653_.dbo.Branch;
SELECT * INTO dbo.Course                     FROM AVIJAN_PROD.ph19987796653_.dbo.Course;
SELECT * INTO dbo.CusMessage                 FROM AVIJAN_PROD.ph19987796653_.dbo.CusMessage;
SELECT * INTO dbo.dailyRoutine               FROM AVIJAN_PROD.ph19987796653_.ph19987796653.dailyRoutine;
SELECT * INTO dbo.Expense                    FROM AVIJAN_PROD.ph19987796653_.dbo.Expense;
SELECT * INTO dbo.Fees                       FROM AVIJAN_PROD.ph19987796653_.dbo.Fees;
SELECT * INTO dbo.ImageUpload                FROM AVIJAN_PROD.ph19987796653_.dbo.ImageUpload;
SELECT * INTO dbo.LateFees                   FROM AVIJAN_PROD.ph19987796653_.dbo.LateFees;
SELECT * INTO dbo.Login                      FROM AVIJAN_PROD.ph19987796653_.dbo.Login;
SELECT * INTO dbo.MailLog_registration_time  FROM AVIJAN_PROD.ph19987796653_.ph19987796653.MailLog_registration_time;
SELECT * INTO dbo.Marks                      FROM AVIJAN_PROD.ph19987796653_.dbo.Marks;
SELECT * INTO dbo.monthlyFees                FROM AVIJAN_PROD.ph19987796653_.dbo.monthlyFees;
SELECT * INTO dbo.Notice                     FROM AVIJAN_PROD.ph19987796653_.dbo.Notice;
SELECT * INTO dbo.OIncome                    FROM AVIJAN_PROD.ph19987796653_.ph19987796653.OIncome;
SELECT * INTO dbo.student                    FROM AVIJAN_PROD.ph19987796653_.dbo.student;
SELECT * INTO dbo.Teacher                    FROM AVIJAN_PROD.ph19987796653_.dbo.Teacher;
SELECT * INTO dbo.TecExpense                 FROM AVIJAN_PROD.ph19987796653_.ph19987796653.TecExpense;
SELECT * INTO dbo.TecFees                    FROM AVIJAN_PROD.ph19987796653_.dbo.TecFees;
SELECT * INTO dbo.Users                      FROM AVIJAN_PROD.ph19987796653_.dbo.Users;
GO

PRINT 'Snapshot complete. Verify with verify\compare_production_snapshot.sql';
