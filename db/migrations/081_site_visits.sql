-- Visitors to the public pages (home, careers, competition, the app's front
-- page), to see how the site's SEO and social-media promotion is working.
--
-- No personal data: VisitorKey is a one-way daily hash of address + browser,
-- so a person counts once a day and cannot be identified or followed.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.SiteVisit') IS NULL
BEGIN
    CREATE TABLE dbo.SiteVisit (
        Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SiteVisit PRIMARY KEY,
        VisitedOn  DATETIME2     NOT NULL,
        OnDate     DATE          NOT NULL,
        Path       VARCHAR(100)  NOT NULL,
        Source     VARCHAR(30)   NOT NULL,
        Platform   VARCHAR(10)   NOT NULL,
        VisitorKey CHAR(32)      NOT NULL
    );

    CREATE INDEX IX_SiteVisit_OnDate ON dbo.SiteVisit (OnDate) INCLUDE (Path, Source, Platform, VisitorKey);
    CREATE INDEX IX_SiteVisit_Visitor ON dbo.SiteVisit (VisitorKey, Path, VisitedOn);
END
GO
