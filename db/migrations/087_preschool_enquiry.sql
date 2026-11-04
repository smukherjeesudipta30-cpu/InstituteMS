-- Avijan PreSchool, phase 2: admission enquiries from the public page at
-- /preschool.
--
-- PreSchoolEnquiry  one row per enquiry: the child, the parent's contact
--                   details, the class asked about, and whether the PreSchool
--                   office has called back.
--
-- Adds a table only. The Computer Centre's CourseEnquiry is not touched, so
-- neither office sees the other's enquiries.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolEnquiry') IS NULL
    CREATE TABLE dbo.PreSchoolEnquiry (
        Id                INT             IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolEnquiry PRIMARY KEY,
        ChildName         NVARCHAR(100)   NOT NULL,
        ChildDateOfBirth  DATE            NULL,
        ParentName        NVARCHAR(100)   NOT NULL,
        Phone             NVARCHAR(20)    NOT NULL,
        Email             NVARCHAR(150)   NULL,
        ClassWanted       NVARCHAR(50)    NULL,
        Message           NVARCHAR(1000)  NULL,
        ClientIp          VARCHAR(50)     NULL,
        CreatedOn         DATETIME2       NOT NULL CONSTRAINT DF_PreSchoolEnquiry_CreatedOn DEFAULT (SYSDATETIME()),
        IsHandled         BIT             NOT NULL CONSTRAINT DF_PreSchoolEnquiry_IsHandled DEFAULT (0),
        HandledBy         VARCHAR(250)    NULL,
        HandledOn         DATETIME2       NULL
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolEnquiry_CreatedOn' AND object_id = OBJECT_ID('dbo.PreSchoolEnquiry'))
    CREATE INDEX IX_PreSchoolEnquiry_CreatedOn ON dbo.PreSchoolEnquiry (CreatedOn);
GO
