/* =====================================================================
   013_reset_target_for_reload.sql
   ---------------------------------------------------------------------
   Empties the application database so the production snapshot can be
   loaded into it cleanly.

   Run this BEFORE 004 / 008 / 010 / 014 whenever you reload from
   production. Without it those scripts fail: 004 deletes Teacher, but
   TecFees and TecExpense (loaded by 008) reference it, so a second run
   hits FK_TecFees_Teacher.

   Deletion order is children first, then parents:
       TecFees, TecExpense  ->  Teacher
       Student              ->  Course, Teacher

   Everything here is local test data. Production is never touched.

   Run:  sqlcmd -S . -I -i migrations\013_reset_target_for_reload.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

/* --- children of Teacher / Course / Student ------------------------- */
DELETE FROM dbo.TecFees;
DELETE FROM dbo.TecExpense;
DELETE FROM dbo.LateFees;
DELETE FROM dbo.Fees;
DELETE FROM dbo.MonthlyFees;
DELETE FROM dbo.Marks;
DELETE FROM dbo.ImageUpload;

/* --- standalone tables ---------------------------------------------- */
DELETE FROM dbo.Expense;
DELETE FROM dbo.OIncome;
DELETE FROM dbo.Notice;
DELETE FROM dbo.CusMessage;

/* --- tables with no migrated module yet (loaded by 014) -------------- */
IF OBJECT_ID('dbo.AttendanceMaster', 'U')          IS NOT NULL DELETE FROM dbo.AttendanceMaster;
IF OBJECT_ID('dbo.dailyRoutine', 'U')              IS NOT NULL DELETE FROM dbo.dailyRoutine;
IF OBJECT_ID('dbo.MailLog_registration_time', 'U') IS NOT NULL DELETE FROM dbo.MailLog_registration_time;
IF OBJECT_ID('dbo.Login', 'U')                     IS NOT NULL DELETE FROM dbo.Login;

/* --- parents -------------------------------------------------------- */
DELETE FROM dbo.Student;
DELETE FROM dbo.Teacher;
DELETE FROM dbo.Course;

/* --- users ----------------------------------------------------------
   Emptied so InstituteMS.Tools.DataMigration reloads all of production's
   accounts with freshly hashed passwords. Any local test login is lost
   with them and has to be recreated afterwards - see db\README.md.     */
DELETE FROM dbo.Users;

PRINT '013: target emptied, ready for reload.';
GO
