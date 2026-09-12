SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    043_attendance_session_gap_repair.sql
    -------------------------------------------------------------------------
    Recomputes the In/Out label on existing StaffAttendance rows. No row is
    inserted or deleted -- only the Direction column is corrected, and only where
    it disagrees with what the corrected rule would have produced.

    The bug: direction used to alternate In, Out, In, Out for the whole day,
    no matter how long the gap between scans. A teacher who scanned in for the
    morning and forgot to scan out at lunch had her evening return recorded as
    "Out" (the toggle's turn), and her actual evening departure recorded as
    "In" -- every direction from the missed scan onward read backwards. The
    owner reported this against Nargis's own attendance.

    AttendanceService.cs now treats a gap of SessionGapHours (4, configurable)
    or more since the previous scan as a fresh arrival outright, whatever the
    previous scan's direction was. This repairs the rows already written under
    the old rule so today's report reads correctly for attendance already taken,
    not only for scans from here on.

    RUN WITH -f 65001 not required (ASCII only), but harmless either way.
*/

DECLARE @GapHours INT = 4;

;WITH Ordered AS (
    SELECT
        Id,
        StaffType,
        StaffId,
        OnDate,
        ScannedOn,
        Direction AS OldDirection,
        LAG(ScannedOn) OVER (
            PARTITION BY StaffType, StaffId, OnDate
            ORDER BY ScannedOn, Id
        ) AS PrevScannedOn
    FROM dbo.StaffAttendance
),
Recomputed AS (
    SELECT
        Id,
        OldDirection,
        PrevScannedOn,
        -- Row number within the day resets the alternation after any gap of
        -- GapHours or more, exactly as AttendanceService.cs now decides it live.
        CASE
            WHEN PrevScannedOn IS NULL THEN 1
            WHEN DATEDIFF(MINUTE, PrevScannedOn, ScannedOn) >= @GapHours * 60 THEN 1
            ELSE 0
        END AS StartsNewSession
    FROM Ordered
),
Numbered AS (
    SELECT
        r.Id,
        r.OldDirection,
        o.StaffType, o.StaffId, o.OnDate, o.ScannedOn,
        SUM(r.StartsNewSession) OVER (
            PARTITION BY o.StaffType, o.StaffId, o.OnDate
            ORDER BY o.ScannedOn, o.Id
            ROWS UNBOUNDED PRECEDING
        ) AS SessionNumber
    FROM Recomputed r
    JOIN Ordered o ON o.Id = r.Id
),
Sessioned AS (
    SELECT
        Id,
        OldDirection,
        ROW_NUMBER() OVER (
            PARTITION BY StaffType, StaffId, OnDate, SessionNumber
            ORDER BY ScannedOn, Id
        ) AS PositionInSession
    FROM Numbered
),
Fixed AS (
    SELECT
        Id,
        OldDirection,
        CASE WHEN PositionInSession % 2 = 1 THEN N'In' ELSE N'Out' END AS NewDirection
    FROM Sessioned
)
UPDATE a
SET a.Direction = f.NewDirection
OUTPUT deleted.Id, deleted.Direction AS WasDirection, inserted.Direction AS NowDirection
FROM dbo.StaffAttendance a
JOIN Fixed f ON f.Id = a.Id
WHERE f.OldDirection <> f.NewDirection;
GO
