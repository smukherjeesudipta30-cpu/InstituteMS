-- Avijan PreSchool: paying My Chhota School in bulk.
--
-- Stock received (PreSchoolStoreMovement, Kind 'In', with its UnitCost) is what
-- the PreSchool owes My Chhota School. PreSchoolSupplierPayment is what it has
-- paid: one entry per payment, covering any number of deliveries. The income
-- report counts the payments, on the day paid; the Store screen shows bought,
-- paid and the balance still due.
--
-- Adds one table. Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolSupplierPayment') IS NULL
    CREATE TABLE dbo.PreSchoolSupplierPayment (
        Id          INT             IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolSupplierPayment PRIMARY KEY,
        Supplier    NVARCHAR(100)   NOT NULL CONSTRAINT DF_PreSchoolSupplierPayment_Supplier DEFAULT (N'My Chhota School'),
        PaidOn      DATE            NOT NULL,
        Amount      DECIMAL(12,2)   NOT NULL,
        Mode        NVARCHAR(10)    NOT NULL,
        Reference   NVARCHAR(60)    NULL,
        Note        NVARCHAR(300)   NULL,
        CreatedBy   VARCHAR(250)    NULL,
        CreatedOn   DATETIME2       NOT NULL CONSTRAINT DF_PreSchoolSupplierPayment_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT CK_PreSchoolSupplierPayment_Amount CHECK (Amount > 0),
        CONSTRAINT CK_PreSchoolSupplierPayment_Mode CHECK (Mode IN ('Cash', 'Bank', 'UPI', 'Cheque'))
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolSupplierPayment_PaidOn')
    CREATE INDEX IX_PreSchoolSupplierPayment_PaidOn ON dbo.PreSchoolSupplierPayment (PaidOn);
GO
