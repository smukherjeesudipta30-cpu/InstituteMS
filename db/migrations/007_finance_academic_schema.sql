/* =====================================================================
   007_finance_academic_schema.sql
   Target DB : Avijan_Angular_Migration
   Purpose   : Tables needed before the rest of the legacy data can be
               loaded (008) and before the dashboard can report real
               finance figures.

   Also relaxes the Users.Email unique index. MIGRATION_PLAN assumed email
   uniquely identified an account; the live data has three email addresses
   shared by two accounts each (all Student rows, created twice by
   mistake). Discarding one of each pair would delete a real account, so
   they are all migrated and AuthService resolves a login deterministically
   -- most recently registered active account wins -- and logs a warning.
   Run db/verify/compare_finance.sql to list them.

   Idempotent: safe to re-run.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

/* --------------------------------------------- Users: email no longer unique */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Users_Email' AND object_id = OBJECT_ID('dbo.Users'))
    DROP INDEX UX_Users_Email ON dbo.Users;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Users_Email' AND object_id = OBJECT_ID('dbo.Users'))
    CREATE INDEX IX_Users_Email ON dbo.Users (Email);
GO

/* Legacy Roles values are wider than the four in the plan: the live data also
   contains 'Receptionist' and the combined 'Receptionist & Teacher'. */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'Roles' AND max_length < 150)
    ALTER TABLE dbo.Users ALTER COLUMN Roles varchar(150) NULL;
GO

/* ------------------------------------------------------------- LateFees */
IF OBJECT_ID('dbo.LateFees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LateFees
    (
        id          int IDENTITY(1,1) NOT NULL,
        StuRegId    varchar(15)       NULL,
        BillNo      varchar(50)       NULL,
        FineFees    decimal(18,2)     NULL,
        DOD         date              NULL,   -- date of the fine
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_LateFees_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_LateFees PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_LateFees_StuRegId ON dbo.LateFees (StuRegId);
    CREATE INDEX IX_LateFees_DOD      ON dbo.LateFees (DOD);
END
GO

/* -------------------------------------------------------------- TecFees */
/* Teacher payout recorded per student payment. */
IF OBJECT_ID('dbo.TecFees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TecFees
    (
        id          int IDENTITY(1,1) NOT NULL,
        TecId       int               NULL,
        StuRegId    varchar(15)       NULL,
        BillNo      varchar(50)       NULL,
        TecSal      decimal(18,2)     NULL,
        UpdateDate  date              NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_TecFees_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_TecFees PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_TecFees_TecId      ON dbo.TecFees (TecId);
    CREATE INDEX IX_TecFees_StuRegId   ON dbo.TecFees (StuRegId);
    CREATE INDEX IX_TecFees_UpdateDate ON dbo.TecFees (UpdateDate);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TecFees_Teacher')
    ALTER TABLE dbo.TecFees ADD CONSTRAINT FK_TecFees_Teacher
        FOREIGN KEY (TecId) REFERENCES dbo.Teacher (id);
GO

/* ----------------------------------------------------------- TecExpense */
IF OBJECT_ID('dbo.TecExpense', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.TecExpense
    (
        id          int IDENTITY(1,1) NOT NULL,
        TecId       int               NULL,
        InDate      date              NULL,
        Amount      decimal(18,2)     NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_TecExpense_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_TecExpense PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_TecExpense_TecId  ON dbo.TecExpense (TecId);
    CREATE INDEX IX_TecExpense_InDate ON dbo.TecExpense (InDate);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TecExpense_Teacher')
    ALTER TABLE dbo.TecExpense ADD CONSTRAINT FK_TecExpense_Teacher
        FOREIGN KEY (TecId) REFERENCES dbo.Teacher (id);
GO

/* ----------------------------------------------------------------- Marks */
IF OBJECT_ID('dbo.Marks', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Marks
    (
        id          int IDENTITY(1,1) NOT NULL,
        RegId       varchar(15)       NULL,
        ExDate      date              NULL,
        Sub1        varchar(30)       NULL,  M1 int NULL,  OM1 int NULL,
        Sub2        varchar(30)       NULL,  M2 int NULL,  OM2 int NULL,
        Sub3        varchar(30)       NULL,  M3 int NULL,  OM3 int NULL,
        Sub4        varchar(30)       NULL,  M4 int NULL,  OM4 int NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_Marks_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  varchar(50)       NULL,
        ModifiedOn  datetime2(0)      NULL,
        CONSTRAINT PK_Marks PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_Marks_RegId ON dbo.Marks (RegId);
END
GO

/* ---------------------------------------------------------------- Branch */
IF OBJECT_ID('dbo.Branch', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Branch
    (
        id          int IDENTITY(1,1) NOT NULL,
        BranchName  varchar(50)       NULL,
        BranchCode  varchar(50)       NULL,
        IsActive    bit               NOT NULL CONSTRAINT DF_Branch_IsActive DEFAULT (1),
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_Branch_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_Branch PRIMARY KEY CLUSTERED (id)
    );
END
GO

/* ------------------------------------------------------------------ Fees */
/* Legacy per-payment income roll-up (Models/Income.cs E7). */
IF OBJECT_ID('dbo.Fees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Fees
    (
        id          int IDENTITY(1,1) NOT NULL,
        StuReg      varchar(15)       NULL,
        Total       decimal(18,2)     NULL,
        StoreDate   date              NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_Fees_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_Fees PRIMARY KEY CLUSTERED (id)
    );
    CREATE INDEX IX_Fees_StuReg    ON dbo.Fees (StuReg);
    CREATE INDEX IX_Fees_StoreDate ON dbo.Fees (StoreDate);
END
GO

PRINT '007_finance_academic_schema.sql applied.';
GO
