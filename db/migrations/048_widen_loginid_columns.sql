SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    048_widen_loginid_columns.sql
    -------------------------------------------------------------------------
    dbo.student.loginId and dbo.Users.Loginid were both varchar(20). This
    column does not hold a "login id" the row itself uses to sign in -- it
    records who (which staff member) created or last touched the row, the
    same role CreatedBy plays. Any receptionist or admin whose own email
    login is longer than 20 characters made every new admission fail with a
    hard SQL truncation error the moment their login was written in:

        String or binary data would be truncated in table 'dbo.student',
        column 'loginId'.

    Reproduced live on 9 September 2026 against a real Receptionist login;
    the production log shows the same failure occurring three times earlier
    that same morning for the actual receptionist account. Widened to 100
    to match the width already used for Receptionist.LoginId and
    StaffAttendance.LoginId.
*/

IF COL_LENGTH('dbo.student', 'loginId') IS NOT NULL
    AND (SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'student' AND COLUMN_NAME = 'loginId') < 100
BEGIN
    ALTER TABLE dbo.student ALTER COLUMN loginId varchar(100) NULL;
END
GO

IF COL_LENGTH('dbo.Users', 'Loginid') IS NOT NULL
    AND (SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
         WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Users' AND COLUMN_NAME = 'Loginid') < 100
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN Loginid varchar(100) NULL;
END
GO
