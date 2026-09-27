-- Chat message deletion.
--
-- "Delete for everyone": the sender removes their own message for the whole
-- conversation. The row is kept (so the thread can show "This message was
-- deleted" in its place) but its body and attachment are wiped by the API at
-- the same moment, so the content cannot be read back from the table later.
--
-- "Delete for me": one person hides a message from their own view only. One
-- row per person per message; everyone else still sees it.
--
-- Idempotent: safe to run more than once.

IF COL_LENGTH('dbo.ChatMessage', 'DeletedOn') IS NULL
    ALTER TABLE dbo.ChatMessage ADD DeletedOn datetime NULL;
GO

IF COL_LENGTH('dbo.ChatMessage', 'DeletedBy') IS NULL
    ALTER TABLE dbo.ChatMessage ADD DeletedBy varchar(150) NULL;
GO

IF OBJECT_ID('dbo.ChatMessageHidden') IS NULL
BEGIN
    CREATE TABLE dbo.ChatMessageHidden
    (
        MessageId int          NOT NULL
            CONSTRAINT FK_ChatMessageHidden_Message REFERENCES dbo.ChatMessage(Id) ON DELETE CASCADE,
        UserEmail varchar(150) NOT NULL,
        HiddenOn  datetime     NOT NULL CONSTRAINT DF_ChatMessageHidden_HiddenOn DEFAULT (GETDATE()),
        CONSTRAINT PK_ChatMessageHidden PRIMARY KEY (MessageId, UserEmail)
    );

    CREATE INDEX IX_ChatMessageHidden_UserEmail ON dbo.ChatMessageHidden (UserEmail, MessageId);
END
GO
