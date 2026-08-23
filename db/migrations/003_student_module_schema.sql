/* =====================================================================
   003_student_module_schema.sql
   Target DB : Avijan_Angular_Migration
   Purpose   : Schema changes needed before the legacy Student data can be
               loaded (004) and before the Students API goes live.
   Idempotent: safe to re-run.

   Three changes, each driven by something found in the real data:

   1. StuRegId UNIQUE -> non-unique.
      MIGRATION_PLAN §2.3 assumed StuRegId could carry a unique index (and a
      FK from MonthlyFees). The live data has 12 registration ids shared by
      2-3 students each -- fallout from the race condition in
      Models/Student.cs:108-146 (defect D4), where the next number came from
      "SELECT TOP 1 id ... WHERE IsActive=1 ORDER BY id DESC" with no lock.
      Some of those pairs are genuinely different people (e.g. AVI/139/20 is
      both Pradip Pramanik and Diptadip Ghosh). Renumbering them here would
      silently rewrite a business key that MonthlyFees, Marks, TecFees and
      ImageUpload all join on, so the data is migrated verbatim and the
      constraint is relaxed instead. New registrations are still guaranteed
      unique by RegistrationNumberService, which takes a lock.

   2. Extra Student columns carried over from the legacy table.
      Avijan_login_Password is deliberately NOT recreated -- it is a
      plain-text credential column (defect D3). Student portal logins belong
      in Users.PasswordHash.

   3. ImageUpload gets a real StuRegId column instead of overloading
      Description (MIGRATION_PLAN §2.3, script 006).
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

/* ------------------------------------------- 1. relax the StuRegId index */
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Student_StuRegId' AND object_id = OBJECT_ID('dbo.Student'))
    DROP INDEX UX_Student_StuRegId ON dbo.Student;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Student_StuRegId' AND object_id = OBJECT_ID('dbo.Student'))
    CREATE INDEX IX_Student_StuRegId ON dbo.Student (StuRegId);
GO

/* Supporting indexes for the list/search screens. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Student_IsActive_Id' AND object_id = OBJECT_ID('dbo.Student'))
    CREATE INDEX IX_Student_IsActive_Id ON dbo.Student (IsActive, id DESC);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Student_StuName' AND object_id = OBJECT_ID('dbo.Student'))
    CREATE INDEX IX_Student_StuName ON dbo.Student (StuName);
GO

/* --------------------------------------------- 2. extra Student columns */
IF COL_LENGTH('dbo.Student', 'Personal_mail_id') IS NULL
    ALTER TABLE dbo.Student ADD Personal_mail_id varchar(150) NULL;
GO

IF COL_LENGTH('dbo.Student', 'Avijan_login_mail_id') IS NULL
    ALTER TABLE dbo.Student ADD Avijan_login_mail_id varchar(150) NULL;
GO

/* Legacy self-reference written by usp_StudentSaveData (Replica_id = own id). */
IF COL_LENGTH('dbo.Student', 'Replica_id') IS NULL
    ALTER TABLE dbo.Student ADD Replica_id int NULL;
GO

/* StuName is varchar(30) in the legacy table and some real names are longer
   once trimmed; widen so the load cannot truncate. */
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Student')
             AND name = 'StuName' AND max_length < 100)
    ALTER TABLE dbo.Student ALTER COLUMN StuName varchar(100) NULL;
GO

/* ------------------------------------------------------- 3. ImageUpload */
IF OBJECT_ID('dbo.ImageUpload', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ImageUpload
    (
        FileID      int IDENTITY(1,1) NOT NULL,
        FileName    varchar(100)      NULL,
        /* Kept for byte-compatibility with the legacy join
           (ImageUpload.Description = Student.StuRegId). */
        Description varchar(max)      NULL,
        /* The normalised replacement for that join. */
        StuRegId    varchar(15)       NULL,
        FilePath    varchar(max)      NULL,   -- Base64 payload, as in the legacy app
        FileSize    int               NULL,
        ContentType varchar(100)      NULL,
        CreatedBy   varchar(50)       NULL,
        CreatedOn   datetime2(0)      NOT NULL CONSTRAINT DF_ImageUpload_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_ImageUpload PRIMARY KEY CLUSTERED (FileID)
    );
    CREATE INDEX IX_ImageUpload_StuRegId ON dbo.ImageUpload (StuRegId);
END
GO

PRINT '003_student_module_schema.sql applied.';
GO
