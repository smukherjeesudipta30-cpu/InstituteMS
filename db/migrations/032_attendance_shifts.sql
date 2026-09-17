/*
    032_attendance_shifts.sql
    -------------------------------------------------------------------------
    Receptionist shifts, and the hour by which each one must have scanned out.

    The front desk is staffed in two sittings:

        Morning   10:00 - 13:00, scan out by 15:00
        Evening   17:00 - 21:00, scan out by 23:00

    Miss that deadline and the day counts as leave, however long the person was
    actually there. It replaces the flat "eight hours after arrival" rule for
    anyone who has a shift: a morning receptionist who forgets at 1pm should not
    keep the day alive until 9pm merely because the arithmetic says so.

    Staff with no shift -- the teachers -- keep the eight-hour rule, since nobody
    has said when a teacher's day is supposed to end.

    The deadline is stored rather than derived as "end + 2 hours", because it is a
    rule the owner set and not a formula; changing one shift's grace should not
    quietly change the other's.

    Only ADDS a table and a column. Nothing running today reads either.
    Idempotent: safe to run twice.
*/

IF OBJECT_ID('dbo.AttendanceShift', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceShift
    (
        Id          int IDENTITY(1,1) NOT NULL,
        Name        nvarchar(50)  NOT NULL,
        StartTime   time(0)       NOT NULL,
        EndTime     time(0)       NOT NULL,

        /* The clock time by which a departure scan must have happened. Later than
           EndTime on purpose -- it is a grace period, not the end of the shift. */
        ScanOutBy   time(0)       NOT NULL,

        IsActive    bit           NOT NULL CONSTRAINT DF_AttendanceShift_IsActive DEFAULT (1),
        CreatedBy   nvarchar(50)  NULL,
        CreatedOn   datetime2(0)  NOT NULL CONSTRAINT DF_AttendanceShift_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  nvarchar(50)  NULL,
        ModifiedOn  datetime2(0)  NULL,

        CONSTRAINT PK_AttendanceShift PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_AttendanceShift_Name UNIQUE (Name)
    );

    PRINT 'Created dbo.AttendanceShift.';
END
ELSE
BEGIN
    PRINT 'dbo.AttendanceShift already exists.';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AttendanceShift WHERE Name = 'Morning')
    INSERT dbo.AttendanceShift (Name, StartTime, EndTime, ScanOutBy, CreatedBy)
    VALUES ('Morning', '10:00', '13:00', '15:00', 'migration-032');
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AttendanceShift WHERE Name = 'Evening')
    INSERT dbo.AttendanceShift (Name, StartTime, EndTime, ScanOutBy, CreatedBy)
    VALUES ('Evening', '17:00', '21:00', '23:00', 'migration-032');
GO

/*
    Which shift a receptionist works. Nullable: somebody on the payroll before the
    rota is decided has no shift yet, and falls back to the flat rule rather than
    being locked out of marking attendance.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'ShiftId')
BEGIN
    ALTER TABLE dbo.Receptionist ADD ShiftId int NULL;
    PRINT 'Added dbo.Receptionist.ShiftId.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Receptionist_AttendanceShift')
BEGIN
    ALTER TABLE dbo.Receptionist
        ADD CONSTRAINT FK_Receptionist_AttendanceShift
        FOREIGN KEY (ShiftId) REFERENCES dbo.AttendanceShift (Id);
    PRINT 'Added FK_Receptionist_AttendanceShift.';
END
GO

SELECT Id, Name, StartTime, EndTime, ScanOutBy, IsActive FROM dbo.AttendanceShift ORDER BY StartTime;
SELECT id, RecName, ShiftId FROM dbo.Receptionist ORDER BY id;
GO
