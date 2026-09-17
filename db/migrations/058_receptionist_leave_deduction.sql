-- The Admin's decision on a receptionist's leave for one reporting period
-- (week/month/year) — whether to dock pay, and by how much. Leave itself is
-- never stored (same rule as StaffAttendance/absence): it is computed fresh
-- each time from StaffShiftAssignment (rostered days) minus StaffAttendance
-- (scanned-in days). Only the Admin's decision needs a durable row.
CREATE TABLE dbo.ReceptionistLeaveDeduction (
    id                INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ReceptionistId    INT NOT NULL,
    PeriodType        NVARCHAR(10) NOT NULL,
    PeriodStart       DATE NOT NULL,
    EntitlementDays   DECIMAL(5,2) NOT NULL,
    TakenDays         DECIMAL(5,2) NOT NULL,
    DeductionPercent  DECIMAL(5,2) NOT NULL,
    Applied           BIT NOT NULL,
    Notes             NVARCHAR(300) NULL,
    CreatedBy         NVARCHAR(50) NULL,
    CreatedOn         DATETIME2 NOT NULL,
    ModifiedBy        NVARCHAR(50) NULL,
    ModifiedOn        DATETIME2 NULL,
    CONSTRAINT FK_ReceptionistLeaveDeduction_Receptionist
        FOREIGN KEY (ReceptionistId) REFERENCES dbo.Receptionist(id) ON DELETE CASCADE
);

-- One decision per person per period; re-opening the same period corrects it
-- in place rather than stacking a second row.
CREATE UNIQUE INDEX UQ_ReceptionistLeaveDeduction_Period
    ON dbo.ReceptionistLeaveDeduction(ReceptionistId, PeriodType, PeriodStart);
