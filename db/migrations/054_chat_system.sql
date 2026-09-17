SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/*
    054_chat_system.sql
    -------------------------------------------------------------------------
    The chat feature: a guest (unauthenticated site visitor) reaching
    Admin/Editor/Receptionist, and internal chat between people the app's own
    role rules allow to talk to each other (student-student, teacher-teacher,
    receptionist-receptionist, and Admin/Editor with anyone), plus groups.

    Three tables:

      * ChatConversation -- one row per thread. Type is 'Direct', 'Group' or
        'Guest'; Title only ever set on a Group.

      * ChatParticipant -- one row per person per conversation. A signed-in
        participant is identified by UserEmail; the guest side of a Guest
        conversation instead carries GuestToken, a random secret their
        browser holds in place of a login (a visitor has no account to sign
        back in with).

      * ChatMessage -- one row per message, SenderEmail null for a guest's
        own messages the same way ChatParticipant.UserEmail is. The four
        Attachment* columns go together or not at all -- held as Base64 text
        in the row, the same choice already made for StudentPhoto and
        CompetitionDocument, rather than a file on disk that a database
        backup would know nothing about.

    nvarchar throughout Body/DisplayName/Title: message and name content is
    free text a person types, in whatever script they type in, unlike most of
    the legacy varchar columns this migration set otherwise follows.
*/

IF OBJECT_ID('dbo.ChatConversation', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatConversation
    (
        Id            int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatConversation PRIMARY KEY,
        Type          varchar(10)   NOT NULL,
        Title         nvarchar(150) NULL,
        CreatedBy     varchar(150)  NULL,
        CreatedOn     datetime      NOT NULL CONSTRAINT DF_ChatConversation_CreatedOn DEFAULT (GETDATE()),
        LastMessageOn datetime      NULL,
        IsActive      bit           NOT NULL CONSTRAINT DF_ChatConversation_IsActive DEFAULT (1)
    );
END
GO

IF OBJECT_ID('dbo.ChatParticipant', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatParticipant
    (
        Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatParticipant PRIMARY KEY,
        ConversationId    int           NOT NULL
            CONSTRAINT FK_ChatParticipant_Conversation REFERENCES dbo.ChatConversation(Id) ON DELETE CASCADE,
        UserEmail         varchar(150)  NULL,
        DisplayName       nvarchar(150) NOT NULL,
        Role              varchar(30)   NOT NULL,
        GuestToken        varchar(64)   NULL,
        Contact           nvarchar(150) NULL,
        JoinedOn          datetime      NOT NULL CONSTRAINT DF_ChatParticipant_JoinedOn DEFAULT (GETDATE()),
        LastReadMessageId int           NULL
    );

    CREATE INDEX IX_ChatParticipant_Conversation_User ON dbo.ChatParticipant (ConversationId, UserEmail);
    CREATE INDEX IX_ChatParticipant_GuestToken ON dbo.ChatParticipant (GuestToken);
END
GO

IF OBJECT_ID('dbo.ChatMessage', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ChatMessage
    (
        Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatMessage PRIMARY KEY,
        ConversationId    int           NOT NULL
            CONSTRAINT FK_ChatMessage_Conversation REFERENCES dbo.ChatConversation(Id) ON DELETE CASCADE,
        SenderEmail       varchar(150)  NULL,
        SenderDisplayName nvarchar(150) NOT NULL,
        SenderRole        varchar(30)   NOT NULL,
        Body              nvarchar(2000) NOT NULL,
        SentOn            datetime      NOT NULL CONSTRAINT DF_ChatMessage_SentOn DEFAULT (GETDATE()),

        AttachmentFileName    nvarchar(150) NULL,
        AttachmentContentType nvarchar(100) NULL,
        AttachmentContent     nvarchar(max) NULL,
        AttachmentSize        bigint        NULL
    );

    CREATE INDEX IX_ChatMessage_Conversation_Id ON dbo.ChatMessage (ConversationId, Id);
END
GO
