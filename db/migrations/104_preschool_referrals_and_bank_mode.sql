-- Avijan PreSchool: reference fees for teachers and receptionists, and "Bank" as a way to pay.
--
-- As at the Computer Centre, every new admission records who referred the
-- child: a PreSchool teacher or receptionist, or no one (walk-in). The
-- referrer earns a share (ReferralPercent, 5% to begin with) of every payment
-- made on the child's admission receipt, instalments included. Their credits
-- are added to their next salary; with no reference, all of it stays
-- PreSchool income.
--
-- PreSchoolSetting.ReferralPercent       the share, set on School Settings.
-- PreSchoolStudent.ReferenceType         'Staff' or 'None'; null for children admitted before this.
-- PreSchoolStudent.ReferredByStaffId     the PreSchool teacher or receptionist who referred.
-- PreSchoolReferralCredit                one credit per admission payment, with the
--                                        salary payment that paid it out (null until then).
-- PreSchoolSalaryPayment.ReferralBonus   reference fees paid with that salary (in NetPaid).
-- Receipt and payment Mode               also 'Bank' (bank transfer or deposit).
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.PreSchoolSetting', 'ReferralPercent') IS NULL
    ALTER TABLE dbo.PreSchoolSetting ADD ReferralPercent DECIMAL(5,2) NOT NULL
        CONSTRAINT DF_PreSchoolSetting_ReferralPercent DEFAULT (5);
GO

IF COL_LENGTH('dbo.PreSchoolStudent', 'ReferenceType') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD ReferenceType VARCHAR(10) NULL;
GO
IF COL_LENGTH('dbo.PreSchoolStudent', 'ReferredByStaffId') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD ReferredByStaffId INT NULL
        CONSTRAINT FK_PreSchoolStudent_ReferredBy REFERENCES dbo.PreSchoolStaff (Id);
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolStudent_ReferenceType')
    ALTER TABLE dbo.PreSchoolStudent ADD CONSTRAINT CK_PreSchoolStudent_ReferenceType
        CHECK (ReferenceType IS NULL OR ReferenceType IN ('Staff', 'None'));
GO

IF COL_LENGTH('dbo.PreSchoolSalaryPayment', 'ReferralBonus') IS NULL
    ALTER TABLE dbo.PreSchoolSalaryPayment ADD ReferralBonus DECIMAL(10,2) NOT NULL
        CONSTRAINT DF_PreSchoolSalaryPayment_ReferralBonus DEFAULT (0);
GO

IF OBJECT_ID('dbo.PreSchoolReferralCredit') IS NULL
    CREATE TABLE dbo.PreSchoolReferralCredit (
        Id                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolReferralCredit PRIMARY KEY,
        StudentId         INT            NOT NULL CONSTRAINT FK_PreSchoolReferralCredit_Student REFERENCES dbo.PreSchoolStudent (Id),
        ReceiptPaymentId  INT            NOT NULL CONSTRAINT FK_PreSchoolReferralCredit_Payment
                                                  REFERENCES dbo.PreSchoolReceiptPayment (Id) ON DELETE CASCADE,
        StaffId           INT            NOT NULL CONSTRAINT FK_PreSchoolReferralCredit_Staff REFERENCES dbo.PreSchoolStaff (Id),
        PaymentAmount     DECIMAL(10,2)  NOT NULL,
        [Percent]         DECIMAL(5,2)   NOT NULL,
        Amount            DECIMAL(10,2)  NOT NULL,
        CreditedOn        DATE           NOT NULL,
        SalaryPaymentId   INT            NULL CONSTRAINT FK_PreSchoolReferralCredit_Salary
                                                  REFERENCES dbo.PreSchoolSalaryPayment (Id) ON DELETE SET NULL,
        CreatedBy         NVARCHAR(250)  NULL,
        CreatedOn         DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolReferralCredit_CreatedOn DEFAULT (SYSDATETIME())
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreSchoolReferralCredit_Payment')
    CREATE UNIQUE INDEX UX_PreSchoolReferralCredit_Payment ON dbo.PreSchoolReferralCredit (ReceiptPaymentId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolReferralCredit_Staff')
    CREATE INDEX IX_PreSchoolReferralCredit_Staff ON dbo.PreSchoolReferralCredit (StaffId, SalaryPaymentId);
GO

/* "Bank" as a payment mode, on receipts and on each payment. */
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceipt_Mode' AND definition NOT LIKE '%Bank%')
    ALTER TABLE dbo.PreSchoolReceipt DROP CONSTRAINT CK_PreSchoolReceipt_Mode;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceipt_Mode')
    ALTER TABLE dbo.PreSchoolReceipt ADD CONSTRAINT CK_PreSchoolReceipt_Mode
        CHECK (Mode IN ('Cash', 'Bank', 'UPI', 'Paytm', 'Cheque', 'DD'));
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceiptPayment_Mode' AND definition NOT LIKE '%Bank%')
    ALTER TABLE dbo.PreSchoolReceiptPayment DROP CONSTRAINT CK_PreSchoolReceiptPayment_Mode;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceiptPayment_Mode')
    ALTER TABLE dbo.PreSchoolReceiptPayment ADD CONSTRAINT CK_PreSchoolReceiptPayment_Mode
        CHECK (Mode IN ('Cash', 'Bank', 'UPI', 'Paytm', 'Cheque', 'DD'));
GO
