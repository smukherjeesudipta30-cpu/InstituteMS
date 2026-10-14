-- The Proprietor's birthday: name, date of birth and phone, one row (Id 1),
-- entered by Admin on the Birthdays page. The Proprietor has no login account,
-- so there is no Users row to read it from. Their birthday is announced to
-- Admin, SuperAdmin and Editor.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.ProprietorProfile') IS NULL
    CREATE TABLE dbo.ProprietorProfile (
        Id          INT           NOT NULL CONSTRAINT PK_ProprietorProfile PRIMARY KEY,
        Name        NVARCHAR(100) NOT NULL,
        DateOfBirth DATE          NULL,
        Phone       VARCHAR(20)   NULL,
        ModifiedBy  VARCHAR(250)  NULL,
        ModifiedOn  DATETIME2     NULL,
        CONSTRAINT CK_ProprietorProfile_OneRow CHECK (Id = 1)
    );
GO
