/*  027_admin_notifications.sql
    ---------------------------
    The administrator's dashboard notifications.

    The first thing it carries is the student password-change alert, which until
    now only went out by email - so it was lost if the mail server was down, and
    unreadable to anyone without the Avijan mailbox. This table is the record; the
    email is a copy of it.

    Deliberately generic (Type, plus a few named columns) rather than one table per
    alert: the next thing worth telling the office about should be an INSERT, not a
    migration.

    Idempotent: safe to run more than once.
*/

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AdminNotification' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.AdminNotification
    (
        Id              INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_AdminNotification PRIMARY KEY,

        /*  What happened. 'StudentPasswordChange' is the only value today; the
            column exists so a second kind of alert needs no schema change.  */
        [Type]          VARCHAR(60)   NOT NULL,

        /*  One line, already written for a reader - the dashboard shows it as-is
            rather than assembling a sentence from the columns below.  */
        Title           NVARCHAR(300) NOT NULL,

        StudentName     NVARCHAR(250) NULL,
        CourseName      NVARCHAR(250) NULL,
        RegistrationId  VARCHAR(50)   NULL,
        LoginId         VARCHAR(250)  NULL,

        /*  The student's chosen password, in clear, kept so the office can read it
            back to a student who forgets it. This is the same decision as the
            emailed copy and carries the same weight: people reuse passwords, so
            treat this column as credential storage. Dropping it costs nothing else
            - every other column stands on its own.  */
        DetailValue     NVARCHAR(300) NULL,

        /*  Local time. The office reads these against its own clock, and every
            other legacy date column in this database is local too.  */
        CreatedOn       DATETIME      NOT NULL
            CONSTRAINT DF_AdminNotification_CreatedOn DEFAULT (GETDATE()),

        ReadOn          DATETIME      NULL,
        ReadBy          VARCHAR(250)  NULL
    );

    /*  The dashboard asks for "newest first, unread first" on every load, which is
        this index exactly.  */
    CREATE INDEX IX_AdminNotification_Unread
        ON dbo.AdminNotification (ReadOn, CreatedOn DESC);

    PRINT 'Created dbo.AdminNotification.';
END
ELSE
BEGIN
    PRINT 'dbo.AdminNotification already exists - nothing to do.';
END
GO
