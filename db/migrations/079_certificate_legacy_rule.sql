-- Certificate grade rule: who keeps the old one.
--
-- da8635e changed how a certificate's grade is worked out (every Theory and
-- Practical sitting of the course counted, rows on the same date taken once).
-- Students already marked "course completed" when that went live keep the rule
-- they were graded under, so their grade never moves - not even on a duplicate
-- copy. This table is that list: the students with IsComplete = 1 at the moment
-- the table is created. Everyone who completes later gets the new rule.
--
-- The list is taken ONCE, when the table is first created; re-running this
-- script never adds anyone, so the cut-off stays the day it went live.
--
-- Idempotent: safe to run more than once.

IF OBJECT_ID('dbo.CertificateLegacyRule') IS NULL
BEGIN
    CREATE TABLE dbo.CertificateLegacyRule (
        StuRegId  VARCHAR(50)  NOT NULL CONSTRAINT PK_CertificateLegacyRule PRIMARY KEY,
        AddedOn   DATETIME2    NOT NULL CONSTRAINT DF_CertificateLegacyRule_AddedOn DEFAULT SYSDATETIME()
    );

    -- Registration ids are not unique in the migrated data, and the database
    -- collation ignores case, so DISTINCT folds AVI/... and avi/... together.
    INSERT INTO dbo.CertificateLegacyRule (StuRegId)
    SELECT DISTINCT StuRegId
    FROM dbo.Student
    WHERE IsComplete = 1 AND StuRegId IS NOT NULL;
END
GO
