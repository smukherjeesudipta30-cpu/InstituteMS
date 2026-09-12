/*
    033_staff_shift_roster.sql
    -------------------------------------------------------------------------
    A rota, not a single shift per person.

    Migration 032 hung one ShiftId off dbo.Receptionist, which cannot express the
    rota the owner actually keeps:

        Chumki Halder    Mon-Sat   evening
        Sandheeta Dhar   Mon-Fri   morning
        Ishani           Sat       morning
                         Sun       morning AND evening

    Two things break the one-column model. A person works different shifts on
    different days, and on Sunday Ishani works both of them. So the assignment moves
    into its own table, keyed by weekday, with more than one row per day allowed.

    Which shift a given day's arrival belongs to is then decided by the clock: the
    latest shift that had already started when they scanned in, falling back to the
    day's first shift if they arrived early. That is what fixes the scan-out
    deadline -- 15:00 for a morning, 23:00 for an evening.

    Somebody with no rota entry for that weekday can still scan; they fall back to
    the flat eight-hour rule rather than being locked out for covering a shift.

    Idempotent: safe to run twice.
*/

/* --------------------------------------- 1. retire the one-shift column ----- */

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Receptionist_AttendanceShift')
BEGIN
    ALTER TABLE dbo.Receptionist DROP CONSTRAINT FK_Receptionist_AttendanceShift;
    PRINT 'Dropped FK_Receptionist_AttendanceShift.';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'ShiftId')
BEGIN
    ALTER TABLE dbo.Receptionist DROP COLUMN ShiftId;
    PRINT 'Dropped dbo.Receptionist.ShiftId (replaced by the rota).';
END
GO

/* ------------------------------------------------------- 2. the rota -------- */

IF OBJECT_ID('dbo.StaffShiftAssignment', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffShiftAssignment
    (
        Id          int IDENTITY(1,1) NOT NULL,

        StaffType   nvarchar(20) NOT NULL,
        StaffId     int          NOT NULL,

        /* 0 = Sunday .. 6 = Saturday, matching System.DayOfWeek so no translation
           is needed anywhere in the application. */
        DayOfWeek   tinyint      NOT NULL,

        ShiftId     int          NOT NULL,

        IsActive    bit          NOT NULL CONSTRAINT DF_StaffShiftAssignment_IsActive DEFAULT (1),
        CreatedBy   nvarchar(50) NULL,
        CreatedOn   datetime2(0) NOT NULL CONSTRAINT DF_StaffShiftAssignment_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  nvarchar(50) NULL,
        ModifiedOn  datetime2(0) NULL,

        CONSTRAINT PK_StaffShiftAssignment PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_StaffShiftAssignment_StaffType CHECK (StaffType IN ('Teacher', 'Receptionist')),
        CONSTRAINT CK_StaffShiftAssignment_DayOfWeek CHECK (DayOfWeek BETWEEN 0 AND 6),
        CONSTRAINT FK_StaffShiftAssignment_Shift FOREIGN KEY (ShiftId) REFERENCES dbo.AttendanceShift (Id),

        /* The same person cannot be put on the same shift twice on the same day,
           but CAN be on two different shifts -- which is Ishani's Sunday. */
        CONSTRAINT UQ_StaffShiftAssignment UNIQUE (StaffType, StaffId, DayOfWeek, ShiftId)
    );

    PRINT 'Created dbo.StaffShiftAssignment.';
END
ELSE
BEGIN
    PRINT 'dbo.StaffShiftAssignment already exists.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_StaffShiftAssignment_Staff' AND object_id = OBJECT_ID('dbo.StaffShiftAssignment'))
BEGIN
    CREATE INDEX IX_StaffShiftAssignment_Staff
        ON dbo.StaffShiftAssignment (StaffType, StaffId, DayOfWeek) INCLUDE (ShiftId, IsActive);
END
GO

/* ------------------------------- 3. the rota as the owner gave it ----------- */
/*
    Entered here so the feature can be tested before the admin screen exists. Once
    that screen is in, this is edited there and never again in SQL.

    "5PM to PM" was given without a closing hour; it is read as the Evening shift
    already defined in migration 032 -- 17:00 to 21:00, scan out by 23:00.
*/

DECLARE @morning int = (SELECT Id FROM dbo.AttendanceShift WHERE Name = 'Morning');
DECLARE @evening int = (SELECT Id FROM dbo.AttendanceShift WHERE Name = 'Evening');

DECLARE @chumki    int = (SELECT id FROM dbo.Receptionist WHERE RecName LIKE 'CHUMKI%');
DECLARE @sandheeta int = (SELECT id FROM dbo.Receptionist WHERE RecName LIKE 'SANDHEETA%');
DECLARE @ishani    int = (SELECT id FROM dbo.Receptionist WHERE RecName LIKE 'ISHANI%');

DECLARE @rota TABLE (StaffId int, DayOfWeek tinyint, ShiftId int);

-- Chumki Halder: Monday to Saturday, evening.
INSERT @rota (StaffId, DayOfWeek, ShiftId)
SELECT @chumki, d, @evening FROM (VALUES (1),(2),(3),(4),(5),(6)) AS x(d);

-- Sandheeta Dhar: Monday to Friday, morning.
INSERT @rota (StaffId, DayOfWeek, ShiftId)
SELECT @sandheeta, d, @morning FROM (VALUES (1),(2),(3),(4),(5)) AS x(d);

-- Ishani: Saturday morning; Sunday morning AND evening.
INSERT @rota (StaffId, DayOfWeek, ShiftId) VALUES
    (@ishani, 6, @morning),
    (@ishani, 0, @morning),
    (@ishani, 0, @evening);

INSERT dbo.StaffShiftAssignment (StaffType, StaffId, DayOfWeek, ShiftId, CreatedBy)
SELECT 'Receptionist', r.StaffId, r.DayOfWeek, r.ShiftId, 'migration-033'
FROM   @rota r
WHERE  r.StaffId IS NOT NULL
  AND  NOT EXISTS (
        SELECT 1 FROM dbo.StaffShiftAssignment a
        WHERE a.StaffType = 'Receptionist' AND a.StaffId = r.StaffId
          AND a.DayOfWeek = r.DayOfWeek AND a.ShiftId = r.ShiftId);
GO

SELECT r.RecName,
       day_of_week = CASE a.DayOfWeek WHEN 0 THEN 'Sun' WHEN 1 THEN 'Mon' WHEN 2 THEN 'Tue'
                                      WHEN 3 THEN 'Wed' WHEN 4 THEN 'Thu' WHEN 5 THEN 'Fri' ELSE 'Sat' END,
       s.Name, s.StartTime, s.EndTime, s.ScanOutBy
FROM   dbo.StaffShiftAssignment a
JOIN   dbo.Receptionist r ON r.id = a.StaffId AND a.StaffType = 'Receptionist'
JOIN   dbo.AttendanceShift s ON s.Id = a.ShiftId
ORDER  BY r.RecName, a.DayOfWeek, s.StartTime;
GO
