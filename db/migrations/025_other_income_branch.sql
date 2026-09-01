/* =====================================================================
   025_other_income_branch.sql
   ---------------------------------------------------------------------
   Gives non-fee income a branch, so it can be reported branch-wise like
   everything else.

   dbo.OIncome was ID / IPurpose / IAmount / ODate and nothing more, so a
   branch-scoped income report had to leave it out entirely rather than
   charge institute-wide income to one branch. That is why "Other income"
   reads zero the moment a branch is selected.

   Existing rows are backfilled to the institute's only branch, matching
   what 024 did for students and expenses. As there is one branch, those
   records ARE that branch's income.

   Run:  sqlcmd -S . -I -i migrations\025_other_income_branch.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

IF COL_LENGTH('dbo.OIncome', 'Branch') IS NULL
BEGIN
    ALTER TABLE dbo.OIncome ADD Branch varchar(100) NULL;
    PRINT 'Added OIncome.Branch.';
END
ELSE
    PRINT 'OIncome.Branch already exists.';
GO

DECLARE @branch varchar(100) = (SELECT TOP 1 BranchName FROM dbo.Branch WHERE IsActive = 1 ORDER BY id);

IF @branch IS NULL
    PRINT 'No active branch on record - backfill skipped.';
ELSE
BEGIN
    DECLARE @rows int;

    UPDATE dbo.OIncome
       SET Branch = @branch
     WHERE LEN(ISNULL(LTRIM(RTRIM(Branch)), '')) = 0;

    SET @rows = @@ROWCOUNT;

    PRINT CONCAT('Backfilled ', @rows, ' other-income row(s) to "', @branch, '".');
END
GO

/* The income report groups by branch inside a date range. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_OIncome_ODate_Branch' AND object_id = OBJECT_ID('dbo.OIncome'))
BEGIN
    CREATE INDEX IX_OIncome_ODate_Branch
        ON dbo.OIncome (ODate) INCLUDE (Branch, IAmount);

    PRINT 'Created IX_OIncome_ODate_Branch.';
END
GO

PRINT '025 complete.';
GO
