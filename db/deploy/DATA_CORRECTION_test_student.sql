/* =====================================================================
   DATA_CORRECTION_test_student.sql
   ---------------------------------------------------------------------
   Zeroes the admission fee on one test record that is distorting every
   report covering August 2025.

   THE RECORD
   ---------------------------------------------------------------------
       StuRegId   AVI/3738/25
       Name       TTTT
       Guardian   TTTTT
       Phone      8989898989
       DOJ        31-08-2025
       StuAdFees  204,546.00      <-- the problem
       Monthly    200

   Every field says test data. The admission fee is roughly 400 times the
   real average of 532 rupees, and on its own it accounted for 204,546 of
   the 228,146 rupees sitting in the "enrolled but never paid a fee"
   group - which is what made that group look alarming rather than
   ordinary. Without it, the remaining 47 students in that group average
   502 rupees, in line with everybody else.

   Verified to be referenced by NOTHING before writing this:

       dbo.monthlyFees   0 rows
       dbo.Fees          0 rows
       dbo.LateFees      0 rows
       dbo.TecFees       0 rows
       dbo.Marks         0 rows

   WHY ZERO THE FEE RATHER THAN DELETE THE ROW
   ---------------------------------------------------------------------
   Deleting is tempting and slightly worse. The row is harmless once the
   number is gone, the registration id stays consumed so it can never be
   reissued to a real student, and nothing that might reference it later
   breaks. Zeroing fixes the reports; deleting fixes the reports and
   throws away a fact.

   WHY IT MATTERS NOW
   ---------------------------------------------------------------------
   The branch report has never filtered on student status, so it counts
   this row today in any range covering August 2025. The income report
   used to hide it behind an IsActive filter - a filter that has now been
   removed, because it was also hiding 354,800 rupees of genuine income
   from students who completed or left. With that filter gone, this row
   would start inflating the income report too.

   Run:  sqlcmd -S <server> -d <database> -I -b -i DATA_CORRECTION_test_student.sql
   Safe to re-run: the update is a no-op once the fee is already zero.
   ===================================================================== */

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

PRINT '--- before ---';
GO

SELECT StuRegId, Stuname, DOJ, StuAdFees, IsActive, IsComplete
FROM dbo.student
WHERE StuRegId = 'AVI/3738/25';
GO

/* Guarded on the name as well as the id, so this can never fire against a
   real student who happens to have been given that registration id. */
IF EXISTS (SELECT 1 FROM dbo.student
           WHERE StuRegId = 'AVI/3738/25' AND Stuname = 'TTTT' AND StuAdFees <> 0)
BEGIN
    DECLARE @was decimal(18,2) =
        (SELECT StuAdFees FROM dbo.student WHERE StuRegId = 'AVI/3738/25');

    UPDATE dbo.student
       SET StuAdFees = 0
     WHERE StuRegId = 'AVI/3738/25' AND Stuname = 'TTTT';

    PRINT CONCAT('Admission fee on AVI/3738/25 (TTTT) cleared: was ', @was, ', now 0.');
END
ELSE
    PRINT 'Nothing to do - AVI/3738/25 is absent, renamed, or already zero.';
GO

PRINT '';
PRINT '--- after ---';
GO

SELECT StuRegId, Stuname, DOJ, StuAdFees, IsActive, IsComplete
FROM dbo.student
WHERE StuRegId = 'AVI/3738/25';
GO

PRINT '';
PRINT '--- the group this record was distorting ---';
GO

SELECT Bucket  = 'inactive, never completed, never paid a fee',
       Students = COUNT(*),
       AdmFees  = SUM(ISNULL(s.StuAdFees, 0)),
       AvgFee   = CAST(AVG(ISNULL(s.StuAdFees, 0)) AS int)
FROM dbo.student s
WHERE ISNULL(s.IsActive, 0) <> 1
  AND ISNULL(s.IsComplete, 0) <> 1
  AND NOT EXISTS (SELECT 1 FROM dbo.monthlyFees f WHERE f.sturegid = s.StuRegId);
GO

PRINT 'DATA_CORRECTION_test_student.sql complete.';
GO
