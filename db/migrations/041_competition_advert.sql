SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    041_competition_advert.sql
    -------------------------------------------------------------------------
    The printed posters, shown on the registration portal.

    The owner asked for two upload slots on the admin screen -- "right now for
    testing old year advertisement can up from next year new advertisement can be
    up" -- so the poster is per competition YEAR, not a fixed image in the site's
    files. Last year's artwork goes up today; next August somebody replaces it from
    the same screen without anyone touching the server.

    Two slots because there are two competitions and two posters: the Bengali
    drawing sheet and the dance sheet. Slot is a string rather than two columns so a
    third poster costs a row, not a migration.

    Stored inline as Base64 beside the other competition documents, for the same
    reason: the artwork then travels with the database backup instead of living on a
    file share that has to be backed up and restored in step.
*/

IF OBJECT_ID('dbo.CompetitionAdvert') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitionAdvert
    (
        Id            int IDENTITY(1,1) NOT NULL,
        CompetitionId int              NOT NULL,

        /* 'Drawing' or 'Dance'. */
        Slot          nvarchar(20)     NOT NULL,

        FileName      nvarchar(200)    NULL,
        ContentType   nvarchar(100)    NULL,
        Content       nvarchar(max)    NOT NULL,

        /* Kept so the portal can size the image without waiting to decode it. */
        Width         int              NULL,
        Height        int              NULL,

        UploadedBy    nvarchar(50)     NULL,
        UploadedOn    datetime         NOT NULL CONSTRAINT DF_CompetitionAdvert_On DEFAULT (GETDATE()),

        CONSTRAINT PK_CompetitionAdvert PRIMARY KEY (Id),
        CONSTRAINT FK_CompetitionAdvert_Competition
            FOREIGN KEY (CompetitionId) REFERENCES dbo.Competition (Id),
        CONSTRAINT CK_CompetitionAdvert_Slot CHECK (Slot IN ('Drawing', 'Dance')),

        /* One poster per slot per year; uploading again replaces it. */
        CONSTRAINT UQ_CompetitionAdvert_Slot UNIQUE (CompetitionId, Slot)
    );

    PRINT 'Created dbo.CompetitionAdvert.';
END
GO

SELECT adverts = (SELECT COUNT(*) FROM dbo.CompetitionAdvert);
GO
