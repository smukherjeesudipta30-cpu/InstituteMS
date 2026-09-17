/* =====================================================================
   015_purge_non_production_rows.sql
   ---------------------------------------------------------------------
   Removes every row that did not come from production, so the database
   holds migrated data and nothing else. Testing then exercises the real
   data through the real logic, with no invented rows to explain results.

   Rows are identified by their audit columns, not by guesswork:
       CreatedBy = 'migration-004' / '-008' / '-014' / '-users'
           came from production.
       CreatedBy = 'migration-004-orphan'
           placeholder rows - see below, these STAY.
       anything else
           created through the running application, i.e. test data.

   Placeholders are NOT test data and are not removed. Production has 10
   students on a course that no longer exists and 20 teacher ids that no
   Teacher row matches. Their placeholders are what let those real rows
   keep their real references; deleting them would break the foreign keys
   and lose which course or teacher a student actually had.

   The local administrator account is handled separately at the bottom -
   read the note before enabling it.

   Run:  sqlcmd -S . -I -i migrations\015_purge_non_production_rows.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

DECLARE @users int, @teachers int, @students int, @fees int, @marks int,
        @expenses int, @notices int, @courses int, @income int;

/* --- child rows first ------------------------------------------------ */
DELETE FROM dbo.TecFees     WHERE CreatedBy NOT LIKE 'migration-%';
DELETE FROM dbo.TecExpense  WHERE CreatedBy NOT LIKE 'migration-%';
DELETE FROM dbo.LateFees    WHERE CreatedBy NOT LIKE 'migration-%';
DELETE FROM dbo.Fees        WHERE CreatedBy NOT LIKE 'migration-%';

DELETE FROM dbo.MonthlyFees WHERE CreatedBy NOT LIKE 'migration-%';
SET @fees = @@ROWCOUNT;

DELETE FROM dbo.Marks       WHERE CreatedBy NOT LIKE 'migration-%';
SET @marks = @@ROWCOUNT;

DELETE FROM dbo.ImageUpload WHERE CreatedBy NOT LIKE 'migration-%';

DELETE FROM dbo.Expense     WHERE CreatedBy NOT LIKE 'migration-%';
SET @expenses = @@ROWCOUNT;

DELETE FROM dbo.OIncome     WHERE CreatedBy NOT LIKE 'migration-%';
SET @income = @@ROWCOUNT;

DELETE FROM dbo.Notice      WHERE CreatedBy NOT LIKE 'migration-%';
SET @notices = @@ROWCOUNT;

DELETE FROM dbo.CusMessage
WHERE  id NOT IN (SELECT id FROM ph19987796653_prod.dbo.CusMessage);

/* --- parents --------------------------------------------------------- */
DELETE FROM dbo.Student     WHERE CreatedBy NOT LIKE 'migration-%';
SET @students = @@ROWCOUNT;

DELETE FROM dbo.Teacher     WHERE CreatedBy NOT LIKE 'migration-%';
SET @teachers = @@ROWCOUNT;

DELETE FROM dbo.Course      WHERE CreatedBy NOT LIKE 'migration-%';
SET @courses = @@ROWCOUNT;

/* --- users ----------------------------------------------------------- */
DELETE FROM dbo.Users       WHERE CreatedBy NOT LIKE 'migration-%';
SET @users = @@ROWCOUNT;

PRINT '015_purge_non_production_rows.sql applied. Removed:';
PRINT '  users     : ' + CAST(@users    AS varchar(10));
PRINT '  teachers  : ' + CAST(@teachers AS varchar(10));
PRINT '  courses   : ' + CAST(@courses  AS varchar(10));
PRINT '  students  : ' + CAST(@students AS varchar(10));
PRINT '  payments  : ' + CAST(@fees     AS varchar(10));
PRINT '  marks     : ' + CAST(@marks    AS varchar(10));
PRINT '  expenses  : ' + CAST(@expenses AS varchar(10));
PRINT '  income    : ' + CAST(@income   AS varchar(10));
PRINT '  notices   : ' + CAST(@notices  AS varchar(10));
GO

/* ---------------------------------------------------------------------
   The local administrator, admin@avijan.local, is created by
   InstituteMS.Tools.DataMigration so there is always a way in. It is the
   ONE row in this database that production does not have.

   Production's own Admin accounts are still here and still work with the
   passwords their owners already had - the migration hashed them, it did
   not reset them. If you can sign in as one of those, this account is not
   needed and you can delete it for an exact production copy:

       DELETE FROM dbo.Users WHERE Email = 'admin@avijan.local';

   Do not run that until you have confirmed you can sign in another way,
   or nobody can administer this database.
   --------------------------------------------------------------------- */
