SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    054_exam_attempt_shuffle_seed.sql
    -------------------------------------------------------------------------
    Two students of the same course sitting the same paper at the same time
    used to see the identical question order (dbo.ExamQuestion.QuestionNo,
    ascending) -- an easy paper to copy off the next desk over, since "your
    question 7" was always "their question 7" too.

    ShuffleSeed is generated once, when the attempt is first opened, and
    stored on the row rather than reshuffled on every request -- reopening a
    paper after a dropped connection must show the SAME order the student
    already had, not a fresh shuffle that no longer matches what they had
    answered against. NULL on every attempt that existed before this column:
    those keep the original QuestionNo order they were already sat with,
    rather than reshuffling an exam that may already be mid-sitting.
*/

IF COL_LENGTH('dbo.ExamAttempt', 'ShuffleSeed') IS NULL
BEGIN
    ALTER TABLE dbo.ExamAttempt ADD ShuffleSeed int NULL;
END
GO
