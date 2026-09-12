SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    040_competition_cancellation.sql
    -------------------------------------------------------------------------
    The office can cancel a registration and refund it in full.

    The owner:

        "After successful registration also if admin wants admin can cancel
         registration if criteria not matching properly, candidate will get refund
         all amount in that case for dance and drawing"

    So a fifth payment status, plus a reason -- a cancellation without a written
    reason is an argument waiting to happen at the desk, and the person cancelling
    is not always the person who has to explain it later.

    Two consequences that are easy to miss and both matter on the day:

      * A cancelled dancer must give their place back, or a group stays "full" at
        fourteen real entrants.
      * Their chest number must be freed, or the numbers run 1..14, 16 and the
        printed pile no longer matches the register.

    The freeing is done in the application (CompetitionService.CancelAsync), which
    nulls the chest number; new entrants then take the LOWEST free number rather
    than MAX + 1, so a group always reads 1..n with no gaps.

    Only ADDS a column and widens a CHECK. Idempotent.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.CompetitionApplicant') AND name = 'CancelReason')
BEGIN
    ALTER TABLE dbo.CompetitionApplicant ADD CancelReason nvarchar(300) NULL;
    PRINT 'Added dbo.CompetitionApplicant.CancelReason.';
END
GO

/* 'Cancelled' joins the allowed statuses. The constraint has to be replaced
   rather than altered -- SQL Server has no ALTER CHECK. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CompetitionApplicant_Status')
BEGIN
    ALTER TABLE dbo.CompetitionApplicant DROP CONSTRAINT CK_CompetitionApplicant_Status;
    PRINT 'Dropped the old status constraint.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_CompetitionApplicant_Status')
BEGIN
    ALTER TABLE dbo.CompetitionApplicant
        ADD CONSTRAINT CK_CompetitionApplicant_Status
            CHECK (PaymentStatus IN ('Pending', 'Confirmed', 'AtVenue', 'Rejected', 'Cancelled'));

    PRINT 'Added the status constraint, now including Cancelled.';
END
GO

SELECT allowed_statuses = 'Pending, Confirmed, AtVenue, Rejected, Cancelled';
GO
