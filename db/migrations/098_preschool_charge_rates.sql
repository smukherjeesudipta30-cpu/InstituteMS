-- Avijan PreSchool: the usual amount of each Fee Receipt row, per class.
--
-- PreSchoolChargeRate  one class's amount for one of the receipt's other rows
--                      (Registration, Annual, Activities, LateFee, Prospectus,
--                      AdmissionKit, Food, Transport, Others). Set by the
--                      PreSchool admin or receptionist on Classes & Fees; the
--                      fee collection screen fills these in, and the office can
--                      still change the amount on any one receipt.
--
-- Adds one table. Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolChargeRate') IS NULL
    CREATE TABLE dbo.PreSchoolChargeRate (
        ClassId     INT             NOT NULL CONSTRAINT FK_PreSchoolChargeRate_Class REFERENCES dbo.PreSchoolClass (Id),
        Head        VARCHAR(20)     NOT NULL,
        Amount      DECIMAL(10,2)   NOT NULL,
        ModifiedBy  VARCHAR(250)    NULL,
        ModifiedOn  DATETIME2       NOT NULL CONSTRAINT DF_PreSchoolChargeRate_ModifiedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT PK_PreSchoolChargeRate PRIMARY KEY (ClassId, Head),
        CONSTRAINT CK_PreSchoolChargeRate_Amount CHECK (Amount >= 0),
        CONSTRAINT CK_PreSchoolChargeRate_Head CHECK (Head IN
            ('Registration', 'Annual', 'Activities', 'LateFee', 'Prospectus', 'AdmissionKit', 'Food', 'Transport', 'Others'))
    );
GO
