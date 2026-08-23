/* =====================================================================
   010_monthlyfees_actual_fee.sql
   Target DB : Avijan_Angular_Migration
   Purpose   : Restore dbo.MonthlyFees.stu_actual_monthly_fees, which 007/008
               missed.

   What it is
   ----------
   The student's agreed monthly fee *at the time of that payment*. The legacy
   usp_SaveData took it as @stu_actual_monthly_fees and stored it on the
   ledger row. It matters because Student.Stu_monthly_fees holds only the
   student's CURRENT fee, so it cannot be used to interpret a historic row
   after a fee change.

   It is also the evidence for how MonthlyFees.Installment is composed:

       Installment  =  agreed monthly fee  +  late fine

   e.g. bill S-M-8508-25 -- Installment 352, actual fee 270, fine 82.

   That is why the income report treats late fines as an "of which"
   breakdown of fee collections rather than as separate income; adding them
   again would overstate income.

   Only 9 of 3,397 legacy rows carry a value (the column was added late), but
   it is real data and the new fee-entry screen writes it from now on.

   Idempotent: safe to re-run.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

IF COL_LENGTH('dbo.MonthlyFees', 'stu_actual_monthly_fees') IS NULL
    ALTER TABLE dbo.MonthlyFees ADD stu_actual_monthly_fees decimal(18,2) NULL;
GO

IF DB_ID('ph19987796653_') IS NULL
BEGIN
    PRINT 'Source database not attached - column added, nothing backfilled.';
    RETURN;
END
GO

/* Report anything that will not convert, before touching the data. */
DECLARE @unconvertible int =
(
    SELECT COUNT(*)
    FROM   ph19987796653_prod.dbo.monthlyFees
    WHERE  stu_actual_monthly_fees IS NOT NULL
      AND  LTRIM(RTRIM(stu_actual_monthly_fees)) <> ''
      AND  TRY_CONVERT(decimal(18,2), stu_actual_monthly_fees) IS NULL
);

IF @unconvertible > 0
    PRINT '  ** ' + CAST(@unconvertible AS varchar(10)) + ' value(s) are not numeric and were left NULL.';

UPDATE  n
SET     n.stu_actual_monthly_fees = TRY_CONVERT(decimal(18,2), l.stu_actual_monthly_fees)
FROM    dbo.MonthlyFees n
JOIN    ph19987796653_prod.dbo.monthlyFees l ON l.id = n.id
WHERE   l.stu_actual_monthly_fees IS NOT NULL
  AND   LTRIM(RTRIM(l.stu_actual_monthly_fees)) <> '';
GO

PRINT '010_monthlyfees_actual_fee.sql applied.';

SELECT 'rows with an agreed-fee value = ' + CAST(COUNT(*) AS varchar(10))
FROM   dbo.MonthlyFees
WHERE  stu_actual_monthly_fees IS NOT NULL;
GO
