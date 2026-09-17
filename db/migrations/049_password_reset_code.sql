SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    049_password_reset_code.sql
    -------------------------------------------------------------------------
    "Forgot password" for every role. dbo.Users gets three new nullable
    columns: a hashed six-digit code (never stored in clear -- the same
    BCrypt hasher as PasswordHash), when it expires, and how many wrong
    codes have been tried against it. All three are cleared the moment the
    code is spent or a fresh one is requested, so a stale row here never
    outlives the reset it belongs to.
*/

IF COL_LENGTH('dbo.Users', 'PasswordResetCodeHash') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD PasswordResetCodeHash varchar(255) NULL;
END
GO

IF COL_LENGTH('dbo.Users', 'PasswordResetCodeExpiry') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD PasswordResetCodeExpiry datetime NULL;
END
GO

IF COL_LENGTH('dbo.Users', 'PasswordResetAttempts') IS NULL
BEGIN
    ALTER TABLE dbo.Users ADD PasswordResetAttempts int NOT NULL CONSTRAINT DF_Users_PasswordResetAttempts DEFAULT (0);
END
GO
