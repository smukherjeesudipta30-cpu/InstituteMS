/* =====================================================================
   019_exams_uncapped_terms.sql
   ---------------------------------------------------------------------
   Removes the "two terms only" cap. A student may be examined on a
   course as many times as the institute chooses.

   What REMAINS is the pairing rule, because the result is read as
   "1 theory + 1 practical": within one course and one term a student has
   at most one theory paper and at most one practical paper. Terms
   themselves are now unlimited, so term 3, 4, 5 ... are all valid.

   017 added CHECK (TermNumber IN (1,2)) on ExamPaper. That is dropped.
   The unique index from 018 stays: it is what makes each term a clean
   theory/practical pair rather than a pile of attempts.

   Run:  sqlcmd -S . -I -i migrations\019_exams_uncapped_terms.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ExamPaper_TermNumber')
BEGIN
    ALTER TABLE dbo.ExamPaper DROP CONSTRAINT CK_ExamPaper_TermNumber;
    PRINT '019: two-term cap dropped.';
END
GO

/* A term still has to be a positive number - term 0 or -1 is a typo. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ExamPaper_TermPositive')
BEGIN
    ALTER TABLE dbo.ExamPaper
        ADD CONSTRAINT CK_ExamPaper_TermPositive CHECK (TermNumber >= 1);

    PRINT '019: CK_ExamPaper_TermPositive added.';
END
GO

PRINT '019_exams_uncapped_terms.sql applied.';
PRINT '  one theory + one practical per course, per term - terms unlimited.';
GO
