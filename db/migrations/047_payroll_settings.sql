SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    047_payroll_settings.sql
    -------------------------------------------------------------------------
    Whether the missed-scan-out deduction actually applies is the admin's own
    call, changeable without asking anyone to touch code or redeploy. The
    owner: "if admin wants then deduction will be happened and if admin
    doesn't want then deduction will not be happened, always it should
    depends on Admin."

    A single-row settings table, read fresh on every report -- not cached in
    appsettings.json, which only an engineer can edit. Off by default: a rule
    that can silently start taking money off a payslip should be switched on
    by a deliberate choice, not by deploying this migration.
*/

IF OBJECT_ID('dbo.PayrollSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PayrollSettings
    (
        Id                            int IDENTITY(1,1) NOT NULL,
        MissedScanOutDeductionEnabled bit NOT NULL CONSTRAINT DF_PayrollSettings_Enabled DEFAULT (0),
        ModifiedBy                    nvarchar(50) NULL,
        ModifiedOn                    datetime2 NULL,

        CONSTRAINT PK_PayrollSettings PRIMARY KEY (Id)
    );

    INSERT INTO dbo.PayrollSettings (MissedScanOutDeductionEnabled) VALUES (0);

    PRINT 'Created dbo.PayrollSettings, deduction off by default.';
END
GO
