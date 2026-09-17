/* =====================================================================
   020_exam_schedule_and_access_code.sql
   ---------------------------------------------------------------------
   Per-student exam scheduling, and the code that opens a theory paper.

   Until now a paper had one window (ExamPaper.OpensOn/ClosesOn) that
   applied to everyone on the course. The institute sits students in
   batches, so the date and time are now set PER STUDENT, and two things
   hang off that one row:

     1. The ADMIT CARD. A student can download one only once they have
        been scheduled - the card prints the date and time from here, in
        the Theory/Practical table the legacy card already had.

     2. The ACCESS CODE. Six digits, generated per student per paper and
        shown only to the administrator. The student signs in with their
        own credentials AND types the code the invigilator reads out;
        without it the theory paper does not open. Credentials alone are
        no longer enough, so a student cannot sit the paper early, from
        home, or on someone else's behalf.

   ONE table, keyed one row per (paper, student), so scheduling a batch
   is a set of rows and migrating it is a single-table copy - the same
   rule the rest of the portal follows.

   A PRACTICAL paper is scheduled here too. It is not sat online, so it
   never needs a code; the row exists so the admit card can print the
   practical date and time beside the theory one, as the legacy card
   laid out.

   Run:  sqlcmd -S . -I -i migrations\020_exam_schedule_and_access_code.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

IF OBJECT_ID('dbo.ExamSchedule', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamSchedule
    (
        ScheduleId    int            NOT NULL IDENTITY(1,1),

        ExamId        int            NOT NULL,

        /* The student's registration id, the key the rest of the system
           carries a student by (dbo.Student.StuRegId). */
        StuRegId      varchar(50)    NOT NULL,

        /* When this student sits this paper. Printed on the admit card,
           and the paper will not open before it. */
        ScheduledOn   datetime2(0)   NOT NULL,

        /* Six digits, held as text so a leading zero survives. NULL on a
           practical, which is examined in the room and needs no code. */
        AccessCode    char(6)        NULL,

        /* Re-issued if a code is overheard; the old one stops working the
           moment this is overwritten. */
        CodeIssuedOn  datetime2(0)   NULL,
        CodeIssuedBy  varchar(50)    NULL,

        CreatedBy     varchar(50)    NULL,
        CreatedOn     datetime2(0)   NOT NULL
                      CONSTRAINT DF_ExamSchedule_CreatedOn DEFAULT (SYSDATETIME()),
        ModifiedBy    varchar(50)    NULL,
        ModifiedOn    datetime2(0)   NULL,

        CONSTRAINT PK_ExamSchedule PRIMARY KEY CLUSTERED (ScheduleId),

        CONSTRAINT FK_ExamSchedule_ExamPaper FOREIGN KEY (ExamId)
            REFERENCES dbo.ExamPaper (ExamId) ON DELETE CASCADE,

        /* One sitting per student per paper. Re-scheduling updates the row
           rather than adding a second, so there is never a question of
           which date and which code are the live ones. */
        CONSTRAINT UQ_ExamSchedule_ExamStudent UNIQUE (ExamId, StuRegId),

        /* Six digits, nothing else. */
        CONSTRAINT CK_ExamSchedule_AccessCode
            CHECK (AccessCode IS NULL OR AccessCode LIKE '[0-9][0-9][0-9][0-9][0-9][0-9]')
    );

    PRINT 'Created dbo.ExamSchedule.';
END
ELSE
    PRINT 'dbo.ExamSchedule already exists - left alone.';
GO

/* Looking a student's schedule up by registration id is the student
   portal's hot path: "what may I sit, and what does my card say?" */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_ExamSchedule_StuRegId'
                 AND object_id = OBJECT_ID('dbo.ExamSchedule'))
BEGIN
    CREATE INDEX IX_ExamSchedule_StuRegId
        ON dbo.ExamSchedule (StuRegId) INCLUDE (ExamId, ScheduledOn);

    PRINT 'Created IX_ExamSchedule_StuRegId.';
END
GO

PRINT '020 complete.';
GO
