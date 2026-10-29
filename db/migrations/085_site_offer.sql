-- The admission offer on the public front page (the "30% OFF" corner ribbon).
--
-- SiteOffer: one row (Id 1) — the discount percentage, the day it starts
-- showing and for how many days. Admin and Receptionist set it from the
-- "Admission Offer" screen; outside those days the ribbon is not shown.
--
-- Seeded with the 30% the page used to hard-code, for 30 days from today, so
-- the front page looks the same until someone changes it.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.SiteOffer') IS NULL
    CREATE TABLE dbo.SiteOffer (
        Id          INT          NOT NULL CONSTRAINT PK_SiteOffer PRIMARY KEY,
        Percentage  INT          NOT NULL,
        StartsOn    DATE         NOT NULL,
        Days        INT          NOT NULL,
        ModifiedBy  VARCHAR(250) NULL,
        ModifiedOn  DATETIME2    NULL,
        CONSTRAINT CK_SiteOffer_OneRow CHECK (Id = 1),
        CONSTRAINT CK_SiteOffer_Percentage CHECK (Percentage BETWEEN 0 AND 100),
        CONSTRAINT CK_SiteOffer_Days CHECK (Days BETWEEN 1 AND 366)
    );
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SiteOffer WHERE Id = 1)
    INSERT INTO dbo.SiteOffer (Id, Percentage, StartsOn, Days, ModifiedBy, ModifiedOn)
    VALUES (1, 30, CAST(GETDATE() AS DATE), 30, 'migration 085', SYSDATETIME());
GO
