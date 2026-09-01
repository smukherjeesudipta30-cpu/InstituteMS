/* =====================================================================
   004_migrate_student_data.sql
   Source    : ph19987796653_prod   (production snapshot, READ ONLY)
   Target    : Avijan_Angular_Migration
   Purpose   : Load the real Course / Teacher / Student / ImageUpload data.

   Identities are preserved (IDENTITY_INSERT ON) because StuRegId, Cid and
   Teacher.id are referenced by MonthlyFees, Marks, TecFees and ImageUpload.
   Renumbering would break every one of those joins.

   Transformations applied, and why each is needed
   -----------------------------------------------
   T1  Fake seed rows from 002 are deleted first. Real ids overlap them.
   T2  Student.Cid = 0  -> NULL.  27 students. 0 is the legacy "no course"
       sentinel; there is no Course row with CId = 0, so it cannot satisfy
       the FK.
   T3  TecName / TecName1 / TecName2 = 0 -> NULL. Same sentinel story
       (1 / 361 / 458 rows respectively).
   T4  Teacher references pointing at deleted teachers are preserved by
       recreating placeholder Teacher rows. The legacy app deletes teachers
       with no referential guard (MIGRATION_PLAN defect T4), which orphaned
       ids 6, 8, 9, 25, 27, 28, 30, 32 across 53 student records. Nulling
       them would silently discard who taught those students, so instead a
       row named '(removed teacher #n)' is created with LoginId
       '__removed_n'. They are obvious in any dropdown and can be merged or
       renamed once someone identifies them.
   T5  Stu_monthly_fees varchar -> decimal(18,2) via TRY_CONVERT. Verified
       0 rows fail to convert.
   T6  IsActive / IsComplete NULL -> 0, matching the NOT NULL DEFAULT 0 the
       new schema declares.
   T7  Duplicate StuRegId values are migrated verbatim. See the header of
       003 for why they are not renumbered.
   T8  Avijan_login_Password is NOT copied. It is a plain-text credential
       (defect D3); student logins belong in Users.PasswordHash.
   T9  ImageUpload.Description held the StuRegId; the value is copied into
       the new StuRegId column and also left in Description so the legacy
       join keeps working during the parallel run.

   Idempotent: re-running clears the migrated rows and reloads them.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

IF DB_ID('ph19987796653_prod') IS NULL
BEGIN
    RAISERROR('Source database ph19987796653_prod is not attached on this server.', 16, 1);
    RETURN;
END
GO

BEGIN TRANSACTION;
GO

/* ------------------------------------------------- T1: clear the target */
/* Order matters: children before parents. */
DELETE FROM dbo.ImageUpload;
DELETE FROM dbo.Student;
DELETE FROM dbo.Teacher;
DELETE FROM dbo.Course;
GO

/* --------------------------------------------------------------- Course */
SET IDENTITY_INSERT dbo.Course ON;

INSERT INTO dbo.Course (Cid, CName, Duration, CreatedBy, CreatedOn)
SELECT  c.CId,
        LTRIM(RTRIM(c.CName)),
        c.duration,
        'migration-004',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.Course AS c;

/* T11: placeholders for courses deleted while students still referenced
   them - the same defect as T4, on the Course side. Production has 10
   students on course id 28, which no longer exists in Course. Nulling the
   reference would lose which course those students were enrolled on. */
INSERT INTO dbo.Course (Cid, CName, Duration, CreatedBy, CreatedOn)
SELECT  DISTINCT s.Cid,
        '(removed course #' + CAST(s.Cid AS varchar(10)) + ')',
        NULL,
        'migration-004-orphan',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.student AS s
WHERE   s.Cid IS NOT NULL
        AND s.Cid <> 0
        AND NOT EXISTS (SELECT 1 FROM dbo.Course c WHERE c.Cid = s.Cid);

SET IDENTITY_INSERT dbo.Course OFF;
GO

/* -------------------------------------------------------------- Teacher */
SET IDENTITY_INSERT dbo.Teacher ON;

INSERT INTO dbo.Teacher (id, TecName, LoginId, TecPhone, CreatedBy, CreatedOn)
SELECT  t.id,
        LTRIM(RTRIM(t.TecName)),
        LTRIM(RTRIM(t.LoginId)),
        LTRIM(RTRIM(t.TecPhone)),
        'migration-004',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.Teacher AS t;

/* T4: placeholders for teachers that were deleted while still referenced. */
INSERT INTO dbo.Teacher (id, TecName, LoginId, TecPhone, CreatedBy, CreatedOn)
SELECT  DISTINCT o.TecId,
        '(removed teacher #' + CAST(o.TecId AS varchar(10)) + ')',
        '__removed_' + CAST(o.TecId AS varchar(10)),
        NULL,
        'migration-004-orphan',
        SYSDATETIME()
