-- How a theory exam is sat, chosen per student when it is scheduled:
--   Online  - taken in the app against the question bank (the existing flow), or
--   Offline - written on paper in the room, its mark entered by hand afterwards.
-- Always 'Online' for a practical, which has no online form. Existing rows are
-- online exams, which the default backfills.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.ExamSchedule') AND name = 'Mode'
)
    ALTER TABLE dbo.ExamSchedule
        ADD Mode NVARCHAR(10) NOT NULL
            CONSTRAINT DF_ExamSchedule_Mode DEFAULT 'Online';
