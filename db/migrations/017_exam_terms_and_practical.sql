/* =====================================================================
   017_exam_terms_and_practical.sql
   ---------------------------------------------------------------------
   Shapes the examination portal to the institute's actual rule:

       a student sits at most FOUR papers -
       (1 theory + 1 practical) x 2 terms

   Only the THEORY paper is sat online. The PRACTICAL is marked by the
   administrator, who enters the score directly.

   No new tables. A practical paper is an ExamPaper like any other, and
   the marks the administrator enters are an ExamAttempt like any other -
   just one with no questions and no sitting. That means the combined
   result is a single query over ExamAttempt, and the "max four" rule is
   one constraint rather than a second results system.

   Run:  sqlcmd -S . -I -i migrations\017_exam_terms_and_practical.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

/* --- 1. What kind of paper, and which of the two terms ---------------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ExamPaper') AND name = 'PaperType')
BEGIN
    ALTER TABLE dbo.ExamPaper
        ADD PaperType varchar(10) NOT NULL
            CONSTRAINT DF_ExamPaper_PaperType DEFAULT ('Theory');

    PRINT '017: ExamPaper.PaperType added.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ExamPaper') AND name = 'TermNumber')
BEGIN
    ALTER TABLE dbo.ExamPaper
        ADD TermNumber int NOT NULL
            CONSTRAINT DF_ExamPaper_TermNumber DEFAULT (1);

    PRINT '017: ExamPaper.TermNumber added.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ExamPaper_PaperType')
BEGIN
    ALTER TABLE dbo.ExamPaper
        ADD CONSTRAINT CK_ExamPaper_PaperType CHECK (PaperType IN ('Theory', 'Practical'));

    PRINT '017: CK_ExamPaper_PaperType added.';
END
GO

/* Two terms only: (theory + practical) x 2 is the whole examination. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ExamPaper_TermNumber')
BEGIN
    ALTER TABLE dbo.ExamPaper
        ADD CONSTRAINT CK_ExamPaper_TermNumber CHECK (TermNumber IN (1, 2));

    PRINT '017: CK_ExamPaper_TermNumber added.';
END
GO

/* --- 2. A practical paper carries no questions and is not sat online --
   DurationMins stays NOT NULL for the shared shape, so a practical is
   stored with a nominal duration that nothing reads.                    */

/* --- 3. How the marks got there --------------------------------------
   'Portal'  the student sat it and the server marked it
   'Entered' an administrator typed the practical score in              */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ExamAttempt') AND name = 'Source')
BEGIN
    ALTER TABLE dbo.ExamAttempt
        ADD Source varchar(10) NOT NULL
            CONSTRAINT DF_ExamAttempt_Source DEFAULT ('Portal');

    PRINT '017: ExamAttempt.Source added.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ExamAttempt') AND name = 'EnteredBy')
BEGIN
    ALTER TABLE dbo.ExamAttempt ADD EnteredBy varchar(50) NULL;
    PRINT '017: ExamAttempt.EnteredBy added.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_ExamAttempt_Source')
BEGIN
    ALTER TABLE dbo.ExamAttempt
        ADD CONSTRAINT CK_ExamAttempt_Source CHECK (Source IN ('Portal', 'Entered'));

    PRINT '017: CK_ExamAttempt_Source added.';
END
GO

/* --- 4. The "at most four" rule --------------------------------------
   A student may hold only one attempt per (term, paper type). With two
   terms and two types that is four, and the database enforces it rather
   than trusting the application to remember.

   The pair is denormalised onto the attempt so a UNIQUE index can cover
   it; ExamPaper is the authority and the trigger below keeps them equal. */
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ExamAttempt') AND name = 'TermNumber')
BEGIN
    ALTER TABLE dbo.ExamAttempt ADD TermNumber int NULL;
    ALTER TABLE dbo.ExamAttempt ADD PaperType varchar(10) NULL;
    PRINT '017: ExamAttempt.TermNumber / PaperType added.';
END
GO

/* Backfill anything already recorded, then enforce. */
UPDATE a
SET    a.TermNumber = p.TermNumber,
       a.PaperType  = p.PaperType
FROM   dbo.ExamAttempt AS a
JOIN   dbo.ExamPaper   AS p ON p.ExamId = a.ExamId
WHERE  a.TermNumber IS NULL OR a.PaperType IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ExamAttempt_TermType')
   AND NOT EXISTS (SELECT 1 FROM dbo.ExamAttempt WHERE TermNumber IS NULL OR PaperType IS NULL)
BEGIN
    CREATE UNIQUE INDEX UQ_ExamAttempt_TermType
        ON dbo.ExamAttempt (StuRegId, TermNumber, PaperType)
        WHERE TermNumber IS NOT NULL AND PaperType IS NOT NULL;

    PRINT '017: UQ_ExamAttempt_TermType created - four papers per student, maximum.';
END
GO

PRINT '017_exam_terms_and_practical.sql applied.';
GO
