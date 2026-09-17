-- One row per browser/device that has agreed to receive a chat notification
-- through the browser's push service, even with no tab open at all. Endpoint
-- is unique per browser install (re-subscribing the same browser replaces
-- its row rather than duplicating it).
CREATE TABLE dbo.ChatPushSubscription (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Email NVARCHAR(150) NOT NULL,
    Endpoint NVARCHAR(500) NOT NULL,
    P256dh NVARCHAR(200) NOT NULL,
    Auth NVARCHAR(100) NOT NULL,
    CreatedOn DATETIME2 NOT NULL
);

CREATE UNIQUE INDEX IX_ChatPushSubscription_Endpoint ON dbo.ChatPushSubscription(Endpoint);
CREATE INDEX IX_ChatPushSubscription_Email ON dbo.ChatPushSubscription(Email);
