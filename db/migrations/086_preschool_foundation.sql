-- Avijan PreSchool, phase 1: its masters, in tables of its own.
--
-- Adds tables only. No Computer Centre table, column or row is changed, and
-- nothing that already exists reads these.
--
-- PreSchoolClass      Pre-Nursery, Nursery, KG-1, KG-2, with the admission fee.
-- PreSchoolSubject    what each class is examined in, with full marks.
-- PreSchoolFeeHead    each class's monthly charges (every PreSchool fee is monthly).
-- PreSchoolGradeBand  the grade for a percentage on the report card.
-- PreSchoolSetting    one row: the school's contact details, its recommended
--                     uniform shop and the receipt number prefix.
--
-- There is no session table: a child joins and completes a class on their own
-- dates.
--
-- Seeds the four classes (no fees yet) with a starting set of subjects, a
-- starting grade table and the settings row, each only when missing, so the
-- PreSchool admin's own changes are never overwritten.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolClass') IS NULL
    CREATE TABLE dbo.PreSchoolClass (
        Id            INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolClass PRIMARY KEY,
        Name          NVARCHAR(50)   NOT NULL,
        SortOrder     INT            NOT NULL CONSTRAINT DF_PreSchoolClass_SortOrder DEFAULT (0),
        AdmissionFee  DECIMAL(10,2)  NOT NULL CONSTRAINT DF_PreSchoolClass_AdmissionFee DEFAULT (0),
        MinAgeMonths  INT            NULL,
        MaxAgeMonths  INT            NULL,
        IsActive      BIT            NOT NULL CONSTRAINT DF_PreSchoolClass_IsActive DEFAULT (1),
        CreatedBy     VARCHAR(250)   NULL,
        CreatedOn     DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolClass_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy    VARCHAR(250)   NULL,
        ModifiedOn    DATETIME2      NULL,
        CONSTRAINT UQ_PreSchoolClass_Name UNIQUE (Name),
        CONSTRAINT CK_PreSchoolClass_AdmissionFee CHECK (AdmissionFee >= 0)
    );
GO

IF OBJECT_ID('dbo.PreSchoolSubject') IS NULL
    CREATE TABLE dbo.PreSchoolSubject (
        Id          INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolSubject PRIMARY KEY,
        ClassId     INT           NOT NULL CONSTRAINT FK_PreSchoolSubject_Class REFERENCES dbo.PreSchoolClass (Id),
        Name        NVARCHAR(80)  NOT NULL,
        MaxMarks    INT           NOT NULL CONSTRAINT DF_PreSchoolSubject_MaxMarks DEFAULT (100),
        SortOrder   INT           NOT NULL CONSTRAINT DF_PreSchoolSubject_SortOrder DEFAULT (0),
        IsActive    BIT           NOT NULL CONSTRAINT DF_PreSchoolSubject_IsActive DEFAULT (1),
        CreatedBy   VARCHAR(250)  NULL,
        CreatedOn   DATETIME2     NOT NULL CONSTRAINT DF_PreSchoolSubject_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  VARCHAR(250)  NULL,
        ModifiedOn  DATETIME2     NULL,
        CONSTRAINT UQ_PreSchoolSubject_ClassName UNIQUE (ClassId, Name),
        CONSTRAINT CK_PreSchoolSubject_MaxMarks CHECK (MaxMarks > 0)
    );
GO

IF OBJECT_ID('dbo.PreSchoolFeeHead') IS NULL
    CREATE TABLE dbo.PreSchoolFeeHead (
        Id             INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolFeeHead PRIMARY KEY,
        ClassId        INT            NOT NULL CONSTRAINT FK_PreSchoolFeeHead_Class REFERENCES dbo.PreSchoolClass (Id),
        Name           NVARCHAR(80)   NOT NULL,
        MonthlyAmount  DECIMAL(10,2)  NOT NULL CONSTRAINT DF_PreSchoolFeeHead_MonthlyAmount DEFAULT (0),
        SortOrder      INT            NOT NULL CONSTRAINT DF_PreSchoolFeeHead_SortOrder DEFAULT (0),
        IsActive       BIT            NOT NULL CONSTRAINT DF_PreSchoolFeeHead_IsActive DEFAULT (1),
        CreatedBy      VARCHAR(250)   NULL,
        CreatedOn      DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolFeeHead_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy     VARCHAR(250)   NULL,
        ModifiedOn     DATETIME2      NULL,
        CONSTRAINT UQ_PreSchoolFeeHead_ClassName UNIQUE (ClassId, Name),
        CONSTRAINT CK_PreSchoolFeeHead_MonthlyAmount CHECK (MonthlyAmount >= 0)
    );
GO

