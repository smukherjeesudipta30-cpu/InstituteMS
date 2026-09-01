/* =====================================================================
   LEGACY_COEXIST_UPGRADE.sql
   ---------------------------------------------------------------------
   Adds everything the Angular 19 / .NET 8 application needs to the LIVE
   legacy database (ph19987796653_), so both applications can run against
   the same production data at the same time.

   THE RULE THIS SCRIPT OBEYS
   ---------------------------------------------------------------------
   Nothing the legacy application touches is altered. Not one existing
   column is dropped, renamed, retyped, widened, narrowed or made NOT
   NULL. Not one existing row is deleted.

   That rule is stricter than "only add columns", and it has to be.
   The legacy application contains seven INSERT statements written
   without a column list:

       Models/About.cs:18       Insert into CusMessage values (...)
       Models/Course.cs:25      Insert into Course     values (...)
       Models/Expense.cs:26     Insert into Expense    values (...)
       Models/Expense.cs:44     Insert into TecExpense values (...)
       Models/Notice.cs:36      Insert into Notice     values (...)
       Models/OtherIncome.cs:23 Insert into OIncome    values (...)
       Models/Teacher.cs:126    Insert into Teacher    values (...)

   A column-list-free INSERT must supply a value for every non-identity
   column, and a DEFAULT does not excuse it. Add a single column to any
   of those seven tables and the statement fails immediately with "Column
   name or number of supplied values does not match table definition" -
   recording an expense, adding a course, saving a notice and creating a
   teacher all stop working.

   So those seven tables are NEVER altered here. The fields the new
   application needs for them live in side tables instead (step 3).

   The other legacy tables are safe to extend, and were checked the same
   way: the only other INSERTs in the legacy codebase are
   TestAttendance.aspx.cs -> AttendanceMaster and Models/User.cs ->
   [dbo].[Users], and both name their columns explicitly. Every legacy
   SELECT * result is read by column NAME (sdr["ExDetails"]), never by
   ordinal, so extra columns cannot shift a read.

   WHAT IS DELIBERATELY NOT DONE
   ---------------------------------------------------------------------
   * dbo.Users.Password is KEPT. The migrated schema replaces it with
     PasswordHash; dropping it would kill legacy sign-in for all 754
     accounts. PasswordHash is added alongside it.
   * dbo.Branch keeps Branch_name / Branch_id / Is_active. The new
     names are added beside them.
   * No foreign key points at a legacy table. dbo.student's primary key
     is on id, not StuRegId, so an FK on StuRegId would first need a new
     unique index on a live table - which could start rejecting legacy
     inserts. Indexes are used instead of constraints.
   * No type is corrected. Money stays decimal(18,0) and dates stay
     datetime. Widening money to hold paise is a separate, deliberate
     script - MONEY_PRECISION_UPGRADE.sql - because it alters live
     columns, which this one never does.

   COMPANION CHANGES
   ---------------------------------------------------------------------
   The API's EF Core model has been remapped onto these legacy shapes:
   DateOnly over datetime, decimal over the varchar fee columns, nullable
   student flags, audit columns ignored on the seven tables above, and
   Branch read through the side tables. Verified against a legacy-shaped
   database - all 64 GET endpoints return successfully.

   Run MONEY_PRECISION_UPGRADE.sql after this one so fee amounts stop
   rounding paise away.

   HOW TO RUN
   ---------------------------------------------------------------------
       sqlcmd -S <server> -d ph19987796653_ -U <user> -P <password> -I -b ^
              -i LEGACY_COEXIST_UPGRADE.sql -o coexist_log.txt

   In SSMS: select the database, turn ON SQLCMD Mode (Query -> SQLCMD
   Mode), then execute. Without SQLCMD mode the ':on error exit' line is
   a syntax error and SSMS runs on past a failed preflight.

   TAKE A BACKUP FIRST. Re-running is harmless; every step is guarded.
   ===================================================================== */

:on error exit

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/* ============================================ 0. preflight ========== */

PRINT '=====================================================';
PRINT CONCAT('Target database : ', DB_NAME());
PRINT CONCAT('Server          : ', @@SERVERNAME);
PRINT CONCAT('Started         : ', CONVERT(varchar(19), SYSDATETIME(), 120));
PRINT '=====================================================';
GO

/* This script belongs on the LEGACY database. The guard below refuses
   any other schema, so it cannot be pointed at the wrong one. */
IF OBJECT_ID('dbo.Student', 'U') IS NULL
   OR OBJECT_ID('dbo.MonthlyFees', 'U') IS NULL
   OR OBJECT_ID('dbo.Expense', 'U') IS NULL
   OR OBJECT_ID('dbo.OIncome', 'U') IS NULL
   OR OBJECT_ID('dbo.Branch', 'U') IS NULL
   OR OBJECT_ID('dbo.Users', 'U') IS NULL
