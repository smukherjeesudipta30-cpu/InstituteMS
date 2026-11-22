-- Avijan PreSchool: the printed Enquiry Form and Fee Receipt.
--
-- PreSchoolEnquiry          gains every field on the school's Enquiry Form:
--                           session, gender, mother / father / guardian with
--                           occupation, phone, email and address, and where the
--                           family heard of the school. ParentName and Phone stay:
--                           they are the person to call back (mother, else father,
--                           else guardian). Source says who filled it in.
-- PreSchoolEnquiryFeedback  the "Enquiry Feedback" page: one dated note per
--                           conversation with the family.
-- PreSchoolStudent.Section  the "Section" on the receipt (A, B …), set by the office.
-- PreSchoolReceipt          gains Cheque and DD as payment modes, with the cheque
--                           date and the bank it is drawn on.
-- PreSchoolReceiptLine      gains Kind 'Charge' with a ChargeHead for the other
--                           rows of the receipt: Registration, Annual, Activities,
--                           LateFee (Part I) and Prospectus, AdmissionKit, Food,
--                           Transport, Others (Part II).
--
-- Adds columns and one table; nothing is removed. Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ---------------------------------------------------------------- enquiry */

IF COL_LENGTH('dbo.PreSchoolEnquiry', 'Session') IS NULL
    ALTER TABLE dbo.PreSchoolEnquiry ADD
        Session             NVARCHAR(9)    NULL,
        Gender              NVARCHAR(10)   NULL,
        MotherName          NVARCHAR(100)  NULL,
        MotherOccupation    NVARCHAR(60)   NULL,
        MotherPhone         NVARCHAR(20)   NULL,
        MotherEmail         NVARCHAR(150)  NULL,
        MotherAddress       NVARCHAR(400)  NULL,
        FatherName          NVARCHAR(100)  NULL,
        FatherOccupation    NVARCHAR(60)   NULL,
        FatherPhone         NVARCHAR(20)   NULL,
        FatherEmail         NVARCHAR(150)  NULL,
        FatherAddress       NVARCHAR(400)  NULL,
        GuardianName        NVARCHAR(100)  NULL,
        GuardianOccupation  NVARCHAR(60)   NULL,
        GuardianPhone       NVARCHAR(20)   NULL,
        GuardianAddress     NVARCHAR(400)  NULL,
        HeardFrom           NVARCHAR(100)  NULL,
        Source              NVARCHAR(10)   NULL,
        CreatedBy           VARCHAR(250)   NULL;
GO

IF OBJECT_ID('dbo.PreSchoolEnquiryFeedback') IS NULL
    CREATE TABLE dbo.PreSchoolEnquiryFeedback (
        Id          INT             IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolEnquiryFeedback PRIMARY KEY,
        EnquiryId   INT             NOT NULL CONSTRAINT FK_PreSchoolEnquiryFeedback_Enquiry
                                        REFERENCES dbo.PreSchoolEnquiry (Id) ON DELETE CASCADE,
        FeedbackOn  DATE            NOT NULL,
        Note        NVARCHAR(1000)  NOT NULL,
        CreatedBy   VARCHAR(250)    NULL,
        CreatedOn   DATETIME2       NOT NULL CONSTRAINT DF_PreSchoolEnquiryFeedback_CreatedOn DEFAULT (SYSDATETIME())
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolEnquiryFeedback_Enquiry')
    CREATE INDEX IX_PreSchoolEnquiryFeedback_Enquiry ON dbo.PreSchoolEnquiryFeedback (EnquiryId, FeedbackOn);
GO

/* ---------------------------------------------------------------- student */

IF COL_LENGTH('dbo.PreSchoolStudent', 'Section') IS NULL
    ALTER TABLE dbo.PreSchoolStudent ADD Section NVARCHAR(10) NULL;
GO

/* ---------------------------------------------------------------- receipt */

IF COL_LENGTH('dbo.PreSchoolReceipt', 'ChequeDate') IS NULL
    ALTER TABLE dbo.PreSchoolReceipt ADD
        ChequeDate  DATE           NULL,
        DrawnOn     NVARCHAR(80)   NULL;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceipt_Mode'
           AND definition NOT LIKE '%Cheque%')
    ALTER TABLE dbo.PreSchoolReceipt DROP CONSTRAINT CK_PreSchoolReceipt_Mode;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceipt_Mode')
    ALTER TABLE dbo.PreSchoolReceipt ADD CONSTRAINT CK_PreSchoolReceipt_Mode
        CHECK (Mode IN ('Cash', 'UPI', 'Paytm', 'Cheque', 'DD'));
GO

IF COL_LENGTH('dbo.PreSchoolReceiptLine', 'ChargeHead') IS NULL
    ALTER TABLE dbo.PreSchoolReceiptLine ADD ChargeHead VARCHAR(20) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceiptLine_Kind'
           AND definition NOT LIKE '%Charge%')
    ALTER TABLE dbo.PreSchoolReceiptLine DROP CONSTRAINT CK_PreSchoolReceiptLine_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceiptLine_Kind')
    ALTER TABLE dbo.PreSchoolReceiptLine ADD CONSTRAINT CK_PreSchoolReceiptLine_Kind
        CHECK (Kind IN ('Admission', 'Monthly', 'Charge'));
GO
