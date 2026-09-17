/* =====================================================================
   024_receptionists_and_branch_backfill.sql
   ---------------------------------------------------------------------
   Two things:

   1. BRANCH BACKFILL. Every student and expense with no branch is set
      to the institute's only branch, Rajpur. This is not a guess: with
      one branch on record, unattributed records ARE Rajpur records, and
      leaving them as "Unassigned" made every branch report a single
      enormous bar that told nobody anything.

      NOTE this cannot be undone from the data alone. Once a row says
      Rajpur there is nothing left to distinguish "was always Rajpur"
      from "was blank and backfilled". Counts are printed so the scale
      of the change is on the record.

      Only blank rows are touched; anything already naming a branch is
      left exactly as it is.

   2. RECEPTIONIST MASTER. Receptionists exist as sign-ins (7 users
      carry the role) but have never had a master record, so there was
      nowhere to record who they are, which branch they work at, or what
      they have been paid.

      Two tables, mirroring the teacher shape so the screens and reports
      read the same way:

        Receptionist         one row per person
        ReceptionistPayment  one row per payment made to them

      A receptionist belongs to a branch, and so does each payment - the
      branch is copied onto the payment so a report stays correct even
      if someone later moves between branches.

   Run:  sqlcmd -S . -I -i migrations\024_receptionists_and_branch_backfill.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

/* ============================================== 1. branch backfill */

DECLARE @branch varchar(100) = (SELECT TOP 1 BranchName FROM dbo.Branch WHERE IsActive = 1 ORDER BY id);

IF @branch IS NULL
BEGIN
    PRINT 'No active branch on record - backfill skipped.';
END
ELSE
BEGIN
    DECLARE @students int, @expenses int;

    UPDATE dbo.Student
       SET Branch = @branch
     WHERE LEN(ISNULL(LTRIM(RTRIM(Branch)), '')) = 0;

    SET @students = @@ROWCOUNT;

    UPDATE dbo.Expense
       SET Branch = @branch
     WHERE LEN(ISNULL(LTRIM(RTRIM(Branch)), '')) = 0;

    SET @expenses = @@ROWCOUNT;

    PRINT CONCAT('Backfilled to "', @branch, '": ', @students, ' student(s), ', @expenses, ' expense(s).');
END
GO

/* ========================================= 2. receptionist master */

IF OBJECT_ID('dbo.Receptionist', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Receptionist
    (
        id         int            NOT NULL IDENTITY(1,1),

        RecName    varchar(100)   NOT NULL,

        /* How they sign in, matching dbo.Users.Email. Nullable because a
           receptionist may be on the payroll before an account exists. */
        LoginId    varchar(100)   NULL,

        Phone      varchar(20)    NULL,

        /* Which branch they work at. Free text matching Student.Branch and
           Expense.Branch, so all three group together in a report. */
        Branch     varchar(100)   NULL,

        IsActive   bit            NOT NULL CONSTRAINT DF_Receptionist_IsActive DEFAULT (1),

        CreatedBy  varchar(50)    NULL,
        CreatedOn  datetime2(0)   NOT NULL
                   CONSTRAINT DF_Receptionist_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy varchar(50)    NULL,
        ModifiedOn datetime2(0)   NULL,

        CONSTRAINT PK_Receptionist PRIMARY KEY CLUSTERED (id)
    );

    PRINT 'Created dbo.Receptionist.';
END
ELSE
    PRINT 'dbo.Receptionist already exists.';
GO

/* One sign-in belongs to one receptionist. Filtered so the NULLs - people
   with no account yet - are not constrained against each other. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_Receptionist_LoginId' AND object_id = OBJECT_ID('dbo.Receptionist'))
BEGIN
    CREATE UNIQUE INDEX UQ_Receptionist_LoginId
        ON dbo.Receptionist (LoginId) WHERE LoginId IS NOT NULL;

    PRINT 'Created UQ_Receptionist_LoginId.';
END
GO

IF OBJECT_ID('dbo.ReceptionistPayment', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReceptionistPayment
    (
        id             int            NOT NULL IDENTITY(1,1),

        ReceptionistId int            NOT NULL,

        /* The month this payment is for. Stored as the first of the month
           so month-wise grouping needs no date arithmetic. */
        PayMonth       date           NOT NULL,

        /* When it was actually handed over, which may not be in PayMonth. */
        PaidOn         date           NULL,

        Amount         decimal(18,2)  NOT NULL,

        /* Copied from the receptionist at the time of payment, so a report
           stays correct if they later move branch. */
        Branch         varchar(100)   NULL,

        Notes          varchar(200)   NULL,

        CreatedBy      varchar(50)    NULL,
        CreatedOn      datetime2(0)   NOT NULL
                       CONSTRAINT DF_ReceptionistPayment_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_ReceptionistPayment PRIMARY KEY CLUSTERED (id),

        CONSTRAINT FK_ReceptionistPayment_Receptionist FOREIGN KEY (ReceptionistId)
            REFERENCES dbo.Receptionist (id) ON DELETE CASCADE,

        /* One payment per person per month. A second payment for the same
           month is a correction to the first, not another salary. */
        CONSTRAINT UQ_ReceptionistPayment_PersonMonth UNIQUE (ReceptionistId, PayMonth),

        CONSTRAINT CK_ReceptionistPayment_Amount CHECK (Amount > 0)
    );

    PRINT 'Created dbo.ReceptionistPayment.';
END
ELSE
    PRINT 'dbo.ReceptionistPayment already exists.';
GO

/* The report groups by branch and month inside a date range. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_ReceptionistPayment_Month_Branch'
                 AND object_id = OBJECT_ID('dbo.ReceptionistPayment'))
BEGIN
    CREATE INDEX IX_ReceptionistPayment_Month_Branch
        ON dbo.ReceptionistPayment (PayMonth) INCLUDE (Branch, Amount, ReceptionistId);

    PRINT 'Created IX_ReceptionistPayment_Month_Branch.';
END
GO

PRINT '024 complete.';
GO
