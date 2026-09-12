/*
    031_staff_attendance.sql
    -------------------------------------------------------------------------
    Staff attendance by QR scan, fenced to the centre's own location.

    A screen at the centre shows a QR code. A teacher or receptionist scans it on
    their phone, the browser asks for their location, and the server records the
    scan only if the phone is genuinely inside that branch's fence. First scan of
    the day is the arrival, the next is the departure, and so on, so a lunch break
    is two more scans rather than a special case.

    Hours are NOT stored. They are worked out by pairing In with the Out that
    follows it, because storing a running total means a corrected scan leaves a
    stale figure behind. A day with no scan at all is absent -- that is the absence
    of a row, so nothing has to be written for someone who did not come in.

    This migration only ADDS a table and three columns. Nothing that is running
    today reads them, so it changes no behaviour on the live site.

    Idempotent: safe to run twice.
*/

/* ------------------------------------------------- 1. the branch's fence ---- */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Branch') AND name = 'Latitude')
BEGIN
    ALTER TABLE dbo.Branch ADD Latitude decimal(9,6) NULL;
    PRINT 'Added dbo.Branch.Latitude.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Branch') AND name = 'Longitude')
BEGIN
    ALTER TABLE dbo.Branch ADD Longitude decimal(9,6) NULL;
    PRINT 'Added dbo.Branch.Longitude.';
END
GO

/*
    How far from the point above still counts as "at the centre".

    The default is deliberately generous. The coordinates supplied for Rajpur were
    given to two decimal places (22.41 N, 88.42 E); one hundredth of a degree is
    about 1.1 km of latitude, so the stated point may itself sit up to ~700 m from
    the doorway. A tight fence against an imprecise centre locks out the very staff
    it is meant to admit. Replace both with six-decimal coordinates taken at the
    door and this can come down to 100-150 m.
*/
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Branch') AND name = 'GeofenceMetres')
BEGIN
    ALTER TABLE dbo.Branch ADD GeofenceMetres int NOT NULL CONSTRAINT DF_Branch_GeofenceMetres DEFAULT (750);
    PRINT 'Added dbo.Branch.GeofenceMetres.';
END
GO

UPDATE dbo.Branch
SET    Latitude  = 22.410000,
       Longitude = 88.420000
WHERE  BranchCode = 'Raj'
  AND  Latitude IS NULL;
GO

/* ------------------------------------------------------ 2. the scan log ----- */

IF OBJECT_ID('dbo.StaffAttendance', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StaffAttendance
    (
        Id              int IDENTITY(1,1) NOT NULL,

        /* 'Teacher' or 'Receptionist'. Kept as text beside the id rather than two
           nullable foreign keys: the two staff tables have separate identities and
           a single pair reads far better in a report. */
        StaffType       nvarchar(20)  NOT NULL,
        StaffId         int           NOT NULL,

        /* The sign-in that scanned. Held even though StaffId identifies the person,
           because a staff record can be re-pointed at a different login later and
           an attendance record must say who actually stood there. */
        LoginId         nvarchar(100) NULL,
        StaffName       nvarchar(150) NULL,

        /* The branch whose QR was scanned -- not the branch on the staff record.
           Someone covering a shift elsewhere is present at the centre they are
           standing in, and the branch report must agree with that. */
        BranchId        int           NULL,
        Branch          nvarchar(100) NULL,

        ScannedOn       datetime2(0)  NOT NULL,

        /* The calendar day the scan belongs to, so a day's rows group on an index
           rather than on a function over ScannedOn. */
        OnDate          date          NOT NULL,

        /* 'In' or 'Out', decided by the server from what came before it that day. */
        Direction       nvarchar(3)   NOT NULL,

        Latitude        decimal(9,6)  NULL,
        Longitude       decimal(9,6)  NULL,

        /* What the phone claimed its own accuracy was, and how far it worked out
           to be from the branch. Both recorded so a disputed scan can be examined
           rather than argued about. */
        AccuracyMetres  decimal(9,2)  NULL,
        DistanceMetres  decimal(9,2)  NULL,

        CreatedOn       datetime2(0)  NOT NULL CONSTRAINT DF_StaffAttendance_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_StaffAttendance PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_StaffAttendance_StaffType CHECK (StaffType IN ('Teacher', 'Receptionist')),
        CONSTRAINT CK_StaffAttendance_Direction CHECK (Direction IN ('In', 'Out'))
    );

    PRINT 'Created dbo.StaffAttendance.';
END
ELSE
BEGIN
    PRINT 'dbo.StaffAttendance already exists.';
END
GO

/* One person's day, which is what both the staff screen and the report ask for. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_StaffAttendance_Staff_Date' AND object_id = OBJECT_ID('dbo.StaffAttendance'))
BEGIN
    CREATE INDEX IX_StaffAttendance_Staff_Date
        ON dbo.StaffAttendance (StaffType, StaffId, OnDate) INCLUDE (Direction, ScannedOn);
END
GO

/* A whole centre's day or month, for the administrator's report. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_StaffAttendance_Date_Branch' AND object_id = OBJECT_ID('dbo.StaffAttendance'))
BEGIN
    CREATE INDEX IX_StaffAttendance_Date_Branch
        ON dbo.StaffAttendance (OnDate, Branch) INCLUDE (StaffType, StaffId, Direction, ScannedOn);
END
GO

SELECT branches_with_a_fence = COUNT(*)
FROM   dbo.Branch WHERE Latitude IS NOT NULL AND Longitude IS NOT NULL;

SELECT b.BranchName, b.Latitude, b.Longitude, b.GeofenceMetres FROM dbo.Branch b;
GO
