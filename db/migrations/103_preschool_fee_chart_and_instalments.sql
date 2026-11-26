-- Avijan PreSchool: the 2027–28 fee chart, and paying the admission package in instalments.
--
-- 1. Three more Fee Receipt heads from the fee chart: Development Charges,
--    Session Charges and Additional Educational Materials. The printed receipt
--    is unchanged: they print in its "Others" row.
-- 2. Each class's usual amount for every receipt head, as on the fee chart
--    (both columns are the same apart from the first month's tuition, which is
--    the class's monthly fee, set on Classes & Fees).
-- 3. PreSchoolReceiptPayment: the money actually received against a receipt,
--    one row per payment (instalment 1 to 4), with the login that collected
--    it. Every existing receipt gets one payment for its whole total, on its
--    date, by whoever wrote it, so old receipts read exactly as before.
--
-- Idempotent: safe to run more than once. Step 2 sets the chart's amounts
-- again if re-run, so run it once (as part of 103) and change amounts on
-- Classes & Fees afterwards.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* 1. ---------------------------------------------------------- heads */

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolChargeRate_Head'
           AND definition NOT LIKE '%Materials%')
    ALTER TABLE dbo.PreSchoolChargeRate DROP CONSTRAINT CK_PreSchoolChargeRate_Head;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolChargeRate_Head')
    ALTER TABLE dbo.PreSchoolChargeRate ADD CONSTRAINT CK_PreSchoolChargeRate_Head CHECK (Head IN
        ('Registration', 'Annual', 'Activities', 'LateFee', 'Prospectus', 'AdmissionKit', 'Food', 'Transport',
         'WinterGarments', 'Development', 'Session', 'Materials', 'Others'));
GO

/* 2. ------------------------------------------------- fee chart rates */

MERGE dbo.PreSchoolChargeRate AS t
USING (
    SELECT c.Id AS ClassId, r.Head, r.Amount
    FROM dbo.PreSchoolClass c
    CROSS JOIN (VALUES
        ('Registration',    100.00),
        ('Annual',          350.00),
        ('Development',     200.00),
        ('Activities',      200.00),
        ('Session',         201.00),
        ('AdmissionKit',   2499.00),
        ('Materials',       400.00),
        ('WinterGarments',  500.00),
        ('Prospectus',      150.00)
    ) AS r (Head, Amount)
    WHERE c.IsActive = 1
) AS s
ON t.ClassId = s.ClassId AND t.Head = s.Head
WHEN MATCHED AND t.Amount <> s.Amount THEN
    UPDATE SET Amount = s.Amount, ModifiedBy = 'migration 103', ModifiedOn = SYSDATETIME()
WHEN NOT MATCHED THEN
    INSERT (ClassId, Head, Amount, ModifiedBy) VALUES (s.ClassId, s.Head, s.Amount, 'migration 103');
GO

/* 3. --------------------------------------------------- instalments */

IF OBJECT_ID('dbo.PreSchoolReceiptPayment') IS NULL
    CREATE TABLE dbo.PreSchoolReceiptPayment (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolReceiptPayment PRIMARY KEY,
        ReceiptId     INT             NOT NULL CONSTRAINT FK_PreSchoolReceiptPayment_Receipt
                                               REFERENCES dbo.PreSchoolReceipt (Id) ON DELETE CASCADE,
        InstalmentNo  INT             NOT NULL,
        PaidOn        DATE            NOT NULL,
        Amount        DECIMAL(10,2)   NOT NULL,
        Mode          NVARCHAR(10)    NOT NULL,
        Reference     NVARCHAR(60)    NULL,
        Notes         NVARCHAR(300)   NULL,
        CollectedBy   NVARCHAR(250)   NULL,
        CreatedOn     DATETIME2       NOT NULL CONSTRAINT DF_PreSchoolReceiptPayment_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT CK_PreSchoolReceiptPayment_No CHECK (InstalmentNo BETWEEN 1 AND 4),
        CONSTRAINT CK_PreSchoolReceiptPayment_Amount CHECK (Amount >= 0),
        CONSTRAINT CK_PreSchoolReceiptPayment_Mode CHECK (Mode IN ('Cash', 'UPI', 'Paytm', 'Cheque', 'DD'))
    );
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PreSchoolReceiptPayment_Receipt_No')
    CREATE UNIQUE INDEX UX_PreSchoolReceiptPayment_Receipt_No ON dbo.PreSchoolReceiptPayment (ReceiptId, InstalmentNo);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolReceiptPayment_PaidOn')
    CREATE INDEX IX_PreSchoolReceiptPayment_PaidOn ON dbo.PreSchoolReceiptPayment (PaidOn);
GO

-- Every receipt written before instalments: paid in full, on its date, by whoever wrote it.
INSERT INTO dbo.PreSchoolReceiptPayment (ReceiptId, InstalmentNo, PaidOn, Amount, Mode, Reference, CollectedBy, CreatedOn)
SELECT r.Id, 1, r.ReceivedOn, r.Total, r.Mode, r.Reference, r.CreatedBy, r.CreatedOn
FROM dbo.PreSchoolReceipt r
WHERE NOT EXISTS (SELECT 1 FROM dbo.PreSchoolReceiptPayment p WHERE p.ReceiptId = r.Id);
GO
