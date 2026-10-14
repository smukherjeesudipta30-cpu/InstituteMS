-- A staff member's photograph (Teacher, Receptionist, Editor…), taken with the
-- camera or uploaded on the User form, one row per user. Printed on their
-- birthday wish card. Kept apart from dbo.Users so listing users never drags
-- the pictures along.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.UserPhoto') IS NULL
    CREATE TABLE dbo.UserPhoto (
        UserId      INT           NOT NULL CONSTRAINT PK_UserPhoto PRIMARY KEY,
        ContentType VARCHAR(20)   NOT NULL,
        PhotoBase64 NVARCHAR(MAX) NOT NULL,
        ModifiedBy  VARCHAR(250)  NULL,
        ModifiedOn  DATETIME2     NULL
    );
GO
