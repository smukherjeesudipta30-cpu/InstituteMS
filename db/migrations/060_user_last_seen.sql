-- When a person's chat connection was last live, updated as they connect and
-- again as they disconnect, so an offline contact shows as "last seen …".
-- Distinct from LastLoginOn (the sign-in event). Null until their first
-- connection after this ships.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Users') AND name = 'LastSeenOn'
)
    ALTER TABLE dbo.Users ADD LastSeenOn DATETIME2 NULL;
