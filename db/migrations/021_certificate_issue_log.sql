/* =====================================================================
   021_certificate_issue_log.sql
   ---------------------------------------------------------------------
   Every certificate handed out, numbered.

   A certificate is a document the institute vouches for, so the first
   one a student takes is the original and every one after it is a
   DUPLICATE COPY, marked as such on its face and carrying its own copy
   number. That is how printed certificates have always worked: an
   unmarked second copy is indistinguishable from the original, and two
   "originals" in circulation is exactly what the marking prevents.

   ONE row per issue rather than a counter column on Student, because
   the number is not the only thing worth keeping - "when was the second
   copy taken, and to which sign-in" is the question the office actually
   gets asked. A counter cannot answer it.

   IssueNo is per student: 1 is the original, 2 and up are duplicates.

   Run:  sqlcmd -S . -I -i migrations\021_certificate_issue_log.sql
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
USE Avijan_Angular_Migration;
GO

IF OBJECT_ID('dbo.CertificateIssue', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CertificateIssue
    (
        IssueId    int            NOT NULL IDENTITY(1,1),

        /* dbo.Student.StuRegId - the key a student is carried by. */
        StuRegId   varchar(50)    NOT NULL,

        /* 1 = the original. 2 and above are duplicates, and the printed
           certificate says so. */
        IssueNo    int            NOT NULL,

        IssuedOn   datetime2(0)   NOT NULL
                   CONSTRAINT DF_CertificateIssue_IssuedOn DEFAULT (SYSDATETIME()),

        /* The sign-in that took it. A student's own certificate can only
           be taken by that student, so this is normally their own login -
           it is recorded so the office can say who, not just when. */
        IssuedTo   varchar(200)   NULL,

        CONSTRAINT PK_CertificateIssue PRIMARY KEY CLUSTERED (IssueId),

        /* Two copies can never share a number for one student. */
        CONSTRAINT UQ_CertificateIssue_StudentIssueNo UNIQUE (StuRegId, IssueNo),

        CONSTRAINT CK_CertificateIssue_IssueNo CHECK (IssueNo >= 1)
    );

    PRINT 'Created dbo.CertificateIssue.';
END
ELSE
    PRINT 'dbo.CertificateIssue already exists - left alone.';
GO

/* "How many has this student taken, and when?" is the only read. */
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = 'IX_CertificateIssue_StuRegId'
                 AND object_id = OBJECT_ID('dbo.CertificateIssue'))
BEGIN
    CREATE INDEX IX_CertificateIssue_StuRegId
        ON dbo.CertificateIssue (StuRegId) INCLUDE (IssueNo, IssuedOn);

    PRINT 'Created IX_CertificateIssue_StuRegId.';
END
GO

PRINT '021 complete.';
GO
