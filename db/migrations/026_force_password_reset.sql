/*  026_force_password_reset.sql
    ----------------------------
    Adds the flag behind "force a password reset at first login".

    The 756 migrated accounts arrived with an empty PasswordHash and their legacy
    plain-text password still in dbo.Users.Password, so none of them could sign in.
    The companion tool (tools/MigratePasswords) BCrypt-hashes each legacy password,
    raises this flag, and blanks the plain-text column - so a user signs in once
    with the password they already knew, is then required to choose a new one
    before they can use anything, and no plain text is left in the database.

    Idempotent: safe to run more than once.
*/

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'MustChangePassword')
BEGIN
    ALTER TABLE dbo.Users
        ADD MustChangePassword BIT NOT NULL
            CONSTRAINT DF_Users_MustChangePassword DEFAULT (0);

    PRINT 'Added dbo.Users.MustChangePassword.';
END
ELSE
BEGIN
    PRINT 'dbo.Users.MustChangePassword already exists - nothing to do.';
END
GO

/*  Accounts that can never complete a first-login reset on their own, because
    there is nothing to sign in WITH or nothing to sign in AS. They are left with
    the flag clear and no hash: still unable to sign in, but now visible, so an
    administrator can set a password for them from the Users page.  */
SELECT
    id,
    Name,
    Email,
    Roles,
    IsActive,
    CASE
        WHEN Email IS NULL OR LEN(LTRIM(RTRIM(Email))) = 0 THEN 'no email address'
        ELSE 'no legacy password'
    END AS Reason
FROM dbo.Users
WHERE (Email    IS NULL OR LEN(LTRIM(RTRIM(Email)))    = 0)
   OR (Password IS NULL OR LEN(LTRIM(RTRIM(Password))) = 0)
ORDER BY IsActive DESC, id;
GO
