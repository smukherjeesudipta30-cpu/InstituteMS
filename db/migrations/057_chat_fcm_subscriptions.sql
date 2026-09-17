SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    057_chat_fcm_subscriptions.sql
    -------------------------------------------------------------------------
    Device push for the installed Android app. dbo.ChatPushSubscription
    (migration 056) holds a browser's Web Push coordinates -- Endpoint,
    P256dh, Auth -- which mean nothing to Firebase Cloud Messaging, the
    mechanism the Android app actually uses. Rather than widen that table
    with nullable, provider-specific columns, FCM gets its own table: one row
    per installed device, keyed by its current registration token.

    Re-registering (a token Firebase reissues, or simply opening the app
    again) replaces the existing row for that token rather than accumulating
    duplicates -- see the application-side upsert-by-Token logic.
*/

IF OBJECT_ID('dbo.ChatFcmSubscription', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatFcmSubscription
    (
        Id        int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatFcmSubscription PRIMARY KEY,
        Email     varchar(150)  NOT NULL,
        Token     nvarchar(300) NOT NULL,
        CreatedOn datetime      NOT NULL CONSTRAINT DF_ChatFcmSubscription_CreatedOn DEFAULT (GETDATE())
    );

    CREATE UNIQUE INDEX UQ_ChatFcmSubscription_Token ON dbo.ChatFcmSubscription (Token);
    CREATE INDEX IX_ChatFcmSubscription_Email ON dbo.ChatFcmSubscription (Email);
END
GO
