-- Proctoring for the teacher competency exam, same as the student portal:
-- leaving full screen or switching tabs is counted server-side against the
-- attempt so a reload cannot reset the strike count.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.TeacherExamAttempt') AND name = 'ViolationCount'
)
    ALTER TABLE dbo.TeacherExamAttempt
        ADD ViolationCount INT NOT NULL
            CONSTRAINT DF_TeacherExamAttempt_ViolationCount DEFAULT 0;
