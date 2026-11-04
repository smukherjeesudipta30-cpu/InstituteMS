-- Avijan PreSchool: the Champahati opening date, and the PreSchool's own
-- holiday notices.
--
-- PreSchoolSetting.OpensOn  the day the Champahati centre opens. Until then
--                           (or while it is empty) both front pages say it is
--                           coming soon.
-- PreSchoolSetting.OfficePhone  the Champahati office number, shared with the
--                           Computer Centre branch there; set with the
--                           PreSchool's own number when they are empty.
-- PreSchoolHoliday          days the PreSchool is closed, with a notice for
--                           parents. Kept apart from the Computer Centre's
--                           Holiday table, which is not touched.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.PreSchoolSetting', 'OpensOn') IS NULL
    ALTER TABLE dbo.PreSchoolSetting ADD OpensOn DATE NULL;
GO

-- The Champahati office number, shared by the PreSchool and the Computer
-- Centre branch in the same building. The PreSchool keeps its own in Phone.
IF COL_LENGTH('dbo.PreSchoolSetting', 'OfficePhone') IS NULL
    ALTER TABLE dbo.PreSchoolSetting ADD OfficePhone NVARCHAR(50) NULL;
GO

-- The numbers the owner gave for Champahati, filled only where still empty so
-- a number the PreSchool admin has since changed is never put back.
UPDATE dbo.PreSchoolSetting SET Phone = N'94338 83381' WHERE Id = 1 AND (Phone IS NULL OR Phone = N'');
UPDATE dbo.PreSchoolSetting SET OfficePhone = N'094778 89645' WHERE Id = 1 AND (OfficePhone IS NULL OR OfficePhone = N'');
GO

IF OBJECT_ID('dbo.PreSchoolHoliday') IS NULL
    CREATE TABLE dbo.PreSchoolHoliday (
        Id          INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolHoliday PRIMARY KEY,
        FromDate    DATE           NOT NULL,
        ToDate      DATE           NOT NULL,
        Title       NVARCHAR(150)  NOT NULL,
        Note        NVARCHAR(300)  NULL,
        CreatedBy   VARCHAR(250)   NULL,
        CreatedOn   DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolHoliday_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  VARCHAR(250)   NULL,
        ModifiedOn  DATETIME2      NULL,
        CONSTRAINT CK_PreSchoolHoliday_Dates CHECK (ToDate >= FromDate)
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolHoliday_FromDate' AND object_id = OBJECT_ID('dbo.PreSchoolHoliday'))
    CREATE INDEX IX_PreSchoolHoliday_FromDate ON dbo.PreSchoolHoliday (FromDate);
GO
