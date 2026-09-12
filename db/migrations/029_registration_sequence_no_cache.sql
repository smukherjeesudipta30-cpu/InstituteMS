/*
    029_registration_sequence_no_cache.sql
    -------------------------------------------------------------------------
    dbo.StudentRegistrationNumber issues the admission number that becomes both
    the registration id (AVI/{n}/{yy}) and the admission bill (S-A-{n}-{yy}).

    It was created with the default CACHE, which SQL Server sized at 10. A cached
    sequence hands out numbers from memory and only writes the position to disk
    once per block, so an unexpected restart discards whatever is left in the
    block. That is visible in the live data as student 4988 followed by 4998 --
    a jump of exactly the cache size.

    NO CACHE writes the position on every draw. At a few admissions a day the
    cost is irrelevant; a visible gap in the bill numbers is not.

    Note this does not make gaps impossible. A sequence value is never returned,
    so an admission that fails after the number is drawn still consumes it. That
    loses one number, not a block of ten.

    Idempotent: re-running it is harmless.
*/

IF EXISTS (SELECT 1 FROM sys.sequences WHERE name = 'StudentRegistrationNumber')
BEGIN
    ALTER SEQUENCE dbo.StudentRegistrationNumber NO CACHE;
    PRINT 'StudentRegistrationNumber set to NO CACHE.';
END
ELSE
BEGIN
    PRINT 'StudentRegistrationNumber not found - nothing to do.';
END
GO

/*
    Re-aligning the counters after a burnt number
    -------------------------------------------------------------------------
    Run this ONLY when the sequence has moved past the last student actually on
    file -- for example an admission that was begun and abandoned, which consumes
    both the sequence value and the table's identity.

    Two things must agree, or the next student gets a registration number that
    does not match their row id:

      * the sequence, which issues the registration and bill number
      * the identity on dbo.student, which issues the row id

    RESTART WITH <last used> followed by one draw is deliberate. Immediately after
    a RESTART, SQL Server issues the start value itself, but sys.sequences reports
    current_value as that same number -- so the application's "next registration
    id" preview, which reads current_value + 1, would be one ahead of reality.
    Drawing once puts the sequence in the state the preview assumes.

    Uncomment and set @last_used to the highest id in dbo.student.
*/

-- DECLARE @last_used int = (SELECT MAX(id) FROM dbo.student);
-- DECLARE @sql nvarchar(200) =
--     N'ALTER SEQUENCE dbo.StudentRegistrationNumber RESTART WITH ' + CONVERT(nvarchar(20), @last_used) + N';';
-- EXEC sp_executesql @sql;
-- DECLARE @burn int = NEXT VALUE FOR dbo.StudentRegistrationNumber;
-- DBCC CHECKIDENT ('dbo.student', RESEED, @last_used) WITH NO_INFOMSGS;
-- SELECT next_registration_will_be = CONVERT(int, current_value) + 1
-- FROM sys.sequences WHERE name = 'StudentRegistrationNumber';
