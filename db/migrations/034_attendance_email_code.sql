/*
    034_attendance_email_code.sql
    -------------------------------------------------------------------------
    A six-digit code, emailed to the staff member, that must be typed back before
    an attendance mark counts.

    Two things this migration adds.

    1. A personal email on teachers and receptionists. Neither table had one: their
       LoginId is an @avijan.in identifier, not a mailbox, and the institute does not
       run mail on that domain. Without a real address there is nowhere to send the
       code, so this is the field the office must fill in before the feature can be
       switched on for anybody.

    2. dbo.AttendanceChallenge -- a mark that is waiting for its code.

    The punch time is stored on the challenge, NOT taken when the code is typed.
    Somebody who presses the button at 10:02 and types the code at 10:04 because
    Gmail was slow was present at 10:02, and the record has to say so; otherwise the
    email delay quietly eats into everyone's hours.

    The code itself is stored hashed. It is a credential, short-lived and only six
    digits, but a row that reveals it would let anyone with database access mark
    somebody else present.

    Idempotent: safe to run twice.
*/

/* --------------------------------------------- 1. somewhere to send it ----- */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'PersonalEmail')
BEGIN
    ALTER TABLE dbo.Teacher ADD PersonalEmail nvarchar(150) NULL;
    PRINT 'Added dbo.Teacher.PersonalEmail.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'PersonalEmail')
BEGIN
    ALTER TABLE dbo.Receptionist ADD PersonalEmail nvarchar(150) NULL;
    PRINT 'Added dbo.Receptionist.PersonalEmail.';
END
GO

/* ------------------------------------------ 2. the mark awaiting its code -- */

IF OBJECT_ID('dbo.AttendanceChallenge', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceChallenge
    (
        Id              int IDENTITY(1,1) NOT NULL,

        StaffType       nvarchar(20)  NOT NULL,
        StaffId         int           NOT NULL,
        StaffName       nvarchar(150) NULL,
        LoginId         nvarchar(100) NULL,

        BranchId        int           NULL,
        Branch          nvarchar(100) NULL,

        /* When the person actually pressed the button. This becomes the attendance
           time once the code is accepted -- see the note above. */
        MarkedOn        datetime2(0)  NOT NULL,
        OnDate          date          NOT NULL,

        Latitude        decimal(9,6)  NULL,
        Longitude       decimal(9,6)  NULL,
        AccuracyMetres  decimal(9,2)  NULL,
        DistanceMetres  decimal(9,2)  NULL,

        /* BCrypt of the six digits. Never the digits themselves. */
        CodeHash        nvarchar(200) NOT NULL,

        /* Where it was sent, kept so a person who mistypes their own address can be
           shown what the office holds for them. */
        SentToEmail     nvarchar(150) NULL,

        ExpiresOn       datetime2(0)  NOT NULL,

        /* Wrong guesses. Six digits is a million to one per try, but unlimited tries
           is not, so the challenge dies after a handful. */
        Attempts        int           NOT NULL CONSTRAINT DF_AttendanceChallenge_Attempts DEFAULT (0),

        /* Set when the code is accepted; a consumed challenge cannot be used twice. */
        ConsumedOn      datetime2(0)  NULL,

        CreatedOn       datetime2(0)  NOT NULL CONSTRAINT DF_AttendanceChallenge_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_AttendanceChallenge PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_AttendanceChallenge_StaffType CHECK (StaffType IN ('Teacher', 'Receptionist'))
    );

    PRINT 'Created dbo.AttendanceChallenge.';
END
ELSE
BEGIN
    PRINT 'dbo.AttendanceChallenge already exists.';
END
GO

/* Finding a person's outstanding challenge, which is the only query that matters. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_AttendanceChallenge_Open' AND object_id = OBJECT_ID('dbo.AttendanceChallenge'))
BEGIN
    CREATE INDEX IX_AttendanceChallenge_Open
        ON dbo.AttendanceChallenge (StaffType, StaffId, ConsumedOn) INCLUDE (ExpiresOn, Attempts);
END
GO

SELECT teachers_with_email = COUNT(*) FROM dbo.Teacher WHERE NULLIF(LTRIM(RTRIM(PersonalEmail)), '') IS NOT NULL;
SELECT receptionists_with_email = COUNT(*) FROM dbo.Receptionist WHERE NULLIF(LTRIM(RTRIM(PersonalEmail)), '') IS NOT NULL;
GO
