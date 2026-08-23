/* =====================================================================
   compare_academics.sql -- Course, Marks and Notice fidelity check.

   These three tables were loaded by migration 004 (Course) and
   migration 008 (Marks, Notice) with IDENTITY_INSERT on, so the check is
   row-for-row on the key, not just a count. Every result set below must
   come back empty apart from the first, which must show Legacy = Migrated.

   Run:  sqlcmd -S . -I -i verify\compare_academics.sql -W -s"|"
   ===================================================================== */
SET NOCOUNT ON;

PRINT '--- 1. Row counts (Legacy must equal Migrated) ---';
SELECT 'Course' AS Entity,
       (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Course) AS Legacy,
       (SELECT COUNT(*) FROM Avijan_Angular_Migration.dbo.Course) AS Migrated
UNION ALL SELECT 'Marks',
       (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Marks),
       (SELECT COUNT(*) FROM Avijan_Angular_Migration.dbo.Marks)
UNION ALL SELECT 'Notice',
       (SELECT COUNT(*) FROM ph19987796653_prod.dbo.Notice),
       (SELECT COUNT(*) FROM Avijan_Angular_Migration.dbo.Notice);

PRINT '--- 2. Course rows that differ (must be empty) ---';
SELECT  l.CId, l.CName AS LegacyName, m.CName AS MigratedName,
        l.duration AS LegacyDuration, m.Duration AS MigratedDuration
FROM    ph19987796653_prod.dbo.Course AS l
FULL JOIN Avijan_Angular_Migration.dbo.Course AS m ON m.Cid = l.CId
WHERE   l.CId IS NULL OR m.Cid IS NULL
        OR ISNULL(l.CName, '') <> ISNULL(m.CName, '')
        OR ISNULL(l.duration, -1)   <> ISNULL(m.Duration, -1);

PRINT '--- 3. Marks rows that differ (must be empty) ---';
/* All four Sub/M/OM triples plus the registration id and exam date. */
SELECT  ISNULL(l.id, m.id) AS MarkId,
        l.Regid AS LegacyReg, m.RegId AS MigratedReg
FROM    ph19987796653_prod.dbo.Marks AS l
FULL JOIN Avijan_Angular_Migration.dbo.Marks AS m ON m.id = l.id
WHERE   l.id IS NULL OR m.id IS NULL
        OR ISNULL(l.Regid, '') <> ISNULL(m.RegId, '')
        OR ISNULL(CONVERT(date, l.ExDate), '19000101') <> ISNULL(CONVERT(date, m.ExDate), '19000101')
        OR ISNULL(l.Sub1, '') <> ISNULL(m.Sub1, '') OR ISNULL(l.M1, -1) <> ISNULL(m.M1, -1) OR ISNULL(l.OM1, -1) <> ISNULL(m.OM1, -1)
        OR ISNULL(l.Sub2, '') <> ISNULL(m.Sub2, '') OR ISNULL(l.M2, -1) <> ISNULL(m.M2, -1) OR ISNULL(l.OM2, -1) <> ISNULL(m.OM2, -1)
        OR ISNULL(l.Sub3, '') <> ISNULL(m.Sub3, '') OR ISNULL(l.M3, -1) <> ISNULL(m.M3, -1) OR ISNULL(l.OM3, -1) <> ISNULL(m.OM3, -1)
        OR ISNULL(l.Sub4, '') <> ISNULL(m.Sub4, '') OR ISNULL(l.M4, -1) <> ISNULL(m.M4, -1) OR ISNULL(l.OM4, -1) <> ISNULL(m.OM4, -1);

PRINT '--- 4. Notice rows that differ (must be empty) ---';
SELECT  ISNULL(l.NID, m.NID) AS NID, l.NewNotice AS LegacyText, m.NewNotice AS MigratedText
FROM    ph19987796653_prod.dbo.Notice AS l
FULL JOIN Avijan_Angular_Migration.dbo.Notice AS m ON m.NID = l.NID
WHERE   l.NID IS NULL OR m.NID IS NULL
        OR ISNULL(l.NewNotice, '') <> ISNULL(m.NewNotice, '')
        OR ISNULL(CONVERT(datetime2(0), l.NoticeDate), '19000101') <> ISNULL(m.NoticeDate, '19000101');

PRINT '--- 5. Marks orphaned from Student (informational) ---';
/* A mark whose StuRegId no longer matches a student. The legacy app had no
   foreign key here, so these are pre-existing, not migration damage. */
SELECT  m.id AS MarkId, m.RegId
FROM    Avijan_Angular_Migration.dbo.Marks AS m
WHERE   NOT EXISTS (SELECT 1 FROM Avijan_Angular_Migration.dbo.Student AS s
                    WHERE s.StuRegId = m.RegId);

PRINT '--- 6. Marks where obtained exceeds full (must be empty) ---';
/* Confirms M = full marks and OM = obtained, the way the entities are named. */
SELECT  id AS MarkId, Sub1, M1, OM1, Sub2, M2, OM2, Sub3, M3, OM3, Sub4, M4, OM4
FROM    Avijan_Angular_Migration.dbo.Marks
WHERE   OM1 > M1 OR OM2 > M2 OR OM3 > M3 OR OM4 > M4;

PRINT '--- 7. Courses referenced by students but missing (must be empty) ---';
SELECT DISTINCT s.Cid
FROM    Avijan_Angular_Migration.dbo.Student AS s
WHERE   s.Cid IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM Avijan_Angular_Migration.dbo.Course AS c WHERE c.Cid = s.Cid);
