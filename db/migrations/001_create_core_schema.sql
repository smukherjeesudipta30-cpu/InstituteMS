/* =====================================================================
   001_create_core_schema.sql
   Target DB : Avijan_Angular_Migration
   Purpose   : Core schema for the Angular 19 / .NET 8 migration.
               Mirrors the legacy ph19987796653_ shapes (§2.1 of
               MIGRATION_PLAN.md) with the phase-1 hardening applied:
                 - real primary keys and foreign keys
                 - date/decimal columns instead of varchar
                 - Users security columns (hash, refresh token, lockout)
   Idempotent: safe to re-run.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;   -- sqlcmd defaults this OFF; filtered indexes require ON
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

/* ---------------------------------------------------------------- Course */
IF OBJECT_ID('dbo.Course', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Course
    (
        Cid         int IDENTITY(1,1) NOT NULL,
        CName       varchar(300)      NULL,
        Duration    int               NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_Course_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(50)       NULL,
        ModifiedOn  datetime2(0)      NULL,
        CONSTRAINT PK_Course PRIMARY KEY CLUSTERED (Cid)
    );
END
GO

/* --------------------------------------------------------------- Teacher */
IF OBJECT_ID('dbo.Teacher', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Teacher
    (
        id          int IDENTITY(1,1) NOT NULL,
        TecName     varchar(50)       NULL,
        LoginId     varchar(20)       NULL,
        TecPhone    varchar(10)       NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_Teacher_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(50)       NULL,
        ModifiedOn  datetime2(0)      NULL,
        CONSTRAINT PK_Teacher PRIMARY KEY CLUSTERED (id)
    );
END
GO

/* --------------------------------------------------------------- Student */
IF OBJECT_ID('dbo.Student', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Student
    (
        id                  int IDENTITY(1,1) NOT NULL,
        StuName             varchar(30)       NULL,
        GName               varchar(300)      NULL,
        Stuaddress          varchar(max)      NULL,
        StuPhone            varchar(10)       NULL,
        StuEmail            varchar(50)       NULL,
        StuRegId            varchar(15)       NULL,   -- business key: AVI/{n}/{yy}
        NoTech              int               NULL,
        StuAdFees           decimal(18,2)     NULL,
        Stu_monthly_fees    decimal(18,2)     NULL,
        DOJ                 date              NULL,
        DOB                 date              NULL,
        DOE                 date              NULL,
        BillNo              varchar(50)       NULL,   -- S-A-{n}-{yy}
        Cid                 int               NULL,
        TecName             int               NULL,
        TecName1            int               NULL,
        TecName2            int               NULL,
        IsActive            bit               NOT NULL CONSTRAINT DF_Student_IsActive   DEFAULT (0),
        IsComplete          bit               NOT NULL CONSTRAINT DF_Student_IsComplete DEFAULT (0),
        loginId             varchar(20)       NULL,
        Branch              varchar(50)       NULL,
        CreatedBy           varchar(50)       NULL,
        CreatedOn           datetime2(0)      NOT NULL CONSTRAINT DF_Student_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy          varchar(50)       NULL,
        ModifiedOn          datetime2(0)      NULL,
        CONSTRAINT PK_Student PRIMARY KEY CLUSTERED (id)
    );
END
GO

/* Non-unique on purpose. MIGRATION_PLAN §2.3 called for a unique index here, but
   the live data has 12 registration ids shared by 2-3 students each -- fallout
   from the unlocked number generator in Models/Student.cs:108-146 (defect D4).
   See the header of 003 for the full reasoning. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Student_StuRegId' AND object_id = OBJECT_ID('dbo.Student'))
    CREATE INDEX IX_Student_StuRegId ON dbo.Student (StuRegId);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Student_Course')
    ALTER TABLE dbo.Student ADD CONSTRAINT FK_Student_Course
        FOREIGN KEY (Cid) REFERENCES dbo.Course (Cid);
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Student_Teacher1')
    ALTER TABLE dbo.Student ADD CONSTRAINT FK_Student_Teacher1
        FOREIGN KEY (TecName)  REFERENCES dbo.Teacher (id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Student_Teacher2')
    ALTER TABLE dbo.Student ADD CONSTRAINT FK_Student_Teacher2
        FOREIGN KEY (TecName1) REFERENCES dbo.Teacher (id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Student_Teacher3')
    ALTER TABLE dbo.Student ADD CONSTRAINT FK_Student_Teacher3
        FOREIGN KEY (TecName2) REFERENCES dbo.Teacher (id);
GO

/* ------------------------------------------------------------ monthlyFees */
IF OBJECT_ID('dbo.MonthlyFees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MonthlyFees
    (
        id              int IDENTITY(1,1) NOT NULL,
        StuRegId        varchar(15)       NULL,
        BillNo          varchar(50)       NULL,
        Date1           date              NULL,
        PDate           date              NULL,
        Installment     decimal(18,2)     NULL,
        OInstallment    decimal(18,2)     NULL,
        percen          decimal(18,2)     NULL,
        OldDue          decimal(18,2)     NULL,
        NewDue          decimal(18,2)     NULL,
        CreatedBy       varchar(50)       NULL,
        CreatedOn       datetime2(0)      NOT NULL CONSTRAINT DF_MonthlyFees_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy      varchar(50)       NULL,
        ModifiedOn      datetime2(0)      NULL,
        CONSTRAINT PK_MonthlyFees PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_MonthlyFees_StuRegId ON dbo.MonthlyFees (StuRegId);
    CREATE INDEX IX_MonthlyFees_Date1    ON dbo.MonthlyFees (Date1);
END
GO

/* --------------------------------------------------------------- Expense */
IF OBJECT_ID('dbo.Expense', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Expense
    (
        id          int IDENTITY(1,1) NOT NULL,
        ExDetails   varchar(max)      NULL,
        ExDate      date              NULL,
        Amount      decimal(18,2)     NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_Expense_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(50)       NULL,
        ModifiedOn  datetime2(0)      NULL,
        CONSTRAINT PK_Expense PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_Expense_ExDate ON dbo.Expense (ExDate);
END
GO

/* --------------------------------------------------------------- OIncome */
IF OBJECT_ID('dbo.OIncome', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OIncome
    (
        ID          int IDENTITY(1,1) NOT NULL,
        IPurpose    varchar(max)      NULL,
        IAmount     decimal(18,2)     NULL,
        ODate       date              NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_OIncome_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(50)       NULL,
        ModifiedOn  datetime2(0)      NULL,
        CONSTRAINT PK_OIncome PRIMARY KEY CLUSTERED (ID)
    );
    CREATE INDEX IX_OIncome_ODate ON dbo.OIncome (ODate);
END
GO

/* ---------------------------------------------------------------- Notice */
IF OBJECT_ID('dbo.Notice', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Notice
    (
        NID         int IDENTITY(1,1) NOT NULL,
        NewNotice   varchar(max)      NULL,
        NoticeDate  datetime2(0)      NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_Notice_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(50)       NULL,
        ModifiedOn  datetime2(0)      NULL,
        CONSTRAINT PK_Notice PRIMARY KEY CLUSTERED (NID)
    );
END
GO

/* ----------------------------------------------------------------- Users */
IF OBJECT_ID('dbo.Users', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users
    (
        id                  int IDENTITY(1,1) NOT NULL,
        Name                varchar(250)      NULL,
        Email               varchar(50)       NULL,
        Roles               varchar(150)      NULL,   -- Admin | Editor | Student | Teacher
        Loginid             varchar(20)       NULL,
        Phn_no              varchar(50)       NULL,
        User_date_of_birth  date              NULL,
        Registerdate        date              NULL,
        End_date            date              NULL,
        IsActive            bit               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),

        /* --- §2.3 / 003_users_security --- */
        PasswordHash        nvarchar(255)     NULL,
        RefreshToken        nvarchar(255)     NULL,
        RefreshTokenExpiry  datetime2(0)      NULL,
        FailedLoginCount    int               NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT (0),
        LockoutEnd          datetime2(0)      NULL,
        LastLoginOn         datetime2(0)      NULL,

        CreatedBy           varchar(50)       NULL,
        CreatedOn           datetime2(0)      NOT NULL CONSTRAINT DF_Users_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy          varchar(50)       NULL,
        ModifiedOn          datetime2(0)      NULL,
        CONSTRAINT PK_Users PRIMARY KEY CLUSTERED (id)
    );
END
GO

/* Single indexed lookup for login — replaces the legacy full-table scan (D3). */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Users_Email' AND object_id = OBJECT_ID('dbo.Users'))
    CREATE UNIQUE INDEX UX_Users_Email ON dbo.Users (Email) WHERE Email IS NOT NULL;
GO

/* ------------------------------------------------------------ CusMessage */
/* Contact-us submissions from the public About Us page (§4.7 N4). */
IF OBJECT_ID('dbo.CusMessage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CusMessage
    (
        id          int IDENTITY(1,1) NOT NULL,
        CusName     varchar(200)      NULL,
        CusMailid   varchar(200)      NULL,
        CusPhone    varchar(20)       NULL,
        cusbody     varchar(max)      NULL,
        SubDate     datetime2(0)      NOT NULL CONSTRAINT DF_CusMessage_SubDate DEFAULT (SYSDATETIME()),
        SourceIp    varchar(64)       NULL,
        IsHandled   bit               NOT NULL CONSTRAINT DF_CusMessage_IsHandled DEFAULT (0),
        CONSTRAINT PK_CusMessage PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_CusMessage_SubDate ON dbo.CusMessage (SubDate DESC);
END
GO

PRINT '001_create_core_schema.sql applied.';
GO
