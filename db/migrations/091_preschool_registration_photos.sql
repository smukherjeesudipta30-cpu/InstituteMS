-- Avijan PreSchool: the fuller registration form, photos and ID cards.
--
-- PreSchoolStudent gains
--   HeightCm, WeightKg      measured on the day of registration
--   GuardianRelation        how the parent to contact (ParentName) is related
--   Guardian2Name/Relation/Phone  the second guardian; each guardian gets an
--                           ID card for picking the child up
--
-- PreSchoolPhoto           one photo per slot, kept out of PreSchoolStudent so
--                          the student lists never load image bytes:
--                            Child, Guardian1, Guardian2  (StudentId set)
--                            SmartClass  the landing page's classroom picture
--                                        (StudentId empty)
--
-- Adds columns and a table only. No Computer Centre table is touched.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.PreSchoolStudent', 'HeightCm') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD HeightCm DECIMAL(5,1) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolStudent', 'WeightKg') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD WeightKg DECIMAL(5,1) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolStudent', 'GuardianRelation') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD GuardianRelation NVARCHAR(30) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolStudent', 'Guardian2Name') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD Guardian2Name NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolStudent', 'Guardian2Relation') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD Guardian2Relation NVARCHAR(30) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolStudent', 'Guardian2Phone') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD Guardian2Phone NVARCHAR(20) NULL;
GO

IF OBJECT_ID('dbo.PreSchoolPhoto') IS NULL
    CREATE TABLE dbo.PreSchoolPhoto (
        Id           INT             IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolPhoto PRIMARY KEY,
        StudentId    INT             NULL CONSTRAINT FK_PreSchoolPhoto_Student REFERENCES dbo.PreSchoolStudent (Id) ON DELETE CASCADE,
        Slot         NVARCHAR(20)    NOT NULL,
        ContentType  NVARCHAR(50)    NOT NULL,
        Data         VARBINARY(MAX)  NOT NULL,
        UpdatedBy    VARCHAR(250)    NULL,
        UpdatedOn    DATETIME2       NOT NULL CONSTRAINT DF_PreSchoolPhoto_UpdatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT CK_PreSchoolPhoto_Slot CHECK (
            (StudentId IS NOT NULL AND Slot IN ('Child', 'Guardian1', 'Guardian2'))
            OR (StudentId IS NULL AND Slot = 'SmartClass'))
    );
GO

-- One photo per child per slot, and one classroom picture for the site.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreSchoolPhoto_Student_Slot' AND object_id = OBJECT_ID('dbo.PreSchoolPhoto'))
    CREATE UNIQUE INDEX UX_PreSchoolPhoto_Student_Slot ON dbo.PreSchoolPhoto (StudentId, Slot) WHERE StudentId IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreSchoolPhoto_Site_Slot' AND object_id = OBJECT_ID('dbo.PreSchoolPhoto'))
    CREATE UNIQUE INDEX UX_PreSchoolPhoto_Site_Slot ON dbo.PreSchoolPhoto (Slot) WHERE StudentId IS NULL;
GO
