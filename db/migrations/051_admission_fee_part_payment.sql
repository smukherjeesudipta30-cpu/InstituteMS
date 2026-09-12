SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    051_admission_fee_part_payment.sql
    -------------------------------------------------------------------------
    The admission fee was all-or-nothing: whatever went in Student.StuAdFees
    was taken as received in full, carrying one Cash/Bank tag (migration 044).
    A student paying 300 of a 600 admission fee at the counter and the rest a
    fortnight later had nowhere to be recorded, and the balance was invisible
    to every report.

    Two things here:

      * Student.AdmissionFeesPaid -- how much of the fee has actually come in.
        The balance owed is StuAdFees - AdmissionFeesPaid.

      * dbo.AdmissionFeePayment -- a row per receipt, because the parts are
        separate events: different days, and possibly different ways (cash at
        the desk now, bank transfer later). AdmissionFeesPaid is the running
        total these add up to, kept on the student so the dues report is a
        column read rather than a sum over this table per student.

    THE BACKFILL MATTERS. Every existing student is set AdmissionFeesPaid =
    StuAdFees, because that is what those records actually mean -- the amount
    entered was the amount taken. Leaving them null would invent an unpaid
    admission balance for all 957 of them and put every single one on the dues
    report overnight.
*/

IF COL_LENGTH('dbo.student', 'AdmissionFeesPaid') IS NULL
BEGIN
    ALTER TABLE dbo.student ADD AdmissionFeesPaid decimal(18,2) NULL;
END
GO

/* Separate batch: the column has to exist before it can be written to. */
UPDATE dbo.student
SET AdmissionFeesPaid = StuAdFees
WHERE AdmissionFeesPaid IS NULL;
GO

IF OBJECT_ID('dbo.AdmissionFeePayment', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdmissionFeePayment
    (
        Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdmissionFeePayment PRIMARY KEY,
        StudentId      int              NOT NULL,
        RegistrationId varchar(50)      NULL,
        Amount         decimal(18,2)    NOT NULL,
        PaymentMode    varchar(10)      NULL,
        PaidOn         date             NOT NULL,
        IsAtAdmission  bit              NOT NULL CONSTRAINT DF_AdmissionFeePayment_IsAtAdmission DEFAULT (0),
        Note           varchar(250)     NULL,
        CreatedBy      varchar(100)     NULL,
        CreatedOn      datetime         NOT NULL CONSTRAINT DF_AdmissionFeePayment_CreatedOn DEFAULT (GETDATE())
    );

    CREATE INDEX IX_AdmissionFeePayment_StudentId ON dbo.AdmissionFeePayment (StudentId);
END
GO

/*
    Deliberately NOT backfilled with a receipt row per existing student. Those
    admissions were recorded before receipts existed, so the honest position is
    "paid in full, no itemised receipt" rather than inventing 957 receipts with
    a made-up date and payment mode. The running total above is what reports
    read; this table starts from today's admissions onward.
*/
