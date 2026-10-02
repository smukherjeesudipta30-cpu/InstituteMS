-- Monthly stock and book-debt statements for the bank (Bank of Maharashtra,
-- Appendix 6), entered by Admin from the Bank Statement page. One row per
-- month; the stock and book-debt lines are stored as JSON.
--
-- Idempotent: safe to run more than once.

IF OBJECT_ID('dbo.BankStatement') IS NULL
    CREATE TABLE dbo.BankStatement (
        MonthStart DATE          NOT NULL CONSTRAINT PK_BankStatement PRIMARY KEY,
        DataJson   NVARCHAR(MAX) NOT NULL,
        ModifiedBy VARCHAR(250)  NULL,
        ModifiedOn DATETIME2     NOT NULL CONSTRAINT DF_BankStatement_ModifiedOn DEFAULT SYSDATETIME()
    );
GO