IF OBJECT_ID('dbo.PreSchoolGradeBand') IS NULL
    CREATE TABLE dbo.PreSchoolGradeBand (
        Id          INT           IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolGradeBand PRIMARY KEY,
        MinPercent  DECIMAL(5,2)  NOT NULL,
        Grade       NVARCHAR(10)  NOT NULL,
        Remark      NVARCHAR(80)  NULL,
        ModifiedBy  VARCHAR(250)  NULL,
        ModifiedOn  DATETIME2     NULL,
        CONSTRAINT UQ_PreSchoolGradeBand_Grade UNIQUE (Grade),
        CONSTRAINT CK_PreSchoolGradeBand_MinPercent CHECK (MinPercent BETWEEN 0 AND 100)
    );
GO

IF OBJECT_ID('dbo.PreSchoolSetting') IS NULL
    CREATE TABLE dbo.PreSchoolSetting (
        Id                  INT            NOT NULL CONSTRAINT PK_PreSchoolSetting PRIMARY KEY,
        SchoolName          NVARCHAR(150)  NOT NULL,
        Address             NVARCHAR(400)  NULL,
        Phone               NVARCHAR(50)   NULL,
        Email               NVARCHAR(150)  NULL,
        MapUrl              NVARCHAR(500)  NULL,
        UniformShopName     NVARCHAR(150)  NULL,
        UniformShopAddress  NVARCHAR(400)  NULL,
        UniformShopPhone    NVARCHAR(50)   NULL,
        UniformShopMapUrl   NVARCHAR(500)  NULL,
        ReceiptPrefix       NVARCHAR(10)   NOT NULL CONSTRAINT DF_PreSchoolSetting_ReceiptPrefix DEFAULT ('APS'),
        ModifiedBy          VARCHAR(250)   NULL,
        ModifiedOn          DATETIME2      NULL,
        CONSTRAINT CK_PreSchoolSetting_OneRow CHECK (Id = 1)
    );
GO

-- The four classes, in the order children move up.
INSERT INTO dbo.PreSchoolClass (Name, SortOrder, CreatedBy)
SELECT v.Name, v.SortOrder, 'migration 086'
FROM (VALUES (N'Pre-Nursery', 1), (N'Nursery', 2), (N'KG-1', 3), (N'KG-2', 4)) AS v (Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.PreSchoolClass c WHERE c.Name = v.Name);
GO

-- A starting set of subjects for every class, each only when that class does
-- not already have it, so a subject the PreSchool admin renamed or retired is
-- never added back beside it. Full marks 100; changed per class later.
INSERT INTO dbo.PreSchoolSubject (ClassId, Name, MaxMarks, SortOrder, CreatedBy)
SELECT c.Id, s.Name, 100, s.SortOrder, 'migration 086'
FROM dbo.PreSchoolClass c
CROSS JOIN (VALUES (N'English', 1), (N'Bengali', 2), (N'Mathematics', 3),
                   (N'Drawing', 4), (N'Rhymes', 5), (N'General Awareness', 6)) AS s (Name, SortOrder)
WHERE c.Name IN (N'Pre-Nursery', N'Nursery', N'KG-1', N'KG-2')
  AND NOT EXISTS (SELECT 1 FROM dbo.PreSchoolSubject x WHERE x.ClassId = c.Id AND x.Name = s.Name)
  AND NOT EXISTS (SELECT 1 FROM dbo.PreSchoolSubject y WHERE y.ClassId = c.Id AND y.CreatedBy <> 'migration 086');
GO

-- A starting grade table, only when there is none: once the PreSchool admin
-- has saved their own, re-running this must not add these back beside it.
IF NOT EXISTS (SELECT 1 FROM dbo.PreSchoolGradeBand)
    INSERT INTO dbo.PreSchoolGradeBand (MinPercent, Grade, Remark, ModifiedBy, ModifiedOn)
    VALUES (90, N'A+', N'Outstanding',       'migration 086', SYSDATETIME()),
           (80, N'A',  N'Excellent',         'migration 086', SYSDATETIME()),
           (70, N'B+', N'Very good',         'migration 086', SYSDATETIME()),
           (60, N'B',  N'Good',              'migration 086', SYSDATETIME()),
           (50, N'C',  N'Satisfactory',      'migration 086', SYSDATETIME()),
           (0,  N'D',  N'Needs improvement', 'migration 086', SYSDATETIME());
GO

IF NOT EXISTS (SELECT 1 FROM dbo.PreSchoolSetting WHERE Id = 1)
    INSERT INTO dbo.PreSchoolSetting (Id, SchoolName, Address, ReceiptPrefix, ModifiedBy, ModifiedOn)
    VALUES (1, N'Avijan PreSchool', N'First Floor, beside Champahati-Baruipur Main Road, Mouza Haral, Champahati, P.S. Baruipur, South 24 Parganas, West Bengal', N'APS', 'migration 086', SYSDATETIME());
GO
