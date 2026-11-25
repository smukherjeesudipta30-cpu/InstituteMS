-- Avijan PreSchool: discounts on a fee receipt.
--
-- A receipt may take a percentage off its total (e.g. 10% on admission) and/or
-- a special offer of a fixed amount. Each is kept as its own receipt line of
-- Kind 'Discount' with a negative amount (ChargeHead 'Discount' or
-- 'SpecialOffer'), so the receipt prints them, its total is what was actually
-- received, and the income report shows the discounts given.
--
-- Widens one check constraint. Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceiptLine_Kind'
           AND definition NOT LIKE '%Discount%')
    ALTER TABLE dbo.PreSchoolReceiptLine DROP CONSTRAINT CK_PreSchoolReceiptLine_Kind;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolReceiptLine_Kind')
    ALTER TABLE dbo.PreSchoolReceiptLine ADD CONSTRAINT CK_PreSchoolReceiptLine_Kind
        CHECK (Kind IN ('Admission', 'Monthly', 'Charge', 'Discount'));
GO
