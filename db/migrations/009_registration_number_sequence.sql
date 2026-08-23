/* =====================================================================
   009_registration_number_sequence.sql
   Target DB : Avijan_Angular_Migration
   Purpose   : Give registration numbers their own sequence instead of
               deriving them from Student.id.

   Why
   ---
   The legacy generator took "SELECT TOP 1 id FROM Student WHERE IsActive=1
   ORDER BY id DESC" and added one (Models/Student.cs:108-146). That has
   two problems beyond the missing lock (defect D4):

     1. It follows the identity column, and IDENTITY leaves gaps -- a
        rolled-back insert or a service restart burns values. Observed
        live: a row landed on id 3477 while the number issued was 3463.
     2. It compares whole strings, so "AVI/3463/26" and "AVI/Bar/3463/26"
        look different even though they are the same number. Once the
        branch segment was introduced the same number could be handed out
        twice.

   A SEQUENCE fixes both: it is transactional, contiguous, cheap, and it
   is independent of the identity column. MIGRATION_PLAN §10 D4 lists
   "a sequence table" as the intended remedy.

   The sequence starts above the highest number already in use, parsed out
   of the existing StuRegId values in every format the data contains:
       AVI/3462/26      -> 3462   (2 slashes)
       AVI/Raj/3462/26  -> 3462   (3 slashes, branch segment)
       AVI//Raj/3455/2  -> 3455   (malformed, empty segment)
       3457             -> 3457   (bare number)
   In each case the number is the second-to-last '/' segment, or the whole
   value when there are no slashes.

   Idempotent: safe to re-run; the sequence is recreated at the right spot.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE [Avijan_Angular_Migration];
GO

DECLARE @maxNumber int;

;WITH parsed AS
(
    SELECT
        s.StuRegId,
        /* Work on the reversed string so the trailing segments are easy to find. */
        r         = REVERSE(LTRIM(RTRIM(s.StuRegId))),
        slashes   = LEN(s.StuRegId) - LEN(REPLACE(s.StuRegId, '/', ''))
    FROM dbo.Student s
    WHERE s.StuRegId IS NOT NULL AND LTRIM(RTRIM(s.StuRegId)) <> ''
),
segments AS
(
    SELECT
        p.StuRegId,
        Number = CASE
                    WHEN p.slashes = 0
                        THEN p.StuRegId                                   -- bare number
                    ELSE REVERSE(
                            SUBSTRING(
                                p.r,
                                CHARINDEX('/', p.r) + 1,
                                CASE
                                    WHEN CHARINDEX('/', p.r, CHARINDEX('/', p.r) + 1) = 0
                                        THEN LEN(p.r)
                                    ELSE CHARINDEX('/', p.r, CHARINDEX('/', p.r) + 1)
                                         - CHARINDEX('/', p.r) - 1
                                END))
                 END
    FROM parsed p
)
SELECT @maxNumber = MAX(TRY_CONVERT(int, Number))
FROM   segments;

/* Never start below the identity high-water mark, so a new number can never
   look older than an existing student row. */
DECLARE @maxId int = (SELECT ISNULL(MAX(id), 0) FROM dbo.Student);
DECLARE @start int = 1 + (SELECT CASE WHEN ISNULL(@maxNumber, 0) > @maxId
                                      THEN ISNULL(@maxNumber, 0) ELSE @maxId END);

IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'StudentRegistrationNumber')
    DROP SEQUENCE dbo.StudentRegistrationNumber;

DECLARE @sql nvarchar(400) =
    N'CREATE SEQUENCE dbo.StudentRegistrationNumber AS int START WITH '
    + CAST(@start AS nvarchar(20)) + N' INCREMENT BY 1 NO CYCLE CACHE 10;';

EXEC sp_executesql @sql;

PRINT '009_registration_number_sequence.sql applied.';
PRINT '  highest number already in use : ' + CAST(ISNULL(@maxNumber, 0) AS varchar(20));
PRINT '  highest student id            : ' + CAST(@maxId AS varchar(20));
PRINT '  sequence starts at            : ' + CAST(@start AS varchar(20));
GO
