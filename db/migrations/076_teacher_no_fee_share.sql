-- A teacher who takes no share of student fees.
--
-- The proprietor teaches but is paid only by the Proprietor Honorarium
-- (migration 072), never a fee share. With NoFeeShare = 1 a fee still records
-- a TecFees row for them - so the bill keeps its teacher count and a
-- co-teacher's share is exactly what it was - but that row's amount is 0,
-- and it is not shown as earnings.
--
-- Idempotent: safe to run more than once. Sets no teacher; that is a data
-- decision made separately.

IF COL_LENGTH('dbo.Teacher', 'NoFeeShare') IS NULL
    ALTER TABLE dbo.Teacher ADD NoFeeShare BIT NOT NULL
        CONSTRAINT DF_Teacher_NoFeeShare DEFAULT (0);
GO
