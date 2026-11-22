-- Avijan PreSchool: what the store buys from My Chhota School, and what it costs.
--
-- PreSchoolStoreItem.UnitCost     the usual price paid per piece; fills in when
--                                 stock comes in.
-- PreSchoolStoreItem.ChargeHead   the Fee Receipt line parents pay this item on
--                                 (Tuition Kit -> AdmissionKit, Winter Garments ->
--                                 WinterGarments, Prospectus -> Prospectus), so the
--                                 income report can set what parents paid against
--                                 what was spent.
-- PreSchoolStoreMovement.UnitCost the price paid per piece for this delivery;
--                                 quantity x unit cost is a purchase in the income
--                                 report on the day the stock came in.
-- PreSchoolStoreMovement.Supplier who it was bought from (My Chhota School).
-- Fee Receipt                      gains the WinterGarments charge, printed on the
--                                 receipt's "Others" line.
--
-- Adds columns only. Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.PreSchoolStoreItem', 'UnitCost') IS NULL
    ALTER TABLE dbo.PreSchoolStoreItem ADD
        UnitCost    DECIMAL(10,2)  NULL,
        ChargeHead  VARCHAR(20)    NULL;
GO

IF COL_LENGTH('dbo.PreSchoolStoreMovement', 'UnitCost') IS NULL
    ALTER TABLE dbo.PreSchoolStoreMovement ADD
        UnitCost    DECIMAL(10,2)  NULL,
        Supplier    NVARCHAR(100)  NULL;
GO

UPDATE dbo.PreSchoolStoreItem SET ChargeHead = 'AdmissionKit'   WHERE ChargeHead IS NULL AND Name IN (N'Tuition Kit', N'School Kit');
UPDATE dbo.PreSchoolStoreItem SET ChargeHead = 'WinterGarments' WHERE ChargeHead IS NULL AND Name = N'Winter Garments';
UPDATE dbo.PreSchoolStoreItem SET ChargeHead = 'Prospectus'     WHERE ChargeHead IS NULL AND Name = N'Prospectus';
GO

-- The usual amounts per class may now include Winter Garments.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolChargeRate_Head' AND definition NOT LIKE '%WinterGarments%')
    ALTER TABLE dbo.PreSchoolChargeRate DROP CONSTRAINT CK_PreSchoolChargeRate_Head;
GO
IF OBJECT_ID('dbo.PreSchoolChargeRate') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PreSchoolChargeRate_Head')
    ALTER TABLE dbo.PreSchoolChargeRate ADD CONSTRAINT CK_PreSchoolChargeRate_Head CHECK (Head IN
        ('Registration', 'Annual', 'Activities', 'LateFee', 'Prospectus', 'AdmissionKit', 'Food', 'Transport', 'WinterGarments', 'Others'));
GO
