-- Admission referral commission.
--
-- Every new admission names who referred the student (a teacher or a
-- receptionist). A share of each admission-fee payment, at the rate the fee's
-- slab set when the student was admitted, is credited to that person: into a
-- teacher's earnings, or onto a receptionist's next monthly pay. Existing
-- students are untouched (no referrer, no credits).
--
-- Idempotent: safe to run more than once.

-- 1. The slabs: the highest MinFee not above the admission fee decides the
--    percentage; below the lowest slab there is no referral. Admin can change
--    these; the rate is copied onto the student at admission, so a later
--    change never alters students already admitted.
IF OBJECT_ID('dbo.ReferralSlab') IS NULL
BEGIN
    CREATE TABLE dbo.ReferralSlab (
        Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReferralSlab PRIMARY KEY,
        MinFee     DECIMAL(12,2) NOT NULL,
        [Percent]  DECIMAL(5,2)  NOT NULL,
        ModifiedBy VARCHAR(250)  NULL,
        ModifiedOn DATETIME2     NULL
    );

    INSERT INTO dbo.ReferralSlab (MinFee, [Percent], ModifiedBy, ModifiedOn) VALUES
        (750,  7,  'migration 069', SYSDATETIME()),
        (1000, 10, 'migration 069', SYSDATETIME()),
        (1500, 15, 'migration 069', SYSDATETIME());
END
GO

-- 2. Who referred each student, and the rate locked in at admission.
IF COL_LENGTH('dbo.Student', 'ReferrerType') IS NULL
    ALTER TABLE dbo.Student ADD ReferrerType VARCHAR(15) NULL;   -- 'Teacher' | 'Receptionist'
GO
IF COL_LENGTH('dbo.Student', 'ReferrerId') IS NULL
    ALTER TABLE dbo.Student ADD ReferrerId INT NULL;
GO
IF COL_LENGTH('dbo.Student', 'ReferralPercent') IS NULL
    ALTER TABLE dbo.Student ADD ReferralPercent DECIMAL(5,2) NULL;
GO

-- 3. One credit per admission-fee payment collected for a referred student.
IF OBJECT_ID('dbo.ReferralCredit') IS NULL
BEGIN
    CREATE TABLE dbo.ReferralCredit (
        Id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReferralCredit PRIMARY KEY,
        StudentId              INT           NOT NULL,
        RegistrationId         VARCHAR(50)   NULL,
        AdmissionFeePaymentId  INT           NOT NULL,
        ReferrerType           VARCHAR(15)   NOT NULL,
        ReferrerId             INT           NOT NULL,
        Branch                 VARCHAR(100)  NULL,
        PaymentAmount          DECIMAL(12,2) NOT NULL,
        [Percent]              DECIMAL(5,2)  NOT NULL,
        Amount                 DECIMAL(12,2) NOT NULL,
        CreditedOn             DATE          NOT NULL,
        -- Set once a receptionist's credit has been paid on a monthly payment.
        ReceptionistPaymentId  INT           NULL,
        CreatedBy              VARCHAR(250)  NULL,
        CreatedOn              DATETIME2     NOT NULL
    );

    CREATE UNIQUE INDEX UX_ReferralCredit_Payment ON dbo.ReferralCredit (AdmissionFeePaymentId);
    CREATE INDEX IX_ReferralCredit_Referrer ON dbo.ReferralCredit (ReferrerType, ReferrerId);
    CREATE INDEX IX_ReferralCredit_CreditedOn ON dbo.ReferralCredit (CreditedOn);
END
GO

-- 4. The referral share paid on a receptionist's monthly payment, kept apart
--    from the base Amount like ExtraShiftBonus.
IF COL_LENGTH('dbo.ReceptionistPayment', 'ReferralBonus') IS NULL
    ALTER TABLE dbo.ReceptionistPayment ADD ReferralBonus DECIMAL(12,2) NOT NULL
        CONSTRAINT DF_ReceptionistPayment_ReferralBonus DEFAULT (0);
GO
