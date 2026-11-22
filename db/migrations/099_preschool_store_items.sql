-- Avijan PreSchool store: the four things every class keeps.
--
--   1 Tuition Kit       (was "<class> tuition book set" from migration 093)
--   2 School Bag        (was "<class> school bag")
--   3 Winter Garments
--   4 Prospectus
--
-- Each class's card on the Store screen carries the class name, so the items
-- are named plainly. Only the items migration 093 created are renamed; any the
-- office added or renamed itself are left alone. Stock and items already given
-- to children stay with their item.
--
-- Idempotent: safe to run more than once.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

UPDATE dbo.PreSchoolStoreItem
SET Name = N'Tuition Kit', SortOrder = c.SortOrder * 10 + 1, ModifiedBy = 'migration 099', ModifiedOn = SYSDATETIME()
FROM dbo.PreSchoolStoreItem i
JOIN dbo.PreSchoolClass c ON c.Id = i.ClassId
WHERE i.CreatedBy = 'migration 093' AND i.Name = c.Name + N' tuition book set';

UPDATE dbo.PreSchoolStoreItem
SET Name = N'School Bag', SortOrder = c.SortOrder * 10 + 2, ModifiedBy = 'migration 099', ModifiedOn = SYSDATETIME()
FROM dbo.PreSchoolStoreItem i
JOIN dbo.PreSchoolClass c ON c.Id = i.ClassId
WHERE i.CreatedBy = 'migration 093' AND i.Name = c.Name + N' school bag';
GO

-- Every active class gets each of the four it does not have yet (by name).
INSERT dbo.PreSchoolStoreItem (Name, ClassId, SortOrder, CreatedBy)
SELECT x.Name, c.Id, c.SortOrder * 10 + x.Slot, 'migration 099'
FROM dbo.PreSchoolClass c
CROSS JOIN (VALUES (1, N'Tuition Kit'), (2, N'School Bag'), (3, N'Winter Garments'), (4, N'Prospectus')) x (Slot, Name)
WHERE c.IsActive = 1
  AND NOT EXISTS (SELECT 1 FROM dbo.PreSchoolStoreItem i WHERE i.ClassId = c.Id AND i.Name = x.Name);
GO
