-- Proprietor honorarium.
--
-- A monthly amount Admin sets for the proprietor, counted as a cost of the
-- whole institute (never of one branch) so the income report's net profit is
-- the real figure, month by month and year by year. Each row applies from its
-- month until the next one, so changing it later does not rewrite past months.
--
-- Idempotent: safe to run more than once.

IF OBJECT_ID('dbo.ProprietorHonorarium') IS NULL
BEGIN
    CREATE TABLE dbo.ProprietorHonorarium (
        Id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProprietorHonorarium PRIMARY KEY,
        EffectiveFrom DATE           NOT NULL,
        MonthlyAmount DECIMAL(12,2)  NOT NULL,
        Note          NVARCHAR(500)  NULL,
        CreatedBy     VARCHAR(250)   NULL,
        CreatedOn     DATETIME2      NOT NULL CONSTRAINT DF_ProprietorHonorarium_CreatedOn DEFAULT SYSDATETIME()
    );

    CREATE UNIQUE INDEX UX_ProprietorHonorarium_EffectiveFrom ON dbo.ProprietorHonorarium (EffectiveFrom);
END
GO
