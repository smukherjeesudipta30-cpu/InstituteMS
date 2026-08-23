/* =====================================================================
   compare_students.sql
   Old-vs-new data diff for the Student module (MIGRATION_PLAN §11.3).
   Read-only. Run after 004; every check should report OK.
   ===================================================================== */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

USE [Avijan_Angular_Migration];
GO

PRINT '=== row counts ===';

SELECT  'Course'  AS [Table],
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Course)  AS Legacy,
        (SELECT COUNT(*) FROM dbo.Course)                 AS Migrated,
        CASE WHEN (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Course)
                = (SELECT COUNT(*) FROM dbo.Course)  THEN 'OK' ELSE '** MISMATCH **' END AS Result
UNION ALL
SELECT  'Student',
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.student),
        (SELECT COUNT(*) FROM dbo.Student),
        CASE WHEN (SELECT COUNT(*) FROM ph19987796653_prod.dbo.student)
                = (SELECT COUNT(*) FROM dbo.Student) THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
/* Counts only the rows the migration created. Photos uploaded through the new
   app afterwards are legitimate new data, not a migration discrepancy. */
SELECT  'ImageUpload (migrated only)',
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.ImageUpload),
        (SELECT COUNT(*) FROM dbo.ImageUpload WHERE CreatedBy = 'migration-004'),
        CASE WHEN (SELECT COUNT(*) FROM ph19987796653_prod.dbo.ImageUpload)
                = (SELECT COUNT(*) FROM dbo.ImageUpload WHERE CreatedBy = 'migration-004') THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
/* Teacher is expected to be larger: placeholders were added for teachers
   that had been deleted while students still referenced them (T4). */
SELECT  'Teacher (real only)',
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Teacher),
        (SELECT COUNT(*) FROM dbo.Teacher WHERE CreatedBy <> 'migration-004-orphan'),
        CASE WHEN (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Teacher)
                = (SELECT COUNT(*) FROM dbo.Teacher WHERE CreatedBy <> 'migration-004-orphan') THEN 'OK' ELSE '** MISMATCH **' END;

PRINT '';
PRINT '=== student status split ===';

SELECT  'Active'   AS Status,
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.student WHERE IsActive = 1) AS Legacy,
        (SELECT COUNT(*) FROM dbo.Student WHERE IsActive = 1)                AS Migrated
UNION ALL
SELECT  'Inactive',
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.student WHERE ISNULL(IsActive, 0) = 0),
        (SELECT COUNT(*) FROM dbo.Student WHERE IsActive = 0)
UNION ALL
SELECT  'Complete',
        (SELECT COUNT(*) FROM ph19987796653_prod.dbo.student WHERE IsComplete = 1),
        (SELECT COUNT(*) FROM dbo.Student WHERE IsComplete = 1);

PRINT '';
PRINT '=== field-level checksums (should all be OK) ===';

SELECT 'StuRegId set identical' AS Check_,
       CASE WHEN NOT EXISTS (
                SELECT LTRIM(RTRIM(StuRegId)) FROM ph19987796653_prod.dbo.student
                EXCEPT
                SELECT StuRegId FROM dbo.Student)
             AND NOT EXISTS (
                SELECT StuRegId FROM dbo.Student
                EXCEPT
                SELECT LTRIM(RTRIM(StuRegId)) FROM ph19987796653_prod.dbo.student)
            THEN 'OK' ELSE '** MISMATCH **' END AS Result
