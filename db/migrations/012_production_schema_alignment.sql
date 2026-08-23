/* =====================================================================
   012_production_schema_alignment.sql
   ---------------------------------------------------------------------
   Brings the application schema in line with what LIVE production
   actually holds, before the production data is loaded into it.

   Everything here was found by measuring production, not by guessing:
   see verify\compare_production_snapshot.sql for the width audit.

   Run:  sqlcmd -S . -I -i migrations\012_production_schema_alignment.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

/* --- 1. Teacher.LoginId was too narrow -------------------------------
   Production declares varchar(50) and holds values up to 27 characters;
   this column was varchar(20), copied from the older restored backup.
   Loading production into it would have failed the migration outright
   (or truncated a teacher's sign-in, which the teacher account report
   matches on). Widened to match production.                            */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Teacher')
             AND name = 'LoginId' AND max_length < 50)
BEGIN
    ALTER TABLE dbo.Teacher ALTER COLUMN LoginId varchar(50) NULL;
    PRINT '012: Teacher.LoginId widened to varchar(50).';
END
GO

/* --- 2. Student.Avijan_login_Password was missing entirely ------------
   Production carries a student portal password on the student row; 30 of
   the 953 students have one. It was absent from this schema, so those 30
   values would have been dropped.

   WARNING: these are stored in clear text on production (e.g.
   'barshabiswas@1234'). They are copied verbatim so the local database is
   a faithful test copy, but nothing in the new application reads this
   column - student sign-in goes through dbo.Users, where passwords are
   BCrypt hashes. Hash or drop this column before the schema is used for
   anything other than testing.                                          */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Student')
                 AND name = 'Avijan_login_Password')
BEGIN
    ALTER TABLE dbo.Student ADD Avijan_login_Password varchar(250) NULL;
    PRINT '012: Student.Avijan_login_Password added.';
END
GO

/* --- 3. Tables production has that this schema did not ---------------
   None of these has a migrated module yet. They are created and loaded
   so the local database is a complete copy of production and nothing is
   silently missing when a module is built for them later.               */

IF OBJECT_ID('dbo.AttendanceMaster', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceMaster
    (
        attID        int           NOT NULL,
        empId        int           NULL,
        attmonth     int           NULL,
        attyear      int           NULL,
        attDate      datetime      NULL,
        attStatus    varchar(50)   NULL,
        remarks      varchar(500)  NULL,
        loggedInDate datetime      NULL,

        CreatedBy    varchar(50)   NULL,
        CreatedOn    datetime2(0)  NOT NULL
                     CONSTRAINT DF_AttendanceMaster_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_AttendanceMaster PRIMARY KEY CLUSTERED (attID)
    );
    PRINT '012: AttendanceMaster created.';
END
GO

IF OBJECT_ID('dbo.dailyRoutine', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.dailyRoutine
    (
        id        int          NOT NULL,
        name      varchar(250) NULL,
        mon       varchar(250) NULL,
        tues      varchar(250) NULL,
        wed       varchar(250) NULL,
        thurs     varchar(250) NULL,
        fri       varchar(250) NULL,
        sat       varchar(250) NULL,
        sun       varchar(250) NULL,

        CreatedBy varchar(50)  NULL,
        CreatedOn datetime2(0) NOT NULL
                  CONSTRAINT DF_dailyRoutine_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_dailyRoutine PRIMARY KEY CLUSTERED (id)
    );
    PRINT '012: dailyRoutine created.';
END
GO

IF OBJECT_ID('dbo.MailLog_registration_time', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MailLog_registration_time
    (
        MailLogId          int            NOT NULL,
        for_registraion_id varchar(50)    NULL,
        ToEmail            varchar(250)   NULL,
        Subject            varchar(500)   NULL,
        MailBody           varchar(max)   NULL,
        Status             varchar(50)    NULL,
        ErrorMessage       varchar(max)   NULL,
        CreatedDate        datetime       NULL,

        CreatedBy          varchar(50)    NULL,
        CreatedOn          datetime2(0)   NOT NULL
                           CONSTRAINT DF_MailLog_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_MailLog_registration_time PRIMARY KEY CLUSTERED (MailLogId)
    );
    PRINT '012: MailLog_registration_time created.';
END
GO

/* Empty on production, created so the copy is complete. */
IF OBJECT_ID('dbo.Login', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Login
    (
        id       int          NOT NULL IDENTITY(1,1),
        username varchar(250) NULL,
        pwd      varchar(250) NULL,

        CONSTRAINT PK_Login PRIMARY KEY CLUSTERED (id)
    );
    PRINT '012: Login created.';
END
GO
