-- AI-generated study notes for the teacher to prepare from before their
-- sitting, generated alongside the questions when a sitting is scheduled.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.TeacherExamSchedule') AND name = 'StudyGuide'
)
    ALTER TABLE dbo.TeacherExamSchedule
        ADD StudyGuide NVARCHAR(MAX) NULL;
