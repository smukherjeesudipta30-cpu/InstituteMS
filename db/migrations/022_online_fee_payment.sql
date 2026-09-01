/* =====================================================================
   022_online_fee_payment.sql
   ---------------------------------------------------------------------
   Students paying their monthly fee from the portal, against the
   institute's shared UPI QR code.

   The QR is a STATIC merchant code. There is no gateway callback, so
   nothing tells this system that money actually arrived - the student
   types the UPI reference from their own app and the payment is
   recorded on that. Three columns exist to make that reviewable:

     TransactionId   the UPI reference the student typed.
                     NULL on an offline payment taken at the counter,
                     which is how the two are told apart.

     VerifiedOn      when someone checked the reference against the
     VerifiedBy      Paytm account and confirmed the money is there.
                     NULL means "recorded, not yet checked" - which is
                     what the dashboard notification lists.

   A payment is written to MonthlyFees straight away, so the student's
   ledger and the teacher payout are correct the moment they pay. The
   verification columns are what let the office confirm it afterwards,
   and what make an unconfirmed payment findable if it has to be
   reversed. Without them an unverified payment would be
   indistinguishable from a counted one.

   Run:  sqlcmd -S . -I -i migrations\022_online_fee_payment.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

IF COL_LENGTH('dbo.MonthlyFees', 'TransactionId') IS NULL
BEGIN
    ALTER TABLE dbo.MonthlyFees ADD TransactionId varchar(64) NULL;
    PRINT 'Added MonthlyFees.TransactionId.';
END
ELSE
    PRINT 'MonthlyFees.TransactionId already exists.';
GO

IF COL_LENGTH('dbo.MonthlyFees', 'VerifiedOn') IS NULL
BEGIN
    ALTER TABLE dbo.MonthlyFees ADD VerifiedOn datetime2(0) NULL;
    PRINT 'Added MonthlyFees.VerifiedOn.';
END
GO

IF COL_LENGTH('dbo.MonthlyFees', 'VerifiedBy') IS NULL
BEGIN
    ALTER TABLE dbo.MonthlyFees ADD VerifiedBy varchar(50) NULL;
    PRINT 'Added MonthlyFees.VerifiedBy.';
END
GO

/* The dashboard's only question: which online payments are still
   unchecked? Filtered so the index stays small however many offline
   payments the ledger accumulates. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_MonthlyFees_UnverifiedOnline'
                 AND object_id = OBJECT_ID('dbo.MonthlyFees'))
BEGIN
    CREATE INDEX IX_MonthlyFees_UnverifiedOnline
        ON dbo.MonthlyFees (CreatedOn DESC)
        INCLUDE (StuRegId, BillNo, Installment, TransactionId)
        WHERE TransactionId IS NOT NULL AND VerifiedOn IS NULL;

    PRINT 'Created IX_MonthlyFees_UnverifiedOnline.';
END
GO

/* Two payments can never share a UPI reference: the same reference
   arriving twice is a double submission, not two payments. Filtered so
   the offline rows, which are all NULL, are not constrained. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UQ_MonthlyFees_TransactionId'
                 AND object_id = OBJECT_ID('dbo.MonthlyFees'))
BEGIN
    CREATE UNIQUE INDEX UQ_MonthlyFees_TransactionId
        ON dbo.MonthlyFees (TransactionId)
        WHERE TransactionId IS NOT NULL;

    PRINT 'Created UQ_MonthlyFees_TransactionId.';
END
GO

PRINT '022 complete.';
GO
