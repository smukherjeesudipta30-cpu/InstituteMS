-- Exam fees.
--
-- Until now an exam fee could only be typed as free text under Other income
-- ("Exam Fee (Rajani Ghosh)CIIT"), with no link to the student or course.
-- These tables make it a first-class fee:
--   ExamFeeRate    - Admin's fee per course and branch (the default charged)
--   Student.ExamFeeOverride - a different amount for one student, if set
--   ExamFeePayment - each exam fee collected, linked to student and course
-- The fee is charged once per course. The institute keeps all of it: no
-- teacher share, no referral commission. Past free-text entries stay in
-- Other income untouched.
--
-- Idempotent: safe to run more than once.

IF OBJECT_ID('dbo.ExamFeeRate') IS NULL
BEGIN
    CREATE TABLE dbo.ExamFeeRate (
        Id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamFeeRate PRIMARY KEY,
        CourseId   INT            NOT NULL,
        Branch     VARCHAR(100)   NOT NULL,
        Amount     DECIMAL(12,2)  NOT NULL,
        ModifiedBy VARCHAR(250)   NULL,
        ModifiedOn DATETIME2      NULL
    );

    CREATE UNIQUE INDEX UX_ExamFeeRate_Course_Branch ON dbo.ExamFeeRate (CourseId, Branch);
END
GO

IF COL_LENGTH('dbo.Student', 'ExamFeeOverride') IS NULL
    ALTER TABLE dbo.Student ADD ExamFeeOverride DECIMAL(12,2) NULL;
GO

IF OBJECT_ID('dbo.ExamFeePayment') IS NULL
BEGIN
    CREATE TABLE dbo.ExamFeePayment (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamFeePayment PRIMARY KEY,
        StudentId      INT            NOT NULL,
        RegistrationId VARCHAR(50)    NULL,
        CourseId       INT            NULL,
        Branch         VARCHAR(100)   NULL,
        Amount         DECIMAL(12,2)  NOT NULL,
        PaymentMode    VARCHAR(10)    NOT NULL,   -- 'Cash' | 'Bank'
        PaidOn         DATE           NOT NULL,
        Note           NVARCHAR(300)  NULL,
        CreatedBy      VARCHAR(250)   NULL,
        CreatedOn      DATETIME2      NOT NULL
    );

    CREATE INDEX IX_ExamFeePayment_Student ON dbo.ExamFeePayment (StudentId);
    CREATE INDEX IX_ExamFeePayment_PaidOn ON dbo.ExamFeePayment (PaidOn);
END
GO
