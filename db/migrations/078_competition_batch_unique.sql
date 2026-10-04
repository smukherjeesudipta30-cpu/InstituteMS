-- Dance chest numbers unique across the whole competition, not just per group.
--
-- Each dance group now issues from its own block, sized by its capacity in
-- display order (30, 30, 15 places -> 1-30, 31-60, 61-75), so no two dancers
-- on the day wear the same number. The service checks every number already
-- issued in the competition; this index is the backstop for two forms
-- arriving at the same moment.
--
-- Numbers already issued are not changed — they may be printed and pinned on.
-- If two groups already share a number, the index cannot be created; that is
-- reported below and the service's own check still prevents any NEW repeat.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_CompetitionEntry_CompetitionBatch')
BEGIN
    IF EXISTS (SELECT CompetitionId, BatchNo
               FROM dbo.CompetitionEntry
               WHERE BatchNo IS NOT NULL
               GROUP BY CompetitionId, BatchNo
               HAVING COUNT(*) > 1)
    BEGIN
        PRINT 'NOT created: some chest numbers are already shared between dance groups.';
        PRINT 'Existing numbers were left as they are; new numbers will still never repeat.';
    END
    ELSE
    BEGIN
        CREATE UNIQUE INDEX UQ_CompetitionEntry_CompetitionBatch
            ON dbo.CompetitionEntry (CompetitionId, BatchNo)
            WHERE BatchNo IS NOT NULL;

        PRINT 'Added the one-chest-number-per-competition index.';
    END
END
GO
