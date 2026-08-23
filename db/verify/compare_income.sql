/* =====================================================================
   compare_income.sql
   Old-vs-new check for the income report (MIGRATION_PLAN §11.3).
   Read-only. Runs the ORIGINAL legacy SQL alongside the new definitions
   so every figure on the report can be traced back.

   Set the range here.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [Avijan_Angular_Migration];
GO

DECLARE @From date = '2025-01-01';
DECLARE @To   date = '2025-12-31';

PRINT '=== range ===';
PRINT '  from ' + CONVERT(varchar(10), @From, 120) + '  to ' + CONVERT(varchar(10), @To, 120);
PRINT '';

/* ---------------------------------------------------------------------
   1. The legacy income roll-up, verbatim from Models/Income.cs:39.
      Note UNION (not UNION ALL) -- this is the bug.
   --------------------------------------------------------------------- */
DECLARE @legacyUnion decimal(18,2) =
(
    SELECT SUM(x.TotalIncome) FROM
    (
        SELECT SUM(A.Total) AS TotalIncome, StuReg, StoreDate
        FROM (
            SELECT A.Installment AS Total, A.StuRegId AS StuReg, A.Date1 AS StoreDate FROM MonthlyFees AS A
            UNION
            SELECT A.StuAdFees  AS Total, A.StuRegId AS StuReg, A.DOJ   AS StoreDate FROM Student A WHERE A.IsActive = 1
        ) AS A
        WHERE A.StoreDate BETWEEN @From AND @To
        GROUP BY A.StuReg, A.StoreDate
    ) x
);

/* Same thing with UNION ALL -- what the new report uses. */
DECLARE @fixedUnion decimal(18,2) =
(
    SELECT SUM(x.TotalIncome) FROM
    (
        SELECT SUM(A.Total) AS TotalIncome, StuReg, StoreDate
        FROM (
            SELECT A.Installment AS Total, A.StuRegId AS StuReg, A.Date1 AS StoreDate FROM MonthlyFees AS A
            UNION ALL
            SELECT A.StuAdFees  AS Total, A.StuRegId AS StuReg, A.DOJ   AS StoreDate FROM Student A WHERE A.IsActive = 1
        ) AS A
        WHERE A.StoreDate BETWEEN @From AND @To
        GROUP BY A.StuReg, A.StoreDate
    ) x
);

PRINT '=== the UNION bug (Models/Income.cs:39) ===';
PRINT '  legacy UNION      : ' + ISNULL(CAST(@legacyUnion AS varchar(30)), '0');
PRINT '  fixed  UNION ALL  : ' + ISNULL(CAST(@fixedUnion  AS varchar(30)), '0');
PRINT '  understated by    : ' + ISNULL(CAST(@fixedUnion - @legacyUnion AS varchar(30)), '0');
PRINT '';

/* ---------------------------------------------------------------------
   2. The component totals the new report returns.
   --------------------------------------------------------------------- */
DECLARE @fees      decimal(18,2) = (SELECT ISNULL(SUM(Installment), 0) FROM MonthlyFees WHERE Date1  BETWEEN @From AND @To);
DECLARE @admission decimal(18,2) = (SELECT ISNULL(SUM(StuAdFees),   0) FROM Student     WHERE IsActive = 1 AND DOJ BETWEEN @From AND @To);
DECLARE @other     decimal(18,2) = (SELECT ISNULL(SUM(IAmount),     0) FROM OIncome     WHERE ODate  BETWEEN @From AND @To);
DECLARE @fines     decimal(18,2) = (SELECT ISNULL(SUM(FineFees),    0) FROM LateFees    WHERE DOD    BETWEEN @From AND @To);
DECLARE @expense   decimal(18,2) = (SELECT ISNULL(SUM(Amount),      0) FROM Expense     WHERE ExDate BETWEEN @From AND @To);
DECLARE @payout    decimal(18,2) = (SELECT ISNULL(SUM(TecSal),      0) FROM TecFees     WHERE UpdateDate BETWEEN @From AND @To);
DECLARE @tecExp    decimal(18,2) = (SELECT ISNULL(SUM(Amount),      0) FROM TecExpense  WHERE InDate BETWEEN @From AND @To);
DECLARE @dues      decimal(18,2) = (SELECT ISNULL(SUM(ISNULL(NewDue,0) - ISNULL(OldDue,0)), 0) FROM MonthlyFees WHERE Date1 BETWEEN @From AND @To);

