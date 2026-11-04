-- Avijan PreSchool: staff on a fixed monthly salary, their salary payments,
-- and the PreSchool's own expenses — the other half of its income report.
--
-- PreSchoolStaff          a receptionist, teacher or housekeeper, with a
--                         monthly salary. No percentage, commission or bonus.
-- PreSchoolSalaryPayment  one month's salary paid to one person (one per
--                         person per month), with any deduction for absence.
-- PreSchoolExpense        rent, electricity, materials and so on. Salaries
--                         are not entered here; they come from the payments.
--
-- Adds tables only. The Computer Centre's teachers, receptionists, payroll and
-- expenses are not touched.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolStaff') IS NULL
    CREATE TABLE dbo.PreSchoolStaff (
        Id             INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolStaff PRIMARY KEY,
        Name           NVARCHAR(100)  NOT NULL,
        Role           NVARCHAR(20)   NOT NULL,
        Phone          NVARCHAR(20)   NULL,
        Email          NVARCHAR(150)  NULL,
        JoinedOn       DATE           NOT NULL,
        LeftOn         DATE           NULL,
        MonthlySalary  DECIMAL(10,2)  NOT NULL,
        Notes          NVARCHAR(300)  NULL,
        CreatedBy      VARCHAR(250)   NULL,
        CreatedOn      DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolStaff_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy     VARCHAR(250)   NULL,
        ModifiedOn     DATETIME2      NULL,
        CONSTRAINT CK_PreSchoolStaff_Role CHECK (Role IN ('Receptionist', 'Teacher', 'Housekeeper')),
        CONSTRAINT CK_PreSchoolStaff_Salary CHECK (MonthlySalary >= 0)
    );
GO

IF OBJECT_ID('dbo.PreSchoolSalaryPayment') IS NULL
    CREATE TABLE dbo.PreSchoolSalaryPayment (
        Id          INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolSalaryPayment PRIMARY KEY,
        StaffId     INT            NOT NULL CONSTRAINT FK_PreSchoolSalaryPayment_Staff REFERENCES dbo.PreSchoolStaff (Id),
        ForMonth    DATE           NOT NULL,
        PaidOn      DATE           NOT NULL,
        Salary      DECIMAL(10,2)  NOT NULL,
        DaysAbsent  DECIMAL(5,1)   NOT NULL CONSTRAINT DF_PreSchoolSalaryPayment_DaysAbsent DEFAULT (0),
        Deduction   DECIMAL(10,2)  NOT NULL CONSTRAINT DF_PreSchoolSalaryPayment_Deduction DEFAULT (0),
        NetPaid     DECIMAL(10,2)  NOT NULL,
        Mode        NVARCHAR(10)   NOT NULL,
        Reference   NVARCHAR(60)   NULL,
        Notes       NVARCHAR(300)  NULL,
        CreatedBy   VARCHAR(250)   NULL,
        CreatedOn   DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolSalaryPayment_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT UQ_PreSchoolSalaryPayment_StaffMonth UNIQUE (StaffId, ForMonth),
        CONSTRAINT CK_PreSchoolSalaryPayment_Mode CHECK (Mode IN ('Cash', 'Bank', 'UPI')),
        CONSTRAINT CK_PreSchoolSalaryPayment_Amounts CHECK (Deduction >= 0 AND NetPaid >= 0 AND Deduction <= Salary)
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolSalaryPayment_PaidOn' AND object_id = OBJECT_ID('dbo.PreSchoolSalaryPayment'))
    CREATE INDEX IX_PreSchoolSalaryPayment_PaidOn ON dbo.PreSchoolSalaryPayment (PaidOn);
GO

IF OBJECT_ID('dbo.PreSchoolExpense') IS NULL
    CREATE TABLE dbo.PreSchoolExpense (
        Id           INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolExpense PRIMARY KEY,
        SpentOn      DATE           NOT NULL,
        Head         NVARCHAR(60)   NOT NULL,
        Description  NVARCHAR(300)  NULL,
        Amount       DECIMAL(10,2)  NOT NULL,
        Mode         NVARCHAR(10)   NOT NULL,
        Reference    NVARCHAR(60)   NULL,
        CreatedBy    VARCHAR(250)   NULL,
        CreatedOn    DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolExpense_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy   VARCHAR(250)   NULL,
        ModifiedOn   DATETIME2      NULL,
        CONSTRAINT CK_PreSchoolExpense_Amount CHECK (Amount > 0),
        CONSTRAINT CK_PreSchoolExpense_Mode CHECK (Mode IN ('Cash', 'Bank', 'UPI'))
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolExpense_SpentOn' AND object_id = OBJECT_ID('dbo.PreSchoolExpense'))
    CREATE INDEX IX_PreSchoolExpense_SpentOn ON dbo.PreSchoolExpense (SpentOn);
GO