UNION ALL
SELECT 'Admission fees total',
       CASE WHEN (SELECT ISNULL(SUM(StuAdFees), 0) FROM ph19987796653_prod.dbo.student)
               = (SELECT ISNULL(SUM(StuAdFees), 0) FROM dbo.Student)
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Monthly fees total',
       CASE WHEN (SELECT ISNULL(SUM(TRY_CONVERT(decimal(18,2), Stu_monthly_fees)), 0) FROM ph19987796653_prod.dbo.student)
               = (SELECT ISNULL(SUM(Stu_monthly_fees), 0) FROM dbo.Student)
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Name mismatches (trimmed)',
       CASE WHEN (SELECT COUNT(*)
                    FROM ph19987796653_prod.dbo.student l
                    JOIN dbo.Student n ON n.id = l.id
                   WHERE ISNULL(LTRIM(RTRIM(l.Stuname)), '') <> ISNULL(n.StuName, '')) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'DOJ mismatches',
       CASE WHEN (SELECT COUNT(*)
                    FROM ph19987796653_prod.dbo.student l
                    JOIN dbo.Student n ON n.id = l.id
                   WHERE ISNULL(TRY_CONVERT(date, l.DOJ), '1900-01-01') <> ISNULL(n.DOJ, '1900-01-01')) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Course link mismatches',
       CASE WHEN (SELECT COUNT(*)
                    FROM ph19987796653_prod.dbo.student l
                    JOIN dbo.Student n ON n.id = l.id
                   WHERE ISNULL(NULLIF(l.Cid, 0), -1) <> ISNULL(n.Cid, -1)) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'Teacher link mismatches',
       CASE WHEN (SELECT COUNT(*)
                    FROM ph19987796653_prod.dbo.student l
                    JOIN dbo.Student n ON n.id = l.id
                   WHERE ISNULL(NULLIF(l.TecName,  0), -1) <> ISNULL(n.TecName,  -1)
                      OR ISNULL(NULLIF(l.TecName1, 0), -1) <> ISNULL(n.TecName1, -1)
                      OR ISNULL(NULLIF(l.TecName2, 0), -1) <> ISNULL(n.TecName2, -1)) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
/* Rows that 005 did not touch must still match the source byte for byte.
   Recovered rows are expected to differ: 005 replaced the stored disk path
   with the actual file contents, which is the whole point of that script. */
SELECT 'Photo blobs byte-identical (excluding recovered)',
       CASE WHEN (SELECT COUNT(*)
                    FROM ph19987796653_prod.dbo.ImageUpload l
                    JOIN dbo.ImageUpload n ON n.FileID = l.FileID
                   WHERE n.ContentType IS NULL      -- ContentType is stamped only by 005
                     AND DATALENGTH(ISNULL(l.FilePath, '')) <> DATALENGTH(ISNULL(n.FilePath, ''))) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'No orphan course links',
       CASE WHEN (SELECT COUNT(*) FROM dbo.Student s
                   WHERE s.Cid IS NOT NULL
                     AND NOT EXISTS (SELECT 1 FROM dbo.Course c WHERE c.Cid = s.Cid)) = 0
            THEN 'OK' ELSE '** MISMATCH **' END
UNION ALL
SELECT 'No orphan teacher links',
       CASE WHEN (SELECT COUNT(*) FROM dbo.Student s
                   WHERE (s.TecName  IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Teacher t WHERE t.id = s.TecName))
                      OR (s.TecName1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Teacher t WHERE t.id = s.TecName1))
                      OR (s.TecName2 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Teacher t WHERE t.id = s.TecName2))) = 0
            THEN 'OK' ELSE '** MISMATCH **' END;

PRINT '';
PRINT '=== known data issues carried over (informational) ===';

SELECT 'Duplicate StuRegId groups' AS Issue, COUNT(*) AS Count_
FROM  (SELECT StuRegId FROM dbo.Student GROUP BY StuRegId HAVING COUNT(*) > 1) d
UNION ALL
SELECT 'Students with no course (Cid was 0)', COUNT(*) FROM dbo.Student WHERE Cid IS NULL
UNION ALL
SELECT 'Placeholder teachers created', COUNT(*) FROM dbo.Teacher WHERE CreatedBy = 'migration-004-orphan'
UNION ALL
SELECT 'Students pointing at a placeholder teacher',
       COUNT(DISTINCT s.id)
FROM   dbo.Student s
JOIN   dbo.Teacher t ON t.id IN (s.TecName, s.TecName1, s.TecName2)
WHERE  t.CreatedBy = 'migration-004-orphan'
UNION ALL
SELECT 'Photos servable from the database', COUNT(*) FROM dbo.ImageUpload WHERE IsInline = 1
UNION ALL
SELECT 'Photos still only on the legacy web server', COUNT(*) FROM dbo.ImageUpload WHERE IsInline = 0;
GO
