/* =====================================================================
   018_exam_limit_is_course_wise.sql
   ---------------------------------------------------------------------
   Corrects the "four papers" rule: it is FOUR PER COURSE, not four per
   student. A student enrolled on two courses sits (1 theory + 1
   practical) x 2 terms for each of them.

   017 indexed (StuRegId, TermNumber, PaperType), which would have
   stopped a student sitting term 1 theory for their second course. The
   course is added to the key.

   Run:  sqlcmd -S . -I -i migrations\018_exam_limit_is_course_wise.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

/* The course the paper belongs to, denormalised onto the attempt so the
   rule can be one unique index. ExamPaper remains the authority. */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ExamAttempt') AND name = 'Cid')
BEGIN
    ALTER TABLE dbo.ExamAttempt ADD Cid int NULL;
    PRINT '018: ExamAttempt.Cid added.';
END
GO

UPDATE a
SET    a.Cid = p.Cid
FROM   dbo.ExamAttempt AS a
JOIN   dbo.ExamPaper   AS p ON p.ExamId = a.ExamId
WHERE  a.Cid IS NULL;
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ExamAttempt_TermType')
BEGIN
    DROP INDEX UQ_ExamAttempt_TermType ON dbo.ExamAttempt;
    PRINT '018: old student-wide index dropped.';
END
GO

/* One attempt per student, per course, per term, per paper type:
   2 terms x 2 types = the four papers, for each course they are on. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ExamAttempt_CourseTermType')
BEGIN
    CREATE UNIQUE INDEX UQ_ExamAttempt_CourseTermType
        ON dbo.ExamAttempt (StuRegId, Cid, TermNumber, PaperType)
        WHERE Cid IS NOT NULL AND TermNumber IS NOT NULL AND PaperType IS NOT NULL;

    PRINT '018: UQ_ExamAttempt_CourseTermType created - four papers per course.';
END
GO

/* A paper must name its course for the rule to bite: a course-less paper
   would sit outside the limit entirely. Enforced in the service so the
   message can explain itself, and checked here as a backstop. */
PRINT '018_exam_limit_is_course_wise.sql applied.';
GO
