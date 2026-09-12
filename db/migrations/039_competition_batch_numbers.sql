SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    039_competition_batch_numbers.sql
    -------------------------------------------------------------------------
    Dance places are limited, and every dancer gets a chest number.

    The owner replaced the reporting-time idea with something better:

        "Group A - will be maximum 30 student registration, B will be maximum 30
         student registration, C will be max 15 student registration ... based on
         birth certificate date of birth application will validate the exact based
         one that registration id will be generated and candidate able to download
         Batch like 1,2 etc, which will taken printout and kept in chest at the time
         of dance only then no batch collection timing will not be required from
         admin it self"

    So:

      Capacity   how many may enter a group. 30 / 30 / 15 for dance; NULL for the
                 drawing divisions, which are not capped.
      BatchNo    the chest number, 1 upwards within its own group, issued at
                 registration and printed by the entrant.

    ReportingTime goes. It was the admin-set batch collection time, and the chest
    number does that job without anyone having to set anything -- the owner's point
    exactly. Dropped rather than left unused: no registration has been taken yet, and
    a column nobody writes is a column somebody later trusts.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.CompetitionGroup') AND name = 'Capacity')
BEGIN
    ALTER TABLE dbo.CompetitionGroup ADD Capacity int NULL;
    PRINT 'Added dbo.CompetitionGroup.Capacity.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.CompetitionEntry') AND name = 'BatchNo')
BEGIN
    ALTER TABLE dbo.CompetitionEntry ADD BatchNo int NULL;
    PRINT 'Added dbo.CompetitionEntry.BatchNo.';
END
GO

/* One chest number per group. Filtered, because the drawing entries carry none and
   would otherwise all collide on NULL. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_CompetitionEntry_Batch')
BEGIN
    CREATE UNIQUE INDEX UQ_CompetitionEntry_Batch
        ON dbo.CompetitionEntry (GroupId, BatchNo)
        WHERE BatchNo IS NOT NULL;

    PRINT 'Added the one-chest-number-per-group index.';
END
GO

/* The capacities from the owner's message. */
UPDATE dbo.CompetitionGroup SET Capacity = 30 WHERE Kind = 'Dance' AND Code = 'A' AND Capacity IS NULL;
UPDATE dbo.CompetitionGroup SET Capacity = 30 WHERE Kind = 'Dance' AND Code = 'B' AND Capacity IS NULL;
UPDATE dbo.CompetitionGroup SET Capacity = 15 WHERE Kind = 'Dance' AND Code = 'C' AND Capacity IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.CompetitionGroup') AND name = 'ReportingTime')
BEGIN
    ALTER TABLE dbo.CompetitionGroup DROP COLUMN ReportingTime;
    PRINT 'Dropped dbo.CompetitionGroup.ReportingTime -- the chest number replaces it.';
END
GO

SELECT Kind, Code, LabelEnglish, Capacity
FROM   dbo.CompetitionGroup
ORDER  BY Kind, SortOrder;
GO
