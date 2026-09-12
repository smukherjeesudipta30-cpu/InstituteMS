SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    044_payment_mode.sql
    -------------------------------------------------------------------------
    Cash-versus-bank capture at the counter, and a way to know which
    receptionist collected each payment for month-end reconciliation.

    The owner: "Receptionist list will be checked means who is taking money
    when it is cash payment at the time of Registration of students or
    monthly payment time of students also capture payment has been done via
    cash or bank - It will be useful to calculate amount who has how many
    amount at month end - absolutely branch wise."

    MonthlyFee.CreatedBy and Student.CreatedBy already record who took each
    payment, and both tables already carry Branch -- neither needed adding.
    What was missing was Cash-versus-Bank itself, so that is the only new
    column on each table.

    Existing rows are backfilled with a best-effort guess rather than left
    NULL, so a report run over last year's data is not silently empty for
    Cash-versus-Bank even though nobody recorded it at the time:
      - MonthlyFee: a row with TransactionId set was a student's own online
        payment against the UPI QR code, which is definitively Bank. A row
        with no TransactionId was recorded at the counter under the old
        system, where cash was overwhelmingly the norm -- guessed as Cash.
      - Student: admission was always taken in person at the counter before
        this feature existed, so every existing row is guessed as Cash.
    Both guesses are exactly that -- a guess, not a record -- and only rows
    from here on carry a value the office actually chose.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.MonthlyFees') AND name = 'PaymentMode')
BEGIN
    ALTER TABLE dbo.MonthlyFees ADD PaymentMode nvarchar(10) NULL;
    PRINT 'Added MonthlyFees.PaymentMode.';
END
GO

UPDATE dbo.MonthlyFees
SET PaymentMode = CASE WHEN TransactionId IS NOT NULL THEN N'Bank' ELSE N'Cash' END
WHERE PaymentMode IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Student') AND name = 'AdmissionPaymentMode')
BEGIN
    ALTER TABLE dbo.Student ADD AdmissionPaymentMode nvarchar(10) NULL;
    PRINT 'Added Student.AdmissionPaymentMode.';
END
GO

UPDATE dbo.Student
SET AdmissionPaymentMode = N'Cash'
WHERE AdmissionPaymentMode IS NULL AND StuRegId IS NOT NULL;
GO

SELECT
    monthly_fees_backfilled = (SELECT COUNT(*) FROM dbo.MonthlyFees WHERE PaymentMode IS NOT NULL),
    monthly_fees_total      = (SELECT COUNT(*) FROM dbo.MonthlyFees),
    students_backfilled     = (SELECT COUNT(*) FROM dbo.Student WHERE AdmissionPaymentMode IS NOT NULL),
    students_total          = (SELECT COUNT(*) FROM dbo.Student WHERE StuRegId IS NOT NULL);
GO