PRINT '=== income ===';
PRINT '  fee collections        : ' + CAST(@fees      AS varchar(30));
PRINT '  admission fees         : ' + CAST(@admission AS varchar(30));
PRINT '  other income           : ' + CAST(@other     AS varchar(30));
PRINT '  TOTAL INCOME           : ' + CAST(@fees + @admission + @other AS varchar(30));
PRINT '    of which late fines  : ' + CAST(@fines AS varchar(30)) + '   (already inside fee collections)';
PRINT '';
PRINT '=== cost ===';
PRINT '  institute expenses     : ' + CAST(@expense AS varchar(30));
PRINT '  teacher payouts        : ' + CAST(@payout  AS varchar(30));
PRINT '  teacher expenses       : ' + CAST(@tecExp  AS varchar(30));
PRINT '  TOTAL COST             : ' + CAST(@expense + @payout + @tecExp AS varchar(30));
PRINT '';
PRINT '=== result ===';
PRINT '  NET                    : ' + CAST((@fees + @admission + @other) - (@expense + @payout + @tecExp) AS varchar(30));
PRINT '  dues movement          : ' + CAST(@dues AS varchar(30));
PRINT '';

/* ---------------------------------------------------------------------
   3. Proof that the fine is already inside the fee figure.

      Checked against MonthlyFees.stu_actual_monthly_fees -- the agreed fee
      recorded ON the payment row. Student.Stu_monthly_fees cannot be used:
      it holds the student's CURRENT fee, so every row written before a fee
      change looks like a mismatch when it is not.
   --------------------------------------------------------------------- */
/*  usp_SaveData stored @Install, which Scripts/MonthlyCourse.js computed as
    TInstall = IAmount + LateFees. So:

        Installment = (whatever installment the operator entered) + fine

    The operator's figure is usually the student's agreed fee, but not always
    -- arrears, partial payments and fine-only bills all appear in the data --
    and it is not recorded separately. The verifiable claim is therefore that
    the fine is CONTAINED IN Installment, never additional to it.            */
PRINT '=== double-count check: is the fine contained in Installment? ===';

SELECT  'fine <= Installment (fine is inside it)' AS Result,
        COUNT(*)                                  AS Rows_
FROM    MonthlyFees m
JOIN    LateFees    l ON l.BillNo = m.BillNo
WHERE   ISNULL(l.FineFees, 0) > 0
  AND   ISNULL(l.FineFees, 0) <= ISNULL(m.Installment, 0)
UNION ALL
SELECT  '** fine > Installment (would break the rule)',
        COUNT(*)
FROM    MonthlyFees m
JOIN    LateFees    l ON l.BillNo = m.BillNo
WHERE   ISNULL(l.FineFees, 0) > 0
  AND   ISNULL(l.FineFees, 0) > ISNULL(m.Installment, 0);

PRINT '';
PRINT 'sample rows (AgreedFee is the standing fee, not necessarily what was charged):';

SELECT  TOP 10
        m.BillNo,
        AgreedFee   = m.stu_actual_monthly_fees,
        Fine        = ISNULL(l.FineFees, 0),
        Expected    = m.stu_actual_monthly_fees + ISNULL(l.FineFees, 0),
        Installment = m.Installment
FROM    MonthlyFees m
JOIN    LateFees    l ON l.BillNo = m.BillNo
WHERE   m.stu_actual_monthly_fees IS NOT NULL
ORDER BY m.id DESC;
GO
