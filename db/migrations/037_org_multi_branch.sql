SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
/*
    037_org_multi_branch.sql
    -------------------------------------------------------------------------
    One person, several branches.

    "Branch can be multiple for same person" -- somebody who teaches at Rajpur on
    Monday and elsewhere on Thursday belongs to both, and the chart has to say so
    rather than making the administrator pick one.

    Held as a comma-separated list in the existing column, widened to take a few
    names. A child table would be tidier in the abstract, but branch here is a label
    on a drawing, not something joined to or reported on -- and the branch names come
    from a master the administrator can rename, which a list of foreign keys would
    make harder rather than safer.

    OrgNode.Branch and Teacher.Branch only. dbo.Receptionist.Branch is deliberately
    LEFT ALONE: receptionist payments and the branch-wise income report compare that
    column to a single branch name, and a list in it would quietly break both.
    A receptionist who covers two branches gets a box of their own on the chart, which
    is the same override that gives anybody a photograph.

    Widening a column only. Idempotent.
*/

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.OrgNode') AND name = 'Branch'
             AND max_length < 600)
BEGIN
    ALTER TABLE dbo.OrgNode ALTER COLUMN Branch nvarchar(300) NULL;
    PRINT 'Widened dbo.OrgNode.Branch to hold several branch names.';
END
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Teacher') AND name = 'Branch'
             AND max_length < 600)
BEGIN
    ALTER TABLE dbo.Teacher ALTER COLUMN Branch nvarchar(300) NULL;
    PRINT 'Widened dbo.Teacher.Branch to hold several branch names.';
END
GO

SELECT table_name = OBJECT_NAME(object_id), column_name = name, characters = max_length / 2
FROM   sys.columns
WHERE  name = 'Branch'
  AND  object_id IN (OBJECT_ID('dbo.OrgNode'), OBJECT_ID('dbo.Teacher'), OBJECT_ID('dbo.Receptionist'));
GO
