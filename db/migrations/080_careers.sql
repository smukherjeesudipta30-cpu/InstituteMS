-- Job vacancies and applications.
--
-- Admin advertises Receptionist vacancies per branch (shift timing, academic
-- criteria) on the public site; anyone can apply with a CV and photograph.
-- Each application raises a dashboard alert for Admin, Editor and SuperAdmin.
-- The CV and photo are kept on the row as Base64, the same way
-- competition documents are.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.JobVacancy') IS NULL
    CREATE TABLE dbo.JobVacancy (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobVacancy PRIMARY KEY,
        Title         NVARCHAR(100)  NOT NULL CONSTRAINT DF_JobVacancy_Title DEFAULT N'Receptionist',
        Branch        NVARCHAR(100)  NOT NULL,
        ShiftName     NVARCHAR(50)   NULL,
        ShiftStart    TIME(0)        NULL,
        ShiftEnd      TIME(0)        NULL,
        WorkingDays   NVARCHAR(100)  NULL,
        Qualification NVARCHAR(300)  NOT NULL,
        Experience    NVARCHAR(200)  NULL,
        Salary        NVARCHAR(100)  NULL,
        Description   NVARCHAR(2000) NULL,
        Openings      INT            NOT NULL CONSTRAINT DF_JobVacancy_Openings DEFAULT 1,
        ClosesOn      DATE           NULL,
        IsActive      BIT            NOT NULL CONSTRAINT DF_JobVacancy_IsActive DEFAULT 1,
        CreatedBy     VARCHAR(250)   NULL,
        CreatedOn     DATETIME2      NOT NULL CONSTRAINT DF_JobVacancy_CreatedOn DEFAULT SYSDATETIME(),
        ModifiedBy    VARCHAR(250)   NULL,
        ModifiedOn    DATETIME2      NULL
    );
GO

IF OBJECT_ID('dbo.JobApplication') IS NULL
BEGIN
    CREATE TABLE dbo.JobApplication (
        Id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobApplication PRIMARY KEY,
        VacancyId        INT            NOT NULL CONSTRAINT FK_JobApplication_Vacancy REFERENCES dbo.JobVacancy (Id),
        Name             NVARCHAR(100)  NOT NULL,
        Phone            VARCHAR(15)    NOT NULL,
        Email            NVARCHAR(150)  NULL,
        Qualification    NVARCHAR(300)  NOT NULL,
        Experience       NVARCHAR(300)  NULL,
        Address          NVARCHAR(300)  NULL,
        Message          NVARCHAR(1000) NULL,
        CvFileName       NVARCHAR(200)  NULL,
        CvContentType    VARCHAR(100)   NULL,
        CvContent        NVARCHAR(MAX)  NOT NULL,
        PhotoContentType VARCHAR(50)    NULL,
        PhotoContent     NVARCHAR(MAX)  NOT NULL,
        Status           VARCHAR(20)    NOT NULL CONSTRAINT DF_JobApplication_Status DEFAULT 'New',
        AdminNote        NVARCHAR(500)  NULL,
        AppliedOn        DATETIME2      NOT NULL CONSTRAINT DF_JobApplication_AppliedOn DEFAULT SYSDATETIME(),
        ReviewedBy       VARCHAR(250)   NULL,
        ReviewedOn       DATETIME2      NULL
    );

END
GO

-- One application per person (phone) in any three months, for any vacancy -
-- checked by the service. This index is what that check reads.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_JobApplication_Vacancy_Phone')
    DROP INDEX UX_JobApplication_Vacancy_Phone ON dbo.JobApplication;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_JobApplication_Phone_AppliedOn')
    CREATE INDEX IX_JobApplication_Phone_AppliedOn ON dbo.JobApplication (Phone, AppliedOn);
GO
