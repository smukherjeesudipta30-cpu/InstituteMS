/* =====================================================================
   023_branch_reporting.sql
   ---------------------------------------------------------------------
   Makes an expense attributable to a branch, so the branch report can
   cover money going out as well as money coming in.

   Admissions and fee payments already reach a branch through the
   student (dbo.Student.Branch). An expense reached nothing: the table
   is id / details / date / amount and has never recorded WHERE the
   money was spent. Without this column a branch-wise expense report
   cannot exist at all - not "is empty", but has no column to group by.

   Branch is stored as text matching dbo.Student.Branch rather than a
   foreign key to dbo.Branch, because that is how a student's branch is
   already recorded and two different mechanisms for the same idea would
   be worse than one imperfect one. dbo.Branch.BranchName supplies the
   list to pick from.

   Existing rows stay NULL and report as "Unassigned" - the historic
   expenses genuinely were not attributed and inventing a branch for
   them would be making figures up.

   Run:  sqlcmd -S . -I -i migrations\023_branch_reporting.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

IF COL_LENGTH('dbo.Expense', 'Branch') IS NULL
BEGIN
    ALTER TABLE dbo.Expense ADD Branch varchar(100) NULL;
    PRINT 'Added Expense.Branch.';
END
ELSE
    PRINT 'Expense.Branch already exists.';
GO

/* The report groups by branch inside a date range, for all three
   measures. These are the three scans it makes. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_Expense_ExDate_Branch'
                 AND object_id = OBJECT_ID('dbo.Expense'))
BEGIN
    CREATE INDEX IX_Expense_ExDate_Branch
        ON dbo.Expense (ExDate) INCLUDE (Branch, Amount);

    PRINT 'Created IX_Expense_ExDate_Branch.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_Student_DOJ_Branch'
                 AND object_id = OBJECT_ID('dbo.Student'))
BEGIN
    CREATE INDEX IX_Student_DOJ_Branch
        ON dbo.Student (DOJ) INCLUDE (Branch, StuAdFees, StuRegId);

    PRINT 'Created IX_Student_DOJ_Branch.';
END
GO

PRINT '023 complete.';
GO
