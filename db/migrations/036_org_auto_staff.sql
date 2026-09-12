SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    036_org_auto_staff.sql
    -------------------------------------------------------------------------
    Everyone on the chart, including the people hired next year.

    The owner asked for teachers and receptionists under Nargis, and then made the
    rule explicit: "It will be dynamic only", "No fixed count of teacher,
    receptionist, Technical staff and all", "Designation also will be dynamic".

    So nothing here is a list and nothing is a count. One box is marked as the
    default parent for staff; every teacher and receptionist WITHOUT a box of their
    own is drawn beneath it automatically. Hire someone, and they appear. Remove
    someone, and they go. Nobody maintains a roster of boxes, and no number is
    written down anywhere to fall out of date.

    Position, qualification, branch and active belong to the staff record itself,
    so they are free text the administrator types per person -- not a fixed set of
    designations to choose from. Technical Support and any other function stays a
    hand-made box, added and renamed at will.

    An automatic box shows what the staff record knows. To give someone a
    photograph or a different name on the chart, the administrator creates a real
    box for them linked to the same staff record, and the automatic one steps aside.

    Only ADDS columns. Idempotent: safe to run twice.
*/

/* -------------------------------------------------- the default parent rule */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.OrgNode') AND name = 'IsDefaultStaffParent')
BEGIN
    ALTER TABLE dbo.OrgNode
        ADD IsDefaultStaffParent bit NOT NULL
            CONSTRAINT DF_OrgNode_IsDefaultStaffParent DEFAULT (0);

    PRINT 'Added dbo.OrgNode.IsDefaultStaffParent.';
END
GO

/* Only one box may carry it, or staff would be drawn twice. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_OrgNode_DefaultStaffParent')
BEGIN
    CREATE UNIQUE INDEX UQ_OrgNode_DefaultStaffParent
        ON dbo.OrgNode (IsDefaultStaffParent)
        WHERE IsDefaultStaffParent = 1;

    PRINT 'Added the single-default-parent index.';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.OrgNode WHERE IsDefaultStaffParent = 1)
BEGIN
    UPDATE dbo.OrgNode
    SET    IsDefaultStaffParent = 1
    WHERE  PersonName = 'Nargis Khatun';

    PRINT 'Nargis Khatun set as the default parent for staff.';
END
GO

/* ------------------------------------------- what an automatic box can show */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'Designation')
BEGIN
    ALTER TABLE dbo.Teacher ADD Designation nvarchar(150) NULL;
    PRINT 'Added dbo.Teacher.Designation.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'Qualification')
BEGIN
    ALTER TABLE dbo.Teacher ADD Qualification nvarchar(200) NULL;
    PRINT 'Added dbo.Teacher.Qualification.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'Branch')
BEGIN
    ALTER TABLE dbo.Teacher ADD Branch nvarchar(100) NULL;
    PRINT 'Added dbo.Teacher.Branch.';
END
GO

/* Teachers had no active flag at all; without one, a teacher who leaves can only
   be deleted, which would take their attendance and salary history with them. */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'IsActive')
BEGIN
    ALTER TABLE dbo.Teacher
        ADD IsActive bit NOT NULL CONSTRAINT DF_Teacher_IsActive DEFAULT (1);

    PRINT 'Added dbo.Teacher.IsActive (everyone on file today stays active).';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'Designation')
BEGIN
    ALTER TABLE dbo.Receptionist ADD Designation nvarchar(150) NULL;
    PRINT 'Added dbo.Receptionist.Designation.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'Qualification')
BEGIN
    ALTER TABLE dbo.Receptionist ADD Qualification nvarchar(200) NULL;
    PRINT 'Added dbo.Receptionist.Qualification.';
END
GO

SELECT default_parent = PersonName FROM dbo.OrgNode WHERE IsDefaultStaffParent = 1;

SELECT drawn_automatically_today =
       (SELECT COUNT(*) FROM dbo.Teacher t
        WHERE t.IsActive = 1
          AND NOT EXISTS (SELECT 1 FROM dbo.OrgNode n
                          WHERE n.StaffType = 'Teacher' AND n.StaffId = t.id))
     + (SELECT COUNT(*) FROM dbo.Receptionist r
        WHERE r.IsActive = 1
          AND NOT EXISTS (SELECT 1 FROM dbo.OrgNode n
                          WHERE n.StaffType = 'Receptionist' AND n.StaffId = r.id));
GO
