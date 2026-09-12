/*
    030_monthlyfees_branch_stamp.sql
    -------------------------------------------------------------------------
    Records which branch took each monthly fee.

    Until now a payment carried no branch at all. The branch report worked out
    where the money belonged by looking up the student's CURRENT branch every time
    the report ran. A student can never pay "to the wrong branch" that way -- there
    is no field to get wrong -- but it has a quieter fault:

        move one student from Rajpur to another branch, and every fee they have
        EVER paid moves with them. Last quarter's branch performance changes
        retrospectively, and nobody can see why.

    Stamping the branch at the moment the money is taken fixes that. It is the same
    reason dbo.monthlyFees already carries stu_actual_monthly_fees: the agreed fee
    is copied onto the row so a historic payment stays interpretable after the
    student's current figures change.

    The report still falls back to the student's branch when the stamp is null, so
    nothing breaks for rows written by an older build.

    Idempotent: safe to run twice.
*/

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.monthlyFees') AND name = 'Branch')
BEGIN
    ALTER TABLE dbo.monthlyFees ADD Branch nvarchar(100) NULL;
    PRINT 'Added dbo.monthlyFees.Branch.';
END
ELSE
BEGIN
    PRINT 'dbo.monthlyFees.Branch already exists.';
END
GO

/*
    Backfill from the student's branch as it stands now. That is the best evidence
    available for payments already taken, and it reproduces exactly what the report
    was calculating before this change -- so no historical figure moves today.

    Registration ids are matched case-insensitively and the lowest student id wins,
    the same rule the reports use: the migrated data holds a few duplicated ids.
*/
UPDATE f
SET    f.Branch = s.Branch
FROM   dbo.monthlyFees f
CROSS APPLY (
    SELECT TOP 1 st.Branch
    FROM   dbo.student st
    WHERE  st.StuRegId = f.sturegid
    ORDER  BY st.id
) s
WHERE  f.Branch IS NULL
  AND  NULLIF(LTRIM(RTRIM(s.Branch)), '') IS NOT NULL;
GO

SELECT stamped   = COUNT(*) FROM dbo.monthlyFees WHERE Branch IS NOT NULL;
SELECT unstamped = COUNT(*) FROM dbo.monthlyFees WHERE Branch IS NULL;
GO
