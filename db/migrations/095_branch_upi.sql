-- Avijan Computer Centre: each branch can take fees into its own UPI account.
--
-- Branch.UpiId, Branch.UpiPayeeName  the UPI ID and the name the payer's app
--                                    shows, for the QR on a student's Pay My
--                                    Fees page. Empty means the institute's
--                                    usual account (Payments:UpiId in
--                                    appsettings.json), so Rajpur is unchanged.
--
-- Champahati takes its fees into the Bank of Maharashtra account on the QR the
-- owner gave: bom261001361082@mahb, payee AVIJAN. Set here for a Champahati
-- branch that already exists; appsettings.json (Payments:Branches) carries the
-- same, so it also applies the moment the branch is created.
--
-- Adds two columns. Nothing is removed and no other branch is changed.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.Branch', 'UpiId') IS NULL
    ALTER TABLE dbo.Branch ADD UpiId NVARCHAR(100) NULL;
GO
IF COL_LENGTH('dbo.Branch', 'UpiPayeeName') IS NULL
    ALTER TABLE dbo.Branch ADD UpiPayeeName NVARCHAR(100) NULL;
GO

UPDATE dbo.Branch
SET UpiId = N'bom261001361082@mahb', UpiPayeeName = N'AVIJAN'
WHERE (BranchName LIKE N'%Champahati%' OR Branch_name LIKE N'%Champahati%')
  AND (UpiId IS NULL OR UpiId = N'');
GO
