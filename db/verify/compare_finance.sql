/* =====================================================================
   compare_finance.sql
   Old-vs-new data diff for Users, the fee ledger and the finance tables
   (MIGRATION_PLAN §11.3). Read-only. Every check should report OK.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [Avijan_Angular_Migration];
GO

PRINT '=== row counts ===';

SELECT  'Users'       AS [Table],
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Users)  AS Legacy,
        /* One legacy user has a blank email, stored as NULL; the IS NULL branch keeps
           it counted, because "NULL <> 'x'" is NULL and would silently drop it. */
        (SELECT COUNT(*) FROM dbo.Users
          WHERE Email IS NULL OR Email <> 'admin@avijan.local') AS Migrated
UNION ALL SELECT 'MonthlyFees', (SELECT COUNT(*) FROM ph19987796653_prod.dbo.monthlyFees), (SELECT COUNT(*) FROM dbo.MonthlyFees)
UNION ALL SELECT 'Expense',     (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Expense),     (SELECT COUNT(*) FROM dbo.Expense)
UNION ALL SELECT 'OIncome',     (SELECT COUNT(*) FROM ph19987796653_prod.dbo.OIncome),     (SELECT COUNT(*) FROM dbo.OIncome)
UNION ALL SELECT 'Notice',      (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Notice),      (SELECT COUNT(*) FROM dbo.Notice)
UNION ALL SELECT 'LateFees',    (SELECT COUNT(*) FROM ph19987796653_prod.dbo.LateFees),    (SELECT COUNT(*) FROM dbo.LateFees)
UNION ALL SELECT 'TecFees',     (SELECT COUNT(*) FROM ph19987796653_prod.dbo.TecFees),     (SELECT COUNT(*) FROM dbo.TecFees)
UNION ALL SELECT 'TecExpense',  (SELECT COUNT(*) FROM ph19987796653_prod.dbo.TecExpense),  (SELECT COUNT(*) FROM dbo.TecExpense)
UNION ALL SELECT 'Marks',       (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Marks),       (SELECT COUNT(*) FROM dbo.Marks)
UNION ALL SELECT 'Branch',      (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Branch),      (SELECT COUNT(*) FROM dbo.Branch)
UNION ALL SELECT 'Fees',        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Fees),        (SELECT COUNT(*) FROM dbo.Fees);

PRINT '';
PRINT '=== money totals (must match to the paisa) ===';

SELECT 'Fee installments' AS Measure,
       CASE WHEN (SELECT ISNULL(SUM(Installment), 0) FROM ph19987796653_prod.dbo.monthlyFees)
               = (SELECT ISNULL(SUM(Installment), 0) FROM dbo.MonthlyFees) THEN 'OK' ELSE '** MISMATCH **' END AS Result
UNION ALL
SELECT 'Old / new dues',
       CASE WHEN (SELECT ISNULL(SUM(OldDue), 0) + ISNULL(SUM(NewDue), 0) FROM ph19987796653_prod.dbo.monthlyFees)
               = (SELECT ISNULL(SUM(OldDue), 0) + ISNULL(SUM(NewDue), 0) FROM dbo.MonthlyFees) THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Institute expenses',
       CASE WHEN (SELECT ISNULL(SUM(Amount), 0) FROM ph19987796653_prod.dbo.Expense)
               = (SELECT ISNULL(SUM(Amount), 0) FROM dbo.Expense) THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Late fines',
       CASE WHEN (SELECT ISNULL(SUM(FineFees), 0) FROM ph19987796653_prod.dbo.LateFees)
               = (SELECT ISNULL(SUM(FineFees), 0) FROM dbo.LateFees) THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Teacher payouts',
       CASE WHEN (SELECT ISNULL(SUM(TecSal), 0) FROM ph19987796653_prod.dbo.TecFees)
               = (SELECT ISNULL(SUM(TecSal), 0) FROM dbo.TecFees) THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Fee bill numbers identical',
       CASE WHEN (SELECT COUNT(*)
                    FROM ph19987796653_prod.dbo.monthlyFees l
                    JOIN dbo.MonthlyFees n ON n.id = l.id
                   WHERE ISNULL(LTRIM(RTRIM(l.BillNo)), '') <> ISNULL(n.BillNo, '')) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Fee dates identical',
       CASE WHEN (SELECT COUNT(*)
                    FROM ph19987796653_prod.dbo.monthlyFees l
                    JOIN dbo.MonthlyFees n ON n.id = l.id
                   WHERE ISNULL(TRY_CONVERT(date, l.Date1), '1900-01-01') <> ISNULL(n.Date1, '1900-01-01')) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'No orphan TecFees teacher links',
       CASE WHEN (SELECT COUNT(*) FROM dbo.TecFees t
                   WHERE t.TecId IS NOT NULL
                     AND NOT EXISTS (SELECT 1 FROM dbo.Teacher x WHERE x.id = t.TecId)) = 0
            THEN 'OK' ELSE '** MISMATCH **' END;

PRINT '';
PRINT '=== Users security ===';

SELECT 'Every migrated user has a password hash' AS Check_,
       CASE WHEN (SELECT COUNT(*) FROM dbo.Users WHERE PasswordHash IS NULL) = 0
            THEN 'OK' ELSE '** MISSING HASHES **' END AS Result
UNION ALL
SELECT 'All hashes are BCrypt',
       CASE WHEN (SELECT COUNT(*) FROM dbo.Users
                   WHERE PasswordHash IS NOT NULL AND PasswordHash NOT LIKE '$2%') = 0
            THEN 'OK' ELSE '** NOT BCRYPT **' END
UNION ALL
SELECT 'No plain-text password column exists',
       CASE WHEN COL_LENGTH('dbo.Users', 'Password') IS NULL
            THEN 'OK' ELSE '** PLAIN-TEXT COLUMN PRESENT **' END
UNION ALL
SELECT 'Role values all recognised',
       CASE WHEN (SELECT COUNT(*) FROM dbo.Users
                   WHERE Roles IS NOT NULL
                     AND Roles NOT IN ('Admin','Editor','Student','Teacher','Receptionist','Receptionist & Teacher')) = 0
            THEN 'OK' ELSE '** UNKNOWN ROLE **' END;

PRINT '';
PRINT 'role breakdown:';
SELECT ISNULL(Roles, '(none)') AS Role, COUNT(*) AS Accounts
FROM   dbo.Users GROUP BY Roles ORDER BY COUNT(*) DESC;

PRINT '';
PRINT '=== accounts sharing an email (login picks the newest active) ===';
SELECT u.Email, COUNT(*) AS Accounts, MAX(u.id) AS WinningId
FROM   dbo.Users u
WHERE  u.Email IS NOT NULL
GROUP BY u.Email
HAVING COUNT(*) > 1
ORDER BY u.Email;
GO
