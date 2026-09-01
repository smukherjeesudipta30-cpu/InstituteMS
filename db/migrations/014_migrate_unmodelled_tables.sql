/* =====================================================================
   014_migrate_unmodelled_tables.sql
   ---------------------------------------------------------------------
   Loads the production tables that have no migrated module yet, so the
   local database is a complete copy and nothing is quietly missing when
   a module is built for them later.

       AttendanceMaster           344 rows   staff attendance
       dailyRoutine                 2 rows   weekly class routine
       MailLog_registration_time   26 rows   registration email log
       Login                        0 rows   empty on production

   Branch is deliberately excluded: it is empty on production and the new
   application has its own Branch master, populated separately.

   Identities are preserved so these rows can still be matched back to
   production while comparing.

   Run after 013 + 004 + 008 + 010:
       sqlcmd -S . -I -i migrations\014_migrate_unmodelled_tables.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

DELETE FROM dbo.AttendanceMaster;
DELETE FROM dbo.dailyRoutine;
DELETE FROM dbo.MailLog_registration_time;
DELETE FROM dbo.Login;
DELETE FROM dbo.CusMessage;
GO

INSERT INTO dbo.AttendanceMaster
    (attID, empId, attmonth, attyear, attDate, attStatus, remarks, loggedInDate, CreatedBy, CreatedOn)
SELECT  a.attID, a.empId, a.attmonth, a.attyear, a.attDate,
        LTRIM(RTRIM(a.attStatus)), LTRIM(RTRIM(a.remarks)), a.loggedInDate,
        'migration-014', SYSDATETIME()
FROM    ph19987796653_prod.dbo.AttendanceMaster AS a;

INSERT INTO dbo.dailyRoutine
    (id, name, mon, tues, wed, thurs, fri, sat, sun, CreatedBy, CreatedOn)
SELECT  d.id, d.name, d.mon, d.tues, d.wed, d.thurs, d.fri, d.sat, d.sun,
        'migration-014', SYSDATETIME()
FROM    ph19987796653_prod.dbo.dailyRoutine AS d;

INSERT INTO dbo.MailLog_registration_time
    (MailLogId, for_registraion_id, ToEmail, Subject, MailBody, Status, ErrorMessage,
     CreatedDate, CreatedBy, CreatedOn)
SELECT  m.MailLogId, m.for_registraion_id, m.ToEmail, m.Subject, m.MailBody,
        m.Status, m.ErrorMessage, m.CreatedDate,
        'migration-014', SYSDATETIME()
FROM    ph19987796653_prod.dbo.MailLog_registration_time AS m;
GO

/* ---------------------------------------------------------- CusMessage
   Contact-form messages. This table is modelled by the application
   (ContactMessage) but no earlier migration ever loaded it, so all 15
   production messages were being dropped. Caught by
   verifympare_production_to_target.sql.

   SourceIp and IsHandled are additions of the new schema; production has
   no equivalent, so they stay NULL / 0.                                 */
SET IDENTITY_INSERT dbo.CusMessage ON;

INSERT INTO dbo.CusMessage (id, CusName, CusMailid, CusPhone, cusbody, SubDate)
SELECT  c.id,
        LEFT(LTRIM(RTRIM(c.CusName)), 200),
        LEFT(LTRIM(RTRIM(c.CusMailid)), 200),
        LEFT(LTRIM(RTRIM(c.CusPhone)), 20),
        c.cusbody,
        TRY_CONVERT(datetime2(0), c.SubDate)
FROM    ph19987796653_prod.dbo.CusMessage AS c;

SET IDENTITY_INSERT dbo.CusMessage OFF;
GO

/* Login is empty on production; the insert is here so a future snapshot
   that does contain rows is carried across without editing this script. */
SET IDENTITY_INSERT dbo.Login ON;

INSERT INTO dbo.Login (id, username, pwd)
SELECT l.id, l.username, l.pwd
FROM   ph19987796653_prod.dbo.Login AS l;

SET IDENTITY_INSERT dbo.Login OFF;
GO

PRINT '014_migrate_unmodelled_tables.sql applied.';
GO
