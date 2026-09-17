/*
    035_org_hierarchy.sql
    -------------------------------------------------------------------------
    The staff hierarchy, as the owner describes it:

        Anindita Mukherjee (Owner)
          ├── Nargis Khatun (Head Receptionist & HR)
          │      └── every teacher and receptionist
          └── Arkita Nath (Entertainment & Play Section)
                 └── Nabamita Das
        Technical Support  ── reports to BOTH Nargis and Arkita

    Two tables rather than a parent column on one, because that last line is not
    a tree. Technical Support answers to two people, and any "ManagerId" column
    can hold only one. Nodes are the boxes; edges are the lines between them, so a
    box may have as many parents as the institute actually gives it.

    Each box keeps its OWN name, designation, qualification, email, photograph,
    branch and active flag. It may also point at a Teacher or Receptionist record,
    but only as a reference: the owner asked that every name, the image and the
    mail id all stay editable here, and a box like "Technical Support" is a
    function with nobody attached at all. Editing the chart therefore never
    disturbs the staff master, and vice versa.

    Only ADDS tables. Nothing that runs today reads them, so the live site is
    unchanged by this migration.

    Idempotent: safe to run twice.
*/

IF OBJECT_ID('dbo.OrgNode', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrgNode
    (
        Id              int IDENTITY(1,1) NOT NULL,

        /* Editable, and deliberately not a foreign key to a staff name. */
        PersonName      nvarchar(150) NOT NULL,

        /* "Owner", "Head Receptionist & HR", "Entertainment & Play Section". */
        Designation     nvarchar(150) NULL,
        Qualification   nvarchar(200) NULL,

        /* May differ from the address on the staff record — the owner asked that
           this be changeable on its own. */
        Email           nvarchar(150) NULL,

        Branch          nvarchar(100) NULL,
        IsActive        bit           NOT NULL CONSTRAINT DF_OrgNode_IsActive DEFAULT (1),

        /* Left-to-right placement among boxes sharing a parent. */
        SortOrder       int           NOT NULL CONSTRAINT DF_OrgNode_SortOrder DEFAULT (0),

        /* Optional reference to the staff master. Null for a function such as
           Technical Support, which is a box on the chart and nobody's record. */
        StaffType       nvarchar(20)  NULL,
        StaffId         int           NULL,

        /* Photograph held the way student photos are: Base64 inline, so it travels
           with the database backup and needs no file share. */
        PhotoFileName   nvarchar(200) NULL,
        PhotoContentType nvarchar(100) NULL,
        PhotoBase64     nvarchar(max) NULL,

        CreatedBy       nvarchar(50)  NULL,
        CreatedOn       datetime2(0)  NOT NULL CONSTRAINT DF_OrgNode_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy      nvarchar(50)  NULL,
        ModifiedOn      datetime2(0)  NULL,

        CONSTRAINT PK_OrgNode PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_OrgNode_StaffType CHECK (StaffType IS NULL OR StaffType IN ('Teacher', 'Receptionist'))
    );

    PRINT 'Created dbo.OrgNode.';
END
ELSE
BEGIN
    PRINT 'dbo.OrgNode already exists.';
END
GO

IF OBJECT_ID('dbo.OrgEdge', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrgEdge
    (
        Id          int IDENTITY(1,1) NOT NULL,

        /* The box above. Null would mean a root, but roots are simply boxes with
           no incoming edge, so this is required. */
        ParentId    int NOT NULL,
        ChildId     int NOT NULL,

        CreatedBy   nvarchar(50) NULL,
        CreatedOn   datetime2(0) NOT NULL CONSTRAINT DF_OrgEdge_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_OrgEdge PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_OrgEdge_Parent FOREIGN KEY (ParentId) REFERENCES dbo.OrgNode (Id),
        CONSTRAINT FK_OrgEdge_Child  FOREIGN KEY (ChildId)  REFERENCES dbo.OrgNode (Id),

        /* The same line drawn twice is not a second reporting relationship. */
        CONSTRAINT UQ_OrgEdge UNIQUE (ParentId, ChildId),

        /* Nobody reports to themselves. Longer loops cannot be expressed as a
           constraint and are refused by the service before the row is written. */
        CONSTRAINT CK_OrgEdge_NoSelf CHECK (ParentId <> ChildId)
    );

    PRINT 'Created dbo.OrgEdge.';
END
ELSE
BEGIN
    PRINT 'dbo.OrgEdge already exists.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OrgEdge_Child' AND object_id = OBJECT_ID('dbo.OrgEdge'))
    CREATE INDEX IX_OrgEdge_Child ON dbo.OrgEdge (ChildId) INCLUDE (ParentId);
GO

/* ------------------------------- the hierarchy the owner described ---------- */
/*
    Seeded so the chart can be seen and judged before the admin screen exists.
    Everything here is editable afterwards; names, photographs and email
    addresses are expected to change.
*/

IF NOT EXISTS (SELECT 1 FROM dbo.OrgNode)
BEGIN
    DECLARE @staff TABLE (Nm nvarchar(150), Tid int);
    INSERT @staff (Nm, Tid)
    SELECT TecName, id FROM dbo.Teacher
    WHERE TecName IN ('Anindita Mukherjee', 'Nargis Khatun', 'Arkita Nath', 'Nabamita Das');

    INSERT dbo.OrgNode (PersonName, Designation, Branch, StaffType, StaffId, SortOrder, CreatedBy)
    SELECT 'Anindita Mukherjee', 'Owner', 'Rajpur', 'Teacher',
           (SELECT Tid FROM @staff WHERE Nm = 'Anindita Mukherjee'), 0, 'migration-035';

    INSERT dbo.OrgNode (PersonName, Designation, Branch, StaffType, StaffId, SortOrder, CreatedBy)
    SELECT 'Nargis Khatun', 'Head Receptionist & HR', 'Rajpur', 'Teacher',
           (SELECT Tid FROM @staff WHERE Nm = 'Nargis Khatun'), 0, 'migration-035';

    INSERT dbo.OrgNode (PersonName, Designation, Branch, StaffType, StaffId, SortOrder, CreatedBy)
    SELECT 'Arkita Nath', 'Entertainment & Play Section', 'Rajpur', 'Teacher',
           (SELECT Tid FROM @staff WHERE Nm = 'Arkita Nath'), 1, 'migration-035';

    INSERT dbo.OrgNode (PersonName, Designation, Branch, StaffType, StaffId, SortOrder, CreatedBy)
    SELECT 'Nabamita Das', 'Play Section', 'Rajpur', 'Teacher',
           (SELECT Tid FROM @staff WHERE Nm = 'Nabamita Das'), 0, 'migration-035';

    /* A function, not a person: no staff record, and it answers to two people. */
    INSERT dbo.OrgNode (PersonName, Designation, Branch, SortOrder, CreatedBy)
    VALUES ('Technical Support', 'Technical Support', 'Rajpur', 2, 'migration-035');

    DECLARE @owner int = (SELECT Id FROM dbo.OrgNode WHERE PersonName = 'Anindita Mukherjee');
    DECLARE @nargis int = (SELECT Id FROM dbo.OrgNode WHERE PersonName = 'Nargis Khatun');
    DECLARE @arkita int = (SELECT Id FROM dbo.OrgNode WHERE PersonName = 'Arkita Nath');
    DECLARE @nabamita int = (SELECT Id FROM dbo.OrgNode WHERE PersonName = 'Nabamita Das');
    DECLARE @tech int = (SELECT Id FROM dbo.OrgNode WHERE PersonName = 'Technical Support');

    INSERT dbo.OrgEdge (ParentId, ChildId, CreatedBy) VALUES
        (@owner,  @nargis,   'migration-035'),
        (@owner,  @arkita,   'migration-035'),
        (@arkita, @nabamita, 'migration-035'),
        -- the two-parent case
        (@nargis, @tech,     'migration-035'),
        (@arkita, @tech,     'migration-035');

    PRINT 'Seeded the hierarchy.';
END
ELSE
BEGIN
    PRINT 'dbo.OrgNode already has rows - left alone.';
END
GO

SELECT box = n.PersonName, n.Designation, n.Branch,
       reports_to = ISNULL(STUFF((
           SELECT ', ' + p.PersonName
           FROM dbo.OrgEdge e JOIN dbo.OrgNode p ON p.Id = e.ParentId
           WHERE e.ChildId = n.Id
           ORDER BY p.PersonName
           FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, ''), '(top of the chart)')
FROM dbo.OrgNode n
ORDER BY n.SortOrder, n.PersonName;
GO
