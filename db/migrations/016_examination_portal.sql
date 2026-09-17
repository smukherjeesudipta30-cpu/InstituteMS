/* =====================================================================
   016_examination_portal.sql
   ---------------------------------------------------------------------
   The online MCQ examination portal.

   THREE tables, and the question data lives in exactly one of them.

       ExamPaper     one row per exam            (what is being sat)
       ExamQuestion  one row per question        (ALL question data)
       ExamAttempt   one row per student sitting (who sat it, and the score)

   ExamQuestion is deliberately DENORMALISED: the four options, the
   correct answer and the mark all sit on the question row, the same
   shape dbo.Marks uses for its four subject slots. There is no separate
   option table and no separate answer-key table, so migrating questions
   to production is a single-table copy - which is what was asked for.

   The student's chosen answers are held on ExamAttempt in one
   'Answers' column as "questionId:option" pairs rather than a fourth
   table, for the same reason. The score is stored alongside so a result
   never has to be recomputed from a question set that may since have
   been edited.

   Marks per paper are free: 25, 40, 50, 100 or anything else. Each
   question carries its own mark and the paper's TotalMarks is the sum,
   checked when the questions are uploaded.

   Run:  sqlcmd -S . -I -i migrations\016_examination_portal.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

/* ---------------------------------------------------------- ExamPaper */
IF OBJECT_ID('dbo.ExamPaper', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamPaper
    (
        ExamId        int            NOT NULL IDENTITY(1,1),

        Title         varchar(200)   NOT NULL,
        /* The theory subject this paper examines - the practical paper is
           marked by hand on the Marks screen, not sat online. */
        Subject       varchar(100)   NULL,
        Cid           int            NULL,

        TotalMarks    int            NOT NULL,
        PassMarks     int            NULL,
        DurationMins  int            NOT NULL,

        /* The window the paper can be sat in. Null means always open. */
        OpensOn       datetime2(0)   NULL,
        ClosesOn      datetime2(0)   NULL,

        IsPublished   bit            NOT NULL CONSTRAINT DF_ExamPaper_IsPublished DEFAULT (0),

        CreatedBy     varchar(50)    NULL,
        CreatedOn     datetime2(0)   NOT NULL CONSTRAINT DF_ExamPaper_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy    varchar(50)    NULL,
        ModifiedOn    datetime2(0)   NULL,

        CONSTRAINT PK_ExamPaper PRIMARY KEY CLUSTERED (ExamId),
        CONSTRAINT FK_ExamPaper_Course FOREIGN KEY (Cid) REFERENCES dbo.Course (Cid),
        CONSTRAINT CK_ExamPaper_TotalMarks CHECK (TotalMarks > 0),
        CONSTRAINT CK_ExamPaper_Duration  CHECK (DurationMins > 0)
    );

    CREATE INDEX IX_ExamPaper_Published ON dbo.ExamPaper (IsPublished, ClosesOn);
    PRINT '016: ExamPaper created.';
END
GO

/* ------------------------------------------------------- ExamQuestion
   The single table holding every piece of question data.               */
IF OBJECT_ID('dbo.ExamQuestion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamQuestion
    (
        QuestionId    int            NOT NULL IDENTITY(1,1),
        ExamId        int            NOT NULL,

        /* Order shown to the student; also the row number in the upload. */
        QuestionNo    int            NOT NULL,
        QuestionText  varchar(max)   NOT NULL,

        OptionA       varchar(500)   NOT NULL,
        OptionB       varchar(500)   NOT NULL,
        OptionC       varchar(500)   NULL,
        OptionD       varchar(500)   NULL,

        /* 'A' | 'B' | 'C' | 'D'. Never sent to the student's browser. */
        CorrectOption char(1)        NOT NULL,

        Marks         int            NOT NULL CONSTRAINT DF_ExamQuestion_Marks DEFAULT (1),

        CreatedBy     varchar(50)    NULL,
        CreatedOn     datetime2(0)   NOT NULL CONSTRAINT DF_ExamQuestion_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_ExamQuestion PRIMARY KEY CLUSTERED (QuestionId),
        CONSTRAINT FK_ExamQuestion_Paper FOREIGN KEY (ExamId)
            REFERENCES dbo.ExamPaper (ExamId) ON DELETE CASCADE,
        CONSTRAINT CK_ExamQuestion_Correct CHECK (CorrectOption IN ('A','B','C','D')),
        CONSTRAINT CK_ExamQuestion_Marks CHECK (Marks > 0),
        CONSTRAINT UQ_ExamQuestion_No UNIQUE (ExamId, QuestionNo)
    );

    PRINT '016: ExamQuestion created.';
END
GO

/* -------------------------------------------------------- ExamAttempt */
IF OBJECT_ID('dbo.ExamAttempt', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamAttempt
    (
        AttemptId     int            NOT NULL IDENTITY(1,1),
        ExamId        int            NOT NULL,
        StuRegId      varchar(15)    NOT NULL,

        StartedOn     datetime2(0)   NOT NULL,
        /* Start + the paper's duration, fixed when the attempt opens so a
           reload cannot buy more time. */
        DueOn         datetime2(0)   NOT NULL,
        SubmittedOn   datetime2(0)   NULL,

        /* "12:A,13:C,14:B" - the student's choices. Kept here rather than
           in a fourth table so an attempt is one row to migrate. */
        Answers       varchar(max)   NULL,

        Score         int            NULL,
        TotalMarks    int            NULL,
        CorrectCount  int            NULL,
        QuestionCount int            NULL,

        /* True when the server closed it on time rather than the student
           pressing submit. */
        AutoSubmitted bit            NOT NULL CONSTRAINT DF_ExamAttempt_Auto DEFAULT (0),

        CreatedOn     datetime2(0)   NOT NULL CONSTRAINT DF_ExamAttempt_CreatedOn DEFAULT (SYSDATETIME()),

        CONSTRAINT PK_ExamAttempt PRIMARY KEY CLUSTERED (AttemptId),
        CONSTRAINT FK_ExamAttempt_Paper FOREIGN KEY (ExamId) REFERENCES dbo.ExamPaper (ExamId),
        /* One sitting per student per paper. */
        CONSTRAINT UQ_ExamAttempt_Student UNIQUE (ExamId, StuRegId)
    );

    CREATE INDEX IX_ExamAttempt_Student ON dbo.ExamAttempt (StuRegId, SubmittedOn DESC);
    PRINT '016: ExamAttempt created.';
END
GO

PRINT '016_examination_portal.sql applied.';
GO
