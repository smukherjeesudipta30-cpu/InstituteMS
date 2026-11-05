-- Avijan PreSchool: the store — tuition books, bags and the like, class by
-- class. The admin or receptionist enters what comes in; when a child is given
-- their item it is taken off that class's stock.
--
-- PreSchoolStoreItem      one thing kept for one class (e.g. "KG-1 tuition book
--                         set"), or for every class when ClassId is empty
-- PreSchoolStoreMovement  stock in (received), or out (given to one child)
--
-- Stock is never stored: received less given. A movement made in error is
-- removed by the PreSchool admin, which puts the stock back.
--
-- Adds tables only. Nothing in the Computer Centre is touched.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.PreSchoolStoreItem') IS NULL
    CREATE TABLE dbo.PreSchoolStoreItem (
        Id          INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolStoreItem PRIMARY KEY,
        Name        NVARCHAR(100)  NOT NULL,
        ClassId     INT            NULL CONSTRAINT FK_PreSchoolStoreItem_Class REFERENCES dbo.PreSchoolClass (Id),
        SortOrder   INT            NOT NULL CONSTRAINT DF_PreSchoolStoreItem_SortOrder DEFAULT (0),
        IsActive    BIT            NOT NULL CONSTRAINT DF_PreSchoolStoreItem_IsActive DEFAULT (1),
        CreatedBy   VARCHAR(250)   NULL,
        CreatedOn   DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolStoreItem_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy  VARCHAR(250)   NULL,
        ModifiedOn  DATETIME2      NULL
    );
GO

IF OBJECT_ID('dbo.PreSchoolStoreMovement') IS NULL
    CREATE TABLE dbo.PreSchoolStoreMovement (
        Id          INT            IDENTITY(1,1) NOT NULL CONSTRAINT PK_PreSchoolStoreMovement PRIMARY KEY,
        ItemId      INT            NOT NULL CONSTRAINT FK_PreSchoolStoreMovement_Item REFERENCES dbo.PreSchoolStoreItem (Id),
        Kind        VARCHAR(10)    NOT NULL,
        Quantity    INT            NOT NULL,
        StudentId   INT            NULL CONSTRAINT FK_PreSchoolStoreMovement_Student REFERENCES dbo.PreSchoolStudent (Id),
        OnDate      DATE           NOT NULL,
        Note        NVARCHAR(200)  NULL,
        CreatedBy   VARCHAR(250)   NULL,
        CreatedOn   DATETIME2      NOT NULL CONSTRAINT DF_PreSchoolStoreMovement_CreatedOn DEFAULT (SYSDATETIME()),
        CONSTRAINT CK_PreSchoolStoreMovement_Kind CHECK (Kind IN ('In', 'Out')),
        CONSTRAINT CK_PreSchoolStoreMovement_Quantity CHECK (Quantity > 0),
        CONSTRAINT CK_PreSchoolStoreMovement_Student CHECK (Kind = 'In' OR StudentId IS NOT NULL)
    );
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolStoreMovement_Item' AND object_id = OBJECT_ID('dbo.PreSchoolStoreMovement'))
    CREATE INDEX IX_PreSchoolStoreMovement_Item ON dbo.PreSchoolStoreMovement (ItemId, Kind);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PreSchoolStoreMovement_Student' AND object_id = OBJECT_ID('dbo.PreSchoolStoreMovement'))
    CREATE INDEX IX_PreSchoolStoreMovement_Student ON dbo.PreSchoolStoreMovement (StudentId) WHERE StudentId IS NOT NULL;
GO

-- A start for each class: a tuition book set and a school bag, with no stock
-- yet. The office renames, adds or switches them off on the Store screen.
IF NOT EXISTS (SELECT 1 FROM dbo.PreSchoolStoreItem)
BEGIN
    INSERT dbo.PreSchoolStoreItem (Name, ClassId, SortOrder, CreatedBy)
    SELECT c.Name + N' tuition book set', c.Id, c.SortOrder * 10 + 1, 'migration 093' FROM dbo.PreSchoolClass c WHERE c.IsActive = 1;
    INSERT dbo.PreSchoolStoreItem (Name, ClassId, SortOrder, CreatedBy)
    SELECT c.Name + N' school bag', c.Id, c.SortOrder * 10 + 2, 'migration 093' FROM dbo.PreSchoolClass c WHERE c.IsActive = 1;
END
GO
