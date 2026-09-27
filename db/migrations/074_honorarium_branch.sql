-- Proprietor honorarium per branch.
--
-- Each amount can now be charged to one branch (by name, the same key every
-- other income-report figure is filtered on) or, with Branch NULL, to the
-- institute as a whole as before. Existing rows stay institute-wide. A branch
-- report carries only that branch's amount; the whole-institute report carries
-- all of them.
--
-- One amount per branch per starting month, so the unique index moves from
-- (EffectiveFrom) to (Branch, EffectiveFrom).
--
-- Idempotent: safe to run more than once.

IF COL_LENGTH('dbo.ProprietorHonorarium', 'Branch') IS NULL
    ALTER TABLE dbo.ProprietorHonorarium ADD Branch nvarchar(100) NULL;
GO

IF EXISTS (SELECT 1 FROM sys.indexes
           WHERE name = 'UX_ProprietorHonorarium_EffectiveFrom'
             AND object_id = OBJECT_ID('dbo.ProprietorHonorarium'))
    DROP INDEX UX_ProprietorHonorarium_EffectiveFrom ON dbo.ProprietorHonorarium;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'UX_ProprietorHonorarium_Branch_EffectiveFrom'
                 AND object_id = OBJECT_ID('dbo.ProprietorHonorarium'))
    CREATE UNIQUE INDEX UX_ProprietorHonorarium_Branch_EffectiveFrom
        ON dbo.ProprietorHonorarium (Branch, EffectiveFrom);
GO