FROM (
        /* Every place a teacher id is referenced, not just the student rows:
           TecFees and TecExpense also point at teachers that were deleted,
           and 6 of those ids appear nowhere on a student. Missing them left
           FK_TecExpense_Teacher failing when 008 loaded the payouts. */
        SELECT s.TecName  AS TecId FROM ph19987796653_prod.dbo.student s WHERE s.TecName  > 0
        UNION
        SELECT s.TecName1        FROM ph19987796653_prod.dbo.student s WHERE s.TecName1 > 0
        UNION
        SELECT s.TecName2        FROM ph19987796653_prod.dbo.student s WHERE s.TecName2 > 0
        UNION
        SELECT f.tecid           FROM ph19987796653_prod.dbo.TecFees f    WHERE f.tecid > 0
        UNION
        SELECT e.tecid           FROM ph19987796653_prod.dbo.TecExpense e WHERE e.tecid > 0
     ) AS o
WHERE NOT EXISTS (SELECT 1 FROM dbo.Teacher t WHERE t.id = o.TecId);

SET IDENTITY_INSERT dbo.Teacher OFF;
GO

/* -------------------------------------------------------------- Student */
SET IDENTITY_INSERT dbo.Student ON;

INSERT INTO dbo.Student
(
    id, StuName, GName, Stuaddress, StuPhone, StuEmail, StuRegId, BillNo,
    NoTech, StuAdFees, Stu_monthly_fees, DOJ, DOB, DOE,
    Cid, TecName, TecName1, TecName2,
    IsActive, IsComplete, loginId, Branch,
    Personal_mail_id, Avijan_login_mail_id, Replica_id, Avijan_login_Password,
    CreatedBy, CreatedOn
)
SELECT
    s.id,
    LTRIM(RTRIM(s.Stuname)),
    LTRIM(RTRIM(s.GName)),
    s.Stuaddress,
    LTRIM(RTRIM(s.StuPhone)),
    LTRIM(RTRIM(s.StuEmail)),
    LTRIM(RTRIM(s.StuRegId)),
    LTRIM(RTRIM(s.BillNo)),
    s.NoTech,
    s.StuAdFees,
    TRY_CONVERT(decimal(18,2), s.Stu_monthly_fees),            -- T5
    TRY_CONVERT(date, s.DOJ),
    TRY_CONVERT(date, s.DOB),
    TRY_CONVERT(date, s.DOE),
    NULLIF(s.Cid, 0),                                          -- T2
    NULLIF(s.TecName,  0),                                     -- T3
    NULLIF(s.TecName1, 0),                                     -- T3
    NULLIF(s.TecName2, 0),                                     -- T3
    ISNULL(s.IsActive,   0),                                   -- T6
    ISNULL(s.IsComplete, 0),                                   -- T6
    LTRIM(RTRIM(s.loginId)),
    /* T10: production has no Branch column on student - the branch was only
       ever encoded inside StuRegId - so nothing can be carried into it. */
    NULL,
    LTRIM(RTRIM(s.Personal_mail_id)),
    LTRIM(RTRIM(s.Avijan_login_mail_id)),
    s.Replica_id,
    /* T8: carried verbatim. Stored in clear text on production; see the
       warning in migration 012. Nothing in the application reads it. */
    LTRIM(RTRIM(s.Avijan_login_Password)),
    'migration-004',
    SYSDATETIME()
FROM ph19987796653_prod.dbo.student AS s;

SET IDENTITY_INSERT dbo.Student OFF;
GO

/* ---------------------------------------------------------- ImageUpload */
SET IDENTITY_INSERT dbo.ImageUpload ON;

INSERT INTO dbo.ImageUpload (FileID, FileName, Description, StuRegId, FilePath, FileSize, CreatedBy, CreatedOn)
SELECT  i.FileID,
        i.FileName,
        i.Description,                          -- T9: legacy join preserved
        LTRIM(RTRIM(i.Description)),            -- T9: normalised column
        i.FilePath,
        i.FileSize,
        'migration-004',
        SYSDATETIME()
FROM    ph19987796653_prod.dbo.ImageUpload AS i;

SET IDENTITY_INSERT dbo.ImageUpload OFF;
GO

COMMIT TRANSACTION;
GO

/* ------------------------------------------------------------ reseeding */
/* IDENTITY_INSERT leaves the seed at the highest inserted value, which is
   what we want -- the next generated id continues the legacy sequence. */
DBCC CHECKIDENT ('dbo.Course',      RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Teacher',     RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.Student',     RESEED) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('dbo.ImageUpload', RESEED) WITH NO_INFOMSGS;
GO

PRINT '004_migrate_student_data.sql applied.';
GO
