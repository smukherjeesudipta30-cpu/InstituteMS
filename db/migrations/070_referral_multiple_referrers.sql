-- Admission referral: more than one referrer per admission.
--
-- A student can now be referred by several teachers and/or receptionists;
-- the referral amount is split equally between them. The people are held in
-- StudentReferrer (one row each). Student.ReferrerType now reads:
--   'None'     - a direct / walk-in admission, nobody credited
--   'Referred' - referred by the people in StudentReferrer
--   NULL       - admitted before referrals existed
-- Student.ReferrerSummary keeps the names as they stood at admission, for display.
--
-- Rows written by 069 (a single ReferrerType/ReferrerId) are moved across.
-- Idempotent: safe to run more than once.

IF OBJECT_ID('dbo.StudentReferrer') IS NULL
BEGIN
    CREATE TABLE dbo.StudentReferrer (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_StudentReferrer PRIMARY KEY,
        StudentId     INT          NOT NULL,
        ReferrerType  VARCHAR(15)  NOT NULL,   -- 'Teacher' | 'Receptionist'
        ReferrerId    INT          NOT NULL
    );

    CREATE UNIQUE INDEX UX_StudentReferrer ON dbo.StudentReferrer (StudentId, ReferrerType, ReferrerId);
END
GO

IF COL_LENGTH('dbo.Student', 'ReferrerSummary') IS NULL
    ALTER TABLE dbo.Student ADD ReferrerSummary NVARCHAR(500) NULL;
GO

-- Move single referrers recorded by 069 into the new table.
INSERT INTO dbo.StudentReferrer (StudentId, ReferrerType, ReferrerId)
SELECT s.id, s.ReferrerType, s.ReferrerId
FROM dbo.Student s
WHERE s.ReferrerType IN ('Teacher', 'Receptionist') AND s.ReferrerId IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM dbo.StudentReferrer r
                  WHERE r.StudentId = s.id AND r.ReferrerType = s.ReferrerType AND r.ReferrerId = s.ReferrerId);
GO

UPDATE s SET
    ReferrerSummary = COALESCE(s.ReferrerSummary,
        CASE s.ReferrerType
            WHEN 'Teacher'      THEN (SELECT t.TecName FROM dbo.Teacher t WHERE t.id = s.ReferrerId) + ' (Teacher)'
            WHEN 'Receptionist' THEN (SELECT r.RecName FROM dbo.Receptionist r WHERE r.id = s.ReferrerId) + ' (Receptionist)'
        END),
    ReferrerType = 'Referred',
    ReferrerId = NULL
FROM dbo.Student s
WHERE s.ReferrerType IN ('Teacher', 'Receptionist');
GO

-- One admission-fee payment now credits each referrer: unique per payment AND person.
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReferralCredit_Payment' AND object_id = OBJECT_ID('dbo.ReferralCredit'))
    DROP INDEX UX_ReferralCredit_Payment ON dbo.ReferralCredit;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ReferralCredit_PaymentReferrer' AND object_id = OBJECT_ID('dbo.ReferralCredit'))
    CREATE UNIQUE INDEX UX_ReferralCredit_PaymentReferrer
        ON dbo.ReferralCredit (AdmissionFeePaymentId, ReferrerType, ReferrerId);
GO
