SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    050_competition_draft_forgot_password.sql
    -------------------------------------------------------------------------
    "Forgot password" for a competition draft, same shape as 049 gave every
    staff/student login. dbo.CompetitionDraft had no email at all -- only a
    phone number -- so there was nowhere to send a reset code. This adds
    Email (nullable: a draft started before this exists still loads, it
    simply cannot use the emailed-code recovery until one is added by some
    other route) plus the same three reset-code columns 049 added to Users.
*/

IF COL_LENGTH('dbo.CompetitionDraft', 'Email') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDraft ADD Email varchar(50) NULL;
END
GO

IF COL_LENGTH('dbo.CompetitionDraft', 'PasswordResetCodeHash') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDraft ADD PasswordResetCodeHash varchar(255) NULL;
END
GO

IF COL_LENGTH('dbo.CompetitionDraft', 'PasswordResetCodeExpiry') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDraft ADD PasswordResetCodeExpiry datetime NULL;
END
GO

IF COL_LENGTH('dbo.CompetitionDraft', 'PasswordResetAttempts') IS NULL
BEGIN
    ALTER TABLE dbo.CompetitionDraft ADD PasswordResetAttempts int NOT NULL CONSTRAINT DF_CompetitionDraft_PasswordResetAttempts DEFAULT (0);
END
GO
