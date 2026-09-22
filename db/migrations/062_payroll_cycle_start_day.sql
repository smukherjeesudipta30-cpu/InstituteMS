-- The day of the month a receptionist's pay cycle starts on. The institute
-- actually runs the 26th to the 25th of the next month, not the calendar
-- month, so this defaults to 26 rather than 1 — an Admin can move it without
-- a deploy (see ReportsController PUT payroll-settings).
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.PayrollSettings') AND name = 'PayCycleStartDay'
)
    ALTER TABLE dbo.PayrollSettings
        ADD PayCycleStartDay INT NOT NULL
            CONSTRAINT DF_PayrollSettings_PayCycleStartDay DEFAULT 26;
