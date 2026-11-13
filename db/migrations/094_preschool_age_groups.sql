-- Avijan PreSchool: the classes and age groups the owner set.
--
--   Pre-Nursery  2 years to under 3   (24 to 35 months)
--   Nursery      3+                   (36 to 47 months)
--   LKG          4+                   (48 to 59 months)
--   UKG          5+                   (60 to 71 months)
--
-- The class names follow My Chhota School, which Avijan PreSchool is powered
-- by: Pre-Nursery, Nursery, LKG, UKG. Renames KG-1 and KG-2 (and a "Play
-- Group" an earlier copy of this script made) in place — same rows, so admitted
-- children, fees, subjects and store items stay with their class — and the
-- store items named after them. The age limits are set only where none has
-- been set yet, so a limit the PreSchool admin changed later is never put back.
-- The limits are checked on the joining date, at admission only.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

UPDATE dbo.PreSchoolClass SET Name = N'Pre-Nursery', ModifiedBy = 'migration 094', ModifiedOn = SYSDATETIME()
WHERE Name = N'Play Group' AND NOT EXISTS (SELECT 1 FROM dbo.PreSchoolClass WHERE Name = N'Pre-Nursery');
UPDATE dbo.PreSchoolClass SET Name = N'LKG', ModifiedBy = 'migration 094', ModifiedOn = SYSDATETIME()
WHERE Name = N'KG-1' AND NOT EXISTS (SELECT 1 FROM dbo.PreSchoolClass WHERE Name = N'LKG');
UPDATE dbo.PreSchoolClass SET Name = N'UKG', ModifiedBy = 'migration 094', ModifiedOn = SYSDATETIME()
WHERE Name = N'KG-2' AND NOT EXISTS (SELECT 1 FROM dbo.PreSchoolClass WHERE Name = N'UKG');
GO

UPDATE c SET MinAgeMonths = v.MinAge, MaxAgeMonths = v.MaxAge
FROM dbo.PreSchoolClass c
JOIN (VALUES (N'Pre-Nursery', 24, 35), (N'Nursery', 36, 47), (N'LKG', 48, 59), (N'UKG', 60, 71)) AS v (Name, MinAge, MaxAge)
  ON v.Name = c.Name
WHERE c.MinAgeMonths IS NULL AND c.MaxAgeMonths IS NULL;
GO

-- Store items seeded by migration 093 carry the old class name at the front.
IF OBJECT_ID('dbo.PreSchoolStoreItem') IS NOT NULL
BEGIN
    UPDATE dbo.PreSchoolStoreItem SET Name = N'Pre-Nursery' + SUBSTRING(Name, LEN(N'Play Group') + 1, 100) WHERE Name LIKE N'Play Group %';
    UPDATE dbo.PreSchoolStoreItem SET Name = N'LKG' + SUBSTRING(Name, LEN(N'KG-1') + 1, 100) WHERE Name LIKE N'KG-1 %';
    UPDATE dbo.PreSchoolStoreItem SET Name = N'UKG' + SUBSTRING(Name, LEN(N'KG-2') + 1, 100) WHERE Name LIKE N'KG-2 %';
END
GO

-- The PreSchool's email is avijanpreschool@gmail.com. Replaces only the
-- address an earlier copy of migration 092 filled in, never one the PreSchool
-- admin or receptionist chose since.
UPDATE dbo.PreSchoolSetting SET Email = N'avijanpreschool@gmail.com'
WHERE Id = 1 AND (Email IS NULL OR Email = N'' OR Email = N'mail.avijan@gmail.com');
GO