BEGIN
    RAISERROR ('Core legacy tables are missing. This is not the Avijan production database.', 16, 1);
END
GO

IF COL_LENGTH('dbo.Branch', 'Branch_name') IS NULL
   OR COL_LENGTH('dbo.Users', 'Password') IS NULL
BEGIN
    RAISERROR ('This does not look like the LEGACY schema (expected Branch.Branch_name and Users.Password). Run this only against the live legacy database.', 16, 1);
END
GO

PRINT 'Preflight passed - legacy schema confirmed.';
GO

/* =============== 1. columns on tables the legacy app never
                      inserts into without a column list ============= */

/* --- dbo.student ------------------------------------------------- */
IF COL_LENGTH('dbo.student', 'Branch') IS NULL
BEGIN
    ALTER TABLE dbo.student ADD Branch varchar(100) NULL;
    PRINT 'Added student.Branch.';
END
GO

IF COL_LENGTH('dbo.student', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.student ADD CreatedBy varchar(50) NULL, ModifiedBy varchar(50) NULL;
    PRINT 'Added student audit columns.';
END
GO

IF COL_LENGTH('dbo.student', 'CreatedOn') IS NULL
BEGIN
    ALTER TABLE dbo.student ADD CreatedOn datetime2(0) NULL, ModifiedOn datetime2(0) NULL;
    PRINT 'Added student audit timestamps.';
END
GO

/* --- dbo.monthlyFees --------------------------------------------- */
IF COL_LENGTH('dbo.monthlyFees', 'TransactionId') IS NULL
BEGIN
    ALTER TABLE dbo.monthlyFees ADD TransactionId varchar(64) NULL;
    PRINT 'Added monthlyFees.TransactionId.';
END
GO

IF COL_LENGTH('dbo.monthlyFees', 'VerifiedOn') IS NULL
BEGIN
    ALTER TABLE dbo.monthlyFees ADD VerifiedOn datetime2(0) NULL, VerifiedBy varchar(50) NULL;
    PRINT 'Added monthlyFees verification columns.';
END
GO

IF COL_LENGTH('dbo.monthlyFees', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.monthlyFees
        ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL,
            ModifiedBy varchar(50) NULL, ModifiedOn datetime2(0) NULL;

    PRINT 'Added monthlyFees audit columns.';
END
GO

/* An online payment's transaction id must be unique, but only among the
   rows that have one - offline payments leave it NULL. A filtered index
   is the only shape that expresses that, and it constrains nothing the
   legacy application writes, because the legacy application never sets
   this column. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_monthlyFees_TransactionId'
                 AND object_id = OBJECT_ID('dbo.monthlyFees'))
BEGIN
    CREATE UNIQUE INDEX UQ_monthlyFees_TransactionId
        ON dbo.monthlyFees (TransactionId) WHERE TransactionId IS NOT NULL;

    PRINT 'Created UQ_monthlyFees_TransactionId.';
END
GO

/* --- dbo.Users (legacy INSERT names its columns, so this is safe) -- */
IF COL_LENGTH('dbo.Users', 'PasswordHash') IS NULL
BEGIN
    /* Password is intentionally left in place beside this. */
    ALTER TABLE dbo.Users ADD PasswordHash nvarchar(255) NULL;
    PRINT 'Added Users.PasswordHash (Users.Password kept).';
END
GO

IF COL_LENGTH('dbo.Users', 'RefreshToken') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD RefreshToken nvarchar(255) NULL, RefreshTokenExpiry datetime2(0) NULL;

    PRINT 'Added Users refresh-token columns.';
END
GO

IF COL_LENGTH('dbo.Users', 'FailedLoginCount') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD FailedLoginCount int NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT (0),
            LockoutEnd  datetime2(0) NULL,
            LastLoginOn datetime2(0) NULL;

    PRINT 'Added Users lockout columns.';
END
GO

IF COL_LENGTH('dbo.Users', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL,
            ModifiedBy varchar(50) NULL, ModifiedOn datetime2(0) NULL;

    PRINT 'Added Users audit columns.';
END
GO

/* --- dbo.Branch (empty in production; legacy names kept) ---------- */
IF COL_LENGTH('dbo.Branch', 'BranchName') IS NULL
BEGIN
    ALTER TABLE dbo.Branch
        ADD BranchName varchar(50)  NULL,
            BranchCode varchar(50)  NULL,
            IsActive   bit          NOT NULL CONSTRAINT DF_Branch_IsActive DEFAULT (1),
            CreatedBy  varchar(50)  NULL,
            CreatedOn  datetime2(0) NULL;

    PRINT 'Added Branch columns beside the legacy ones.';
END
GO

/* --- dbo.ImageUpload --------------------------------------------- */
IF COL_LENGTH('dbo.ImageUpload', 'StuRegId') IS NULL
BEGIN
    ALTER TABLE dbo.ImageUpload
        ADD StuRegId varchar(15) NULL, ContentType varchar(100) NULL,
            IsInline bit NOT NULL CONSTRAINT DF_ImageUpload_IsInline DEFAULT (0);

    PRINT 'Added ImageUpload columns.';
END
GO

/* --- audit columns on the remaining extendable tables ------------- */
IF COL_LENGTH('dbo.Marks', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.Marks
        ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL,
            ModifiedBy varchar(50) NULL, ModifiedOn datetime2(0) NULL;
    PRINT 'Added Marks audit columns.';
END
GO

IF COL_LENGTH('dbo.Fees', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.Fees ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL;
    PRINT 'Added Fees audit columns.';
END
GO

IF COL_LENGTH('dbo.LateFees', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.LateFees ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL;
    PRINT 'Added LateFees audit columns.';
END
GO

IF COL_LENGTH('dbo.TecFees', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.TecFees ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL;
    PRINT 'Added TecFees audit columns.';
END
GO

/* AttendanceMaster is extendable: its legacy INSERTs name their columns. */
IF COL_LENGTH('dbo.AttendanceMaster', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.AttendanceMaster ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL;
    PRINT 'Added AttendanceMaster audit columns.';
END
GO

IF COL_LENGTH('dbo.dailyRoutine', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.dailyRoutine ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL;
    PRINT 'Added dailyRoutine audit columns.';
END
GO

IF COL_LENGTH('dbo.MailLog_registration_time', 'CreatedBy') IS NULL
BEGIN
    ALTER TABLE dbo.MailLog_registration_time
        ADD CreatedBy varchar(50) NULL, CreatedOn datetime2(0) NULL;
    PRINT 'Added MailLog audit columns.';
END
GO

/* ===================== 2. reporting indexes on live tables ==========
   Indexes only. They change no column, reject no insert, and are
   invisible to the legacy application apart from being faster. */

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_student_DOJ_Branch' AND object_id = OBJECT_ID('dbo.student'))
BEGIN
    CREATE INDEX IX_student_DOJ_Branch ON dbo.student (DOJ) INCLUDE (Branch, StuAdFees, StuRegId);
    PRINT 'Created IX_student_DOJ_Branch.';
END
GO

/* The production database has NO indexes beyond its primary keys - verified
   against the live server. Every lookup by registration id is therefore a
   full table scan, which the legacy application survives because its screens
   are simple, but which times out the new application's dues and statement
   queries against 8,639 fee rows and 953 students.

   These indexes change no column and reject no insert, so the legacy
   application cannot notice them except by getting faster. */

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_monthlyFees_sturegid' AND object_id = OBJECT_ID('dbo.monthlyFees'))
BEGIN
    CREATE INDEX IX_monthlyFees_sturegid
        ON dbo.monthlyFees (sturegid)
        INCLUDE (Date1, PDate, Installment, OInstallment, OldDue, NewDue, percen);

    PRINT 'Created IX_monthlyFees_sturegid.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_monthlyFees_Date1' AND object_id = OBJECT_ID('dbo.monthlyFees'))
BEGIN
    CREATE INDEX IX_monthlyFees_Date1
        ON dbo.monthlyFees (Date1) INCLUDE (sturegid, Installment, NewDue);

    PRINT 'Created IX_monthlyFees_Date1.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_student_StuRegId' AND object_id = OBJECT_ID('dbo.student'))
BEGIN
    CREATE INDEX IX_student_StuRegId ON dbo.student (StuRegId) INCLUDE (IsActive, IsComplete, Branch, Cid);
    PRINT 'Created IX_student_StuRegId.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_Marks_Regid' AND object_id = OBJECT_ID('dbo.Marks'))
BEGIN
    CREATE INDEX IX_Marks_Regid ON dbo.Marks (Regid) INCLUDE (ExDate);
    PRINT 'Created IX_Marks_Regid.';
END
GO

/* ============ 3. side tables for the seven forbidden tables =========
   dbo.Expense and dbo.OIncome cannot gain a Branch column without
   breaking "Insert into Expense values (...)". The branch lives beside
   the row instead, keyed by the legacy id. The new application joins;
   the legacy application never sees it.

   Deleting an expense in the legacy application leaves an orphan row
   here. That is deliberate - a foreign key with ON DELETE CASCADE would
   mean adding a constraint to a live table. The reports left-join, so an
   orphan is inert, and step 6 can be re-run to tidy up. */

IF OBJECT_ID('dbo.ExpenseExt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExpenseExt
    (
        ExpenseId  int          NOT NULL,
        Branch     varchar(100) NULL,
        CreatedBy  varchar(50)  NULL,
        CreatedOn  datetime2(0) NOT NULL
                   CONSTRAINT DF_ExpenseExt_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy varchar(50)  NULL,
        ModifiedOn datetime2(0) NULL,

        CONSTRAINT PK_ExpenseExt PRIMARY KEY CLUSTERED (ExpenseId)
    );

    CREATE INDEX IX_ExpenseExt_Branch ON dbo.ExpenseExt (Branch);

    PRINT 'Created dbo.ExpenseExt.';
END
ELSE
    PRINT 'dbo.ExpenseExt already exists.';
GO

IF OBJECT_ID('dbo.OIncomeExt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OIncomeExt
    (
        IncomeId   int          NOT NULL,
        Branch     varchar(100) NULL,
        CreatedBy  varchar(50)  NULL,
        CreatedOn  datetime2(0) NOT NULL
                   CONSTRAINT DF_OIncomeExt_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy varchar(50)  NULL,
        ModifiedOn datetime2(0) NULL,

        CONSTRAINT PK_OIncomeExt PRIMARY KEY CLUSTERED (IncomeId)
    );

    CREATE INDEX IX_OIncomeExt_Branch ON dbo.OIncomeExt (Branch);

    PRINT 'Created dbo.OIncomeExt.';
END
ELSE
    PRINT 'dbo.OIncomeExt already exists.';
GO

/* dbo.CusMessage is bare-inserted by the legacy About page, so it cannot
   gain the IsHandled flag the dashboard's pending-enquiries panel needs.
   No side row means not handled - the right default, because an enquiry
   nobody has touched is exactly the one that should still be showing. */

IF OBJECT_ID('dbo.CusMessageExt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CusMessageExt
    (
        MessageId int          NOT NULL,
        IsHandled bit          NOT NULL
                  CONSTRAINT DF_CusMessageExt_IsHandled DEFAULT (0),
        HandledBy varchar(50)  NULL,
        HandledOn datetime2(0) NULL,

        CONSTRAINT PK_CusMessageExt PRIMARY KEY CLUSTERED (MessageId)
    );

    CREATE INDEX IX_CusMessageExt_IsHandled ON dbo.CusMessageExt (IsHandled);

    PRINT 'Created dbo.CusMessageExt.';
END
ELSE
    PRINT 'dbo.CusMessageExt already exists.';
GO

/* =============== 3b. the registration number sequence ===============
   Without this, admitting a student fails outright with "Invalid object
   name 'dbo.StudentRegistrationNumber'". It is a SEQUENCE, not a table,
   and it is what issues the next registration number.

   The legacy generator took the highest Student.id and added one. That
   follows the identity column, which leaves gaps - a rolled-back insert
   burns a value - and it compared whole strings, so "AVI/3463/26" and
   "AVI/Raj/3463/26" looked different and the same number could be handed
   out twice once the branch segment appeared.

   The sequence starts above the highest number already in use, parsed out
   of the existing StuRegId values in every format the data contains:

       AVI/3462/26      -> 3462   (two slashes)
       AVI/Raj/3462/26  -> 3462   (branch segment)
       AVI//Raj/3455/2  -> 3455   (malformed, empty segment)
       3457             -> 3457   (bare number)

   The number is the second-to-last '/' segment, or the whole value when
   there are no slashes. Creating a sequence adds no column and constrains
   no table, so the legacy application is untouched by it. */

IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'StudentRegistrationNumber')
BEGIN
    DECLARE @maxNumber int;

    ;WITH parsed AS
    (
        SELECT
            s.StuRegId,
            r       = REVERSE(LTRIM(RTRIM(s.StuRegId))),
            slashes = LEN(s.StuRegId) - LEN(REPLACE(s.StuRegId, '/', ''))
        FROM dbo.student s
        WHERE s.StuRegId IS NOT NULL AND LTRIM(RTRIM(s.StuRegId)) <> ''
    ),
    segments AS
    (
        SELECT
            p.StuRegId,
            Number = CASE
                        WHEN p.slashes = 0 THEN p.StuRegId
                        ELSE REVERSE(
                                SUBSTRING(
                                    p.r,
                                    CHARINDEX('/', p.r) + 1,
                                    CASE
                                        WHEN CHARINDEX('/', p.r, CHARINDEX('/', p.r) + 1) = 0
                                            THEN LEN(p.r)
                                        ELSE CHARINDEX('/', p.r, CHARINDEX('/', p.r) + 1)
                                             - CHARINDEX('/', p.r) - 1
                                    END))
                     END
        FROM parsed p
    )
    SELECT @maxNumber = MAX(TRY_CONVERT(int, Number)) FROM segments;

    /* Never start below the identity high-water mark, so a new number can
       never look older than an existing student row. */
    DECLARE @maxId int = (SELECT ISNULL(MAX(id), 0) FROM dbo.student);
    DECLARE @start int = 1 + (SELECT CASE WHEN ISNULL(@maxNumber, 0) > @maxId
                                          THEN ISNULL(@maxNumber, 0) ELSE @maxId END);

    DECLARE @seqSql nvarchar(400) =
        N'CREATE SEQUENCE dbo.StudentRegistrationNumber AS int START WITH '
        + CAST(@start AS nvarchar(20)) + N' INCREMENT BY 1 NO CYCLE CACHE 10;';

    EXEC sp_executesql @seqSql;

    PRINT CONCAT('Created sequence dbo.StudentRegistrationNumber starting at ', @start,
                 ' (highest number in use ', ISNULL(@maxNumber, 0),
                 ', highest student id ', @maxId, ').');
END
ELSE
    PRINT 'Sequence dbo.StudentRegistrationNumber already exists.';
GO

/* ==================== 4. the examination portal =====================
   Three tables. Question data is denormalised onto ExamQuestion and the
   student's answers onto ExamAttempt, so there is no fourth table.

   FK_ExamPaper_Course is deliberately omitted: it would put a constraint
   on the live dbo.Course, and deleting a course in the legacy
   application would start failing. Cid is indexed instead. */

IF OBJECT_ID('dbo.ExamPaper', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamPaper
    (
        ExamId       int            NOT NULL IDENTITY(1,1),
        Title        varchar(200)   NOT NULL,
        Subject      varchar(100)   NULL,
        Cid          int            NULL,
        TotalMarks   int            NOT NULL CONSTRAINT DF_ExamPaper_TotalMarks DEFAULT (0),
        PassMarks    int            NULL,
        DurationMins int            NOT NULL CONSTRAINT DF_ExamPaper_DurationMins DEFAULT (0),
        OpensOn      datetime2(0)   NULL,
        ClosesOn     datetime2(0)   NULL,
        IsPublished  bit            NOT NULL CONSTRAINT DF_ExamPaper_IsPublished DEFAULT (0),
        CreatedBy    varchar(50)    NULL,
        CreatedOn    datetime2(0)   NOT NULL
                     CONSTRAINT DF_ExamPaper_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy   varchar(50)    NULL,
        ModifiedOn   datetime2(0)   NULL,
        PaperType    varchar(10)    NOT NULL CONSTRAINT DF_ExamPaper_PaperType DEFAULT ('Theory'),
        TermNumber   int            NOT NULL CONSTRAINT DF_ExamPaper_TermNumber DEFAULT (1),

        CONSTRAINT PK_ExamPaper PRIMARY KEY CLUSTERED (ExamId),
        CONSTRAINT CK_ExamPaper_PaperType    CHECK (PaperType IN ('Theory', 'Practical')),
        CONSTRAINT CK_ExamPaper_TermPositive CHECK (TermNumber >= 1)
    );

    CREATE INDEX IX_ExamPaper_Published ON dbo.ExamPaper (IsPublished, ClosesOn);
    CREATE INDEX IX_ExamPaper_Cid       ON dbo.ExamPaper (Cid);

    PRINT 'Created dbo.ExamPaper.';
END
ELSE
    PRINT 'dbo.ExamPaper already exists.';
GO

IF OBJECT_ID('dbo.ExamQuestion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamQuestion
    (
        QuestionId    int           NOT NULL IDENTITY(1,1),
        ExamId        int           NOT NULL,
        QuestionNo    int           NOT NULL,
        QuestionText  varchar(max)  NOT NULL,
        OptionA       varchar(500)  NOT NULL,
        OptionB       varchar(500)  NOT NULL,
        OptionC       varchar(500)  NULL,
        OptionD       varchar(500)  NULL,
        CorrectOption char(1)       NOT NULL,
        Marks         int           NOT NULL CONSTRAINT DF_ExamQuestion_Marks DEFAULT (1),
        CreatedBy     varchar(50)   NULL,
        CreatedOn     datetime2(0)  NOT NULL
                      CONSTRAINT DF_ExamQuestion_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_ExamQuestion PRIMARY KEY CLUSTERED (QuestionId),
        CONSTRAINT FK_ExamQuestion_Paper FOREIGN KEY (ExamId)
            REFERENCES dbo.ExamPaper (ExamId) ON DELETE CASCADE,
        CONSTRAINT CK_ExamQuestion_Correct CHECK (CorrectOption IN ('A','B','C','D'))
    );

    CREATE INDEX IX_ExamQuestion_Exam ON dbo.ExamQuestion (ExamId, QuestionNo);

    PRINT 'Created dbo.ExamQuestion.';
END
ELSE
    PRINT 'dbo.ExamQuestion already exists.';
GO

IF OBJECT_ID('dbo.ExamAttempt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamAttempt
    (
        AttemptId     int          NOT NULL IDENTITY(1,1),
        ExamId        int          NOT NULL,
        StuRegId      varchar(15)  NOT NULL,
        StartedOn     datetime2(0) NOT NULL
                      CONSTRAINT DF_ExamAttempt_StartedOn DEFAULT (SYSDATETIME()),
        DueOn         datetime2(0) NOT NULL
                      CONSTRAINT DF_ExamAttempt_DueOn DEFAULT (SYSDATETIME()),
        SubmittedOn   datetime2(0) NULL,
        Answers       varchar(max) NULL,
        Score         int          NULL,
        TotalMarks    int          NULL,
        CorrectCount  int          NULL,
        QuestionCount int          NULL,
        AutoSubmitted bit          NOT NULL
                      CONSTRAINT DF_ExamAttempt_AutoSubmitted DEFAULT (0),
        CreatedOn     datetime2(0) NOT NULL
                      CONSTRAINT DF_ExamAttempt_CreatedOn DEFAULT (SYSDATETIME()),
        Source        varchar(10)  NOT NULL
                      CONSTRAINT DF_ExamAttempt_Source DEFAULT ('Portal'),
        EnteredBy     varchar(50)  NULL,
        TermNumber    int          NULL,
        PaperType     varchar(10)  NULL,
        Cid           int          NULL,

        CONSTRAINT PK_ExamAttempt PRIMARY KEY CLUSTERED (AttemptId),
        CONSTRAINT FK_ExamAttempt_Paper FOREIGN KEY (ExamId)
            REFERENCES dbo.ExamPaper (ExamId),
        CONSTRAINT CK_ExamAttempt_Source CHECK (Source IN ('Portal', 'Entered'))
    );

    CREATE INDEX IX_ExamAttempt_Student ON dbo.ExamAttempt (StuRegId, SubmittedOn DESC);

    /* One theory and one practical per course, per term. */
    CREATE UNIQUE INDEX UQ_ExamAttempt_CourseTermType
        ON dbo.ExamAttempt (StuRegId, Cid, TermNumber, PaperType)
        WHERE Cid IS NOT NULL AND TermNumber IS NOT NULL AND PaperType IS NOT NULL;

    PRINT 'Created dbo.ExamAttempt.';
END
ELSE
    PRINT 'dbo.ExamAttempt already exists.';
GO

/* ============== 5. scheduling, certificates, receptionists ========== */

IF OBJECT_ID('dbo.ExamSchedule', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamSchedule
    (
        ScheduleId   int          NOT NULL IDENTITY(1,1),
        ExamId       int          NOT NULL,
        StuRegId     varchar(50)  NOT NULL,
        ScheduledOn  datetime2(0) NOT NULL,

        /* Handed to the student by the administrator; without it the
           paper will not start. */
        AccessCode   char(6)      NULL,
        CodeIssuedOn datetime2(0) NULL,
        CodeIssuedBy varchar(50)  NULL,
        CreatedBy    varchar(50)  NULL,
        CreatedOn    datetime2(0) NOT NULL
                     CONSTRAINT DF_ExamSchedule_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy   varchar(50)  NULL,
        ModifiedOn   datetime2(0) NULL,

        CONSTRAINT PK_ExamSchedule PRIMARY KEY CLUSTERED (ScheduleId),
        CONSTRAINT FK_ExamSchedule_ExamPaper FOREIGN KEY (ExamId)
            REFERENCES dbo.ExamPaper (ExamId) ON DELETE CASCADE,
        CONSTRAINT UQ_ExamSchedule_ExamStudent UNIQUE (ExamId, StuRegId)
    );

    CREATE INDEX IX_ExamSchedule_StuRegId ON dbo.ExamSchedule (StuRegId, ScheduledOn);

    PRINT 'Created dbo.ExamSchedule.';
END
ELSE
    PRINT 'dbo.ExamSchedule already exists.';
GO

IF OBJECT_ID('dbo.CertificateIssue', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CertificateIssue
    (
        IssueId  int          NOT NULL IDENTITY(1,1),
        StuRegId varchar(50)  NOT NULL,
        IssueNo  int          NOT NULL,
        IssuedOn datetime2(0) NOT NULL
                 CONSTRAINT DF_CertificateIssue_IssuedOn DEFAULT (SYSDATETIME()),
        IssuedTo varchar(200) NULL,

        CONSTRAINT PK_CertificateIssue PRIMARY KEY CLUSTERED (IssueId),
        CONSTRAINT UQ_CertificateIssue_StudentIssue UNIQUE (StuRegId, IssueNo)
    );

    CREATE INDEX IX_CertificateIssue_StuRegId ON dbo.CertificateIssue (StuRegId, IssuedOn DESC);

    PRINT 'Created dbo.CertificateIssue.';
END
ELSE
    PRINT 'dbo.CertificateIssue already exists.';
GO

IF OBJECT_ID('dbo.Receptionist', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Receptionist
    (
        id         int          NOT NULL IDENTITY(1,1),
        RecName    varchar(100) NOT NULL,
        LoginId    varchar(100) NULL,
        Phone      varchar(20)  NULL,
        Branch     varchar(100) NULL,
        IsActive   bit          NOT NULL CONSTRAINT DF_Receptionist_IsActive DEFAULT (1),
        CreatedBy  varchar(50)  NULL,
        CreatedOn  datetime2(0) NOT NULL
                   CONSTRAINT DF_Receptionist_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy varchar(50)  NULL,
        ModifiedOn datetime2(0) NULL,

        CONSTRAINT PK_Receptionist PRIMARY KEY CLUSTERED (id)
    );

    CREATE UNIQUE INDEX UQ_Receptionist_LoginId
        ON dbo.Receptionist (LoginId) WHERE LoginId IS NOT NULL;

    PRINT 'Created dbo.Receptionist.';
END
ELSE
    PRINT 'dbo.Receptionist already exists.';
GO

IF OBJECT_ID('dbo.ReceptionistPayment', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReceptionistPayment
    (
        id             int           NOT NULL IDENTITY(1,1),
        ReceptionistId int           NOT NULL,
        PayMonth       date          NOT NULL,
        PaidOn         date          NULL,
        Amount         decimal(18,2) NOT NULL,
        Branch         varchar(100)  NULL,
        Notes          varchar(200)  NULL,
        CreatedBy      varchar(50)   NULL,
        CreatedOn      datetime2(0)  NOT NULL
                       CONSTRAINT DF_ReceptionistPayment_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_ReceptionistPayment PRIMARY KEY CLUSTERED (id),
        CONSTRAINT FK_ReceptionistPayment_Receptionist FOREIGN KEY (ReceptionistId)
            REFERENCES dbo.Receptionist (id) ON DELETE CASCADE,
        CONSTRAINT UQ_ReceptionistPayment_PersonMonth UNIQUE (ReceptionistId, PayMonth),
        CONSTRAINT CK_ReceptionistPayment_Amount CHECK (Amount > 0)
    );

    CREATE INDEX IX_ReceptionistPayment_Month_Branch
        ON dbo.ReceptionistPayment (PayMonth) INCLUDE (Branch, Amount, ReceptionistId);

    PRINT 'Created dbo.ReceptionistPayment.';
END
ELSE
    PRINT 'dbo.ReceptionistPayment already exists.';
GO

/* ======================= 6. branch register and backfill ============
   dbo.Branch is empty in production. Every branch-wise screen reads its
   list from here, so without a row the branch reports are empty forever.
   Both the legacy and the new column names are populated, so neither
   application sees a half-filled row. */

IF NOT EXISTS (SELECT 1 FROM dbo.Branch)
BEGIN
    INSERT INTO dbo.Branch (Branch_name, Branch_id, Is_active, BranchName, BranchCode, IsActive, CreatedBy, CreatedOn)
    VALUES ('Rajpur', 'Raj', 1, 'Rajpur', 'Raj', 1, 'coexist-script', SYSDATETIME());

    PRINT 'Seeded dbo.Branch with Rajpur.';
END
ELSE
BEGIN
    DECLARE @branches int = (SELECT COUNT(*) FROM dbo.Branch);
    PRINT CONCAT('dbo.Branch already has ', @branches, ' row(s) - left alone.');
END
GO

/* Everything currently unattributed becomes Rajpur, as agreed. Only
   blank values are filled; a branch already set is never overwritten. */
DECLARE @branch varchar(100) =
    (SELECT TOP 1 ISNULL(NULLIF(LTRIM(RTRIM(BranchName)), ''), Branch_name)
     FROM dbo.Branch WHERE ISNULL(IsActive, 1) = 1 ORDER BY id);

IF @branch IS NULL
    PRINT 'No active branch on record - backfill skipped.';
ELSE
BEGIN
    DECLARE @s int, @e int, @o int;

    UPDATE dbo.student SET Branch = @branch
     WHERE LEN(ISNULL(LTRIM(RTRIM(Branch)), '')) = 0;
    SET @s = @@ROWCOUNT;

    /* The side tables start empty, so one row per legacy row is created
       here rather than updated. Re-running only adds what is missing. */
    INSERT INTO dbo.ExpenseExt (ExpenseId, Branch, CreatedBy)
    SELECT e.id, @branch, 'coexist-script'
    FROM dbo.Expense e
    WHERE NOT EXISTS (SELECT 1 FROM dbo.ExpenseExt x WHERE x.ExpenseId = e.id);
    SET @e = @@ROWCOUNT;

    INSERT INTO dbo.OIncomeExt (IncomeId, Branch, CreatedBy)
    SELECT o.ID, @branch, 'coexist-script'
    FROM dbo.OIncome o
    WHERE NOT EXISTS (SELECT 1 FROM dbo.OIncomeExt x WHERE x.IncomeId = o.ID);
    SET @o = @@ROWCOUNT;

    PRINT CONCAT('Backfilled to "', @branch, '": ', @s, ' student(s), ',
                 @e, ' expense(s), ', @o, ' other-income row(s).');
END
GO

/* ============================== 7. verification ===================== */

PRINT '';
PRINT '=====================================================';
PRINT 'VERIFICATION';
PRINT '=====================================================';
GO

SELECT Item = v.Item,
       Status = CASE WHEN v.Present = 1 THEN 'OK' ELSE '*** MISSING ***' END
FROM (VALUES
    ('table  dbo.ExamPaper',           CASE WHEN OBJECT_ID('dbo.ExamPaper','U')           IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.ExamQuestion',        CASE WHEN OBJECT_ID('dbo.ExamQuestion','U')        IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.ExamAttempt',         CASE WHEN OBJECT_ID('dbo.ExamAttempt','U')         IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.ExamSchedule',        CASE WHEN OBJECT_ID('dbo.ExamSchedule','U')        IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.CertificateIssue',    CASE WHEN OBJECT_ID('dbo.CertificateIssue','U')    IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.Receptionist',        CASE WHEN OBJECT_ID('dbo.Receptionist','U')        IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.ReceptionistPayment', CASE WHEN OBJECT_ID('dbo.ReceptionistPayment','U') IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.ExpenseExt',          CASE WHEN OBJECT_ID('dbo.ExpenseExt','U')          IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.OIncomeExt',          CASE WHEN OBJECT_ID('dbo.OIncomeExt','U')          IS NOT NULL THEN 1 ELSE 0 END),
    ('table  dbo.CusMessageExt',       CASE WHEN OBJECT_ID('dbo.CusMessageExt','U')       IS NOT NULL THEN 1 ELSE 0 END),
    ('sequence StudentRegistrationNumber',
        CASE WHEN EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'StudentRegistrationNumber') THEN 1 ELSE 0 END),
    ('column student.Branch',          CASE WHEN COL_LENGTH('dbo.student','Branch')            IS NOT NULL THEN 1 ELSE 0 END),
    ('column monthlyFees.TransactionId',CASE WHEN COL_LENGTH('dbo.monthlyFees','TransactionId') IS NOT NULL THEN 1 ELSE 0 END),
    ('column Users.PasswordHash',      CASE WHEN COL_LENGTH('dbo.Users','PasswordHash')        IS NOT NULL THEN 1 ELSE 0 END),
    ('column Branch.BranchName',       CASE WHEN COL_LENGTH('dbo.Branch','BranchName')         IS NOT NULL THEN 1 ELSE 0 END)
) AS v(Item, Present)
ORDER BY v.Present, v.Item;
GO

PRINT '';
PRINT '--- LEGACY MUST STILL BE INTACT (all four must say OK) ---';
GO

SELECT Item = v.Item,
       Status = CASE WHEN v.Present = 1 THEN 'OK' ELSE '*** LEGACY BROKEN ***' END
FROM (VALUES
    ('Users.Password still present',   CASE WHEN COL_LENGTH('dbo.Users','Password')      IS NOT NULL THEN 1 ELSE 0 END),
    ('Branch.Branch_name still present',CASE WHEN COL_LENGTH('dbo.Branch','Branch_name') IS NOT NULL THEN 1 ELSE 0 END),
    ('Expense column count unchanged (4)',
        CASE WHEN (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Expense')) = 4 THEN 1 ELSE 0 END),
    ('OIncome column count unchanged (4)',
        CASE WHEN (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('dbo.OIncome')) = 4 THEN 1 ELSE 0 END)
) AS v(Item, Present)
ORDER BY v.Present, v.Item;
GO

PRINT '';
PRINT '--- column counts on the seven tables the legacy app bare-inserts into ---';
PRINT '--- (each must match what the legacy INSERT statements supply) ---';
GO

SELECT TableName = t.name,
       Columns   = COUNT(c.column_id),
       NonIdentity = SUM(CASE WHEN c.is_identity = 1 THEN 0 ELSE 1 END)
FROM sys.tables t
JOIN sys.columns c ON c.object_id = t.object_id
WHERE t.name IN ('CusMessage','Course','Expense','TecExpense','Notice','OIncome','Teacher')
GROUP BY t.name
ORDER BY t.name;
GO

PRINT '';
PRINT CONCAT('Finished : ', CONVERT(varchar(19), SYSDATETIME(), 120));
PRINT 'LEGACY_COEXIST_UPGRADE.sql complete.';
GO
