-- Avijan PreSchool: marks from its offline exams.
--
-- PreSchoolExam  one exam the school holds for every class (e.g. "1st Term",
--                "Half Yearly", "Annual"), with the day it was held.
-- PreSchoolMark  one child's mark in one subject of their class in one exam,
--                or absent. Full marks come from the subject (PreSchoolSubject).
--
-- Marks only: no report card or ID card is printed for the PreSchool. Grades
-- shown beside the marks come from the PreSchool's grade table.
--
-- Adds tables only. Nothing in the Computer Centre's exams is touched.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolExam') IS NULL
    CREATE TABLE dbo.PreSchoolExam (
        Id          INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolExam PRIMARY KEY,
        Name        NVARCHAR(80)   NOT NULL,
        HeldOn      DATE           NULL,
        SortOrder   INT            NOT NULL CONSTRAINT DF_PreSchoolExam_SortOrder DEFAULT (0),
        IsActive    BIT            NOT NULL CONSTRAINT DF_PreSchoolExam_IsActive DEFAULT (1),
        CreatedBy   VARCHAR(250)   NULL,
        CreatedOn   DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolExam_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  VARCHAR(250)   NULL,
        ModifiedOn  DATETIME2      NULL,
        CONSTRAINT UQ_PreSchoolExam_Name UNIQUE (Name)
    );
GO

IF OBJECT_ID('dbo.PreSchoolMark') IS NULL
    CREATE TABLE dbo.PreSchoolMark (
        Id          INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolMark PRIMARY KEY,
        ExamId      INT            NOT NULL CONSTRAINT FK_PreSchoolMark_Exam REFERENCES dbo.PreSchoolExam (Id),
        StudentId   INT            NOT NULL CONSTRAINT FK_PreSchoolMark_Student REFERENCES dbo.PreSchoolStudent (Id),
        SubjectId   INT            NOT NULL CONSTRAINT FK_PreSchoolMark_Subject REFERENCES dbo.PreSchoolSubject (Id),
        Marks       DECIMAL(6,2)   NULL,
        IsAbsent    BIT            NOT NULL CONSTRAINT DF_PreSchoolMark_IsAbsent DEFAULT (0),
        UpdatedBy   VARCHAR(250)   NULL,
        UpdatedOn   DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolMark_UpdatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT UQ_PreSchoolMark UNIQUE (ExamId, StudentId, SubjectId),
        CONSTRAINT CK_PreSchoolMark_Marks CHECK (Marks IS NULL OR Marks >= 0)
    );
GO

-- A start: the three usual sittings. The office renames or adds more.
IF NOT EXISTS (SELECT 1 FROM dbo.PreSchoolExam)
    INSERT dbo.PreSchoolExam (Name, SortOrder, CreatedBy)
    VALUES (N'1st Term', 1, 'migration 096'), (N'2nd Term', 2, 'migration 096'), (N'Annual', 3, 'migration 096');
GO
