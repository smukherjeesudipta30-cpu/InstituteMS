SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    037_staff_qualification_required.sql
    -------------------------------------------------------------------------
    Qualification and computer training, required of every member of staff
    except the technical ones.

    The owner: "Qualification will be mandatory for all stuff and computer
    training will be mandatory, only for technical stuff those will not be
    mandatory."

    Two things follow from that.

    ComputerTraining is a new column beside Qualification: what somebody studied
    and what computer training they hold are different answers, and an institute
    that teaches computing has a reason to record the second one separately.

    IsTechnical is how a person is excused, and it is a tick the administrator
    sets rather than a name this script recognises. Matching on the word
    "Technical" in a job title would work today and quietly fail the moment
    somebody is called "IT Support" or "Systems" instead -- and the owner has
    been clear throughout that the chart is not to depend on fixed lists of
    names.

    The requirement is enforced in the application, not as a NOT NULL column
    here: 33 teachers and 3 receptionists already exist with no qualification on
    file, and a NOT NULL constraint would refuse to add the column at all. They
    are filled in as each person is next edited.

    Only ADDS columns. Idempotent: safe to run twice.
*/

/* --------------------------------------------------------------- teachers */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'ComputerTraining')
BEGIN
    ALTER TABLE dbo.Teacher ADD ComputerTraining nvarchar(200) NULL;
    PRINT 'Added dbo.Teacher.ComputerTraining.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'IsTechnical')
BEGIN
    ALTER TABLE dbo.Teacher
        ADD IsTechnical bit NOT NULL CONSTRAINT DF_Teacher_IsTechnical DEFAULT (0);

    PRINT 'Added dbo.Teacher.IsTechnical.';
END
GO

/* ---------------------------------------------------------- receptionists */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'ComputerTraining')
BEGIN
    ALTER TABLE dbo.Receptionist ADD ComputerTraining nvarchar(200) NULL;
    PRINT 'Added dbo.Receptionist.ComputerTraining.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Receptionist') AND name = 'IsTechnical')
BEGIN
    ALTER TABLE dbo.Receptionist
        ADD IsTechnical bit NOT NULL CONSTRAINT DF_Receptionist_IsTechnical DEFAULT (0);

    PRINT 'Added dbo.Receptionist.IsTechnical.';
END
GO

/* ------------------------------------------- hand-made boxes on the chart */

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.OrgNode') AND name = 'ComputerTraining')
BEGIN
    ALTER TABLE dbo.OrgNode ADD ComputerTraining nvarchar(200) NULL;
    PRINT 'Added dbo.OrgNode.ComputerTraining.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.OrgNode') AND name = 'IsTechnical')
BEGIN
    ALTER TABLE dbo.OrgNode
        ADD IsTechnical bit NOT NULL CONSTRAINT DF_OrgNode_IsTechnical DEFAULT (0);

    PRINT 'Added dbo.OrgNode.IsTechnical.';
END
GO

/*
    The Technical Support box that already exists is the one thing on the chart
    today that is plainly technical, so it starts out excused. Anyone else is
    marked by the administrator.
*/
UPDATE dbo.OrgNode
SET    IsTechnical = 1
WHERE  IsTechnical = 0
  AND  StaffId IS NULL
  AND  Designation LIKE '%Technical%';
GO

SELECT excused_boxes = COUNT(*) FROM dbo.OrgNode WHERE IsTechnical = 1;

SELECT staff_still_needing_qualification =
       (SELECT COUNT(*) FROM dbo.Teacher
        WHERE IsActive = 1 AND IsTechnical = 0
          AND (Qualification IS NULL OR LTRIM(RTRIM(Qualification)) = ''))
     + (SELECT COUNT(*) FROM dbo.Receptionist
        WHERE IsActive = 1 AND IsTechnical = 0
          AND (Qualification IS NULL OR LTRIM(RTRIM(Qualification)) = ''));
GO
