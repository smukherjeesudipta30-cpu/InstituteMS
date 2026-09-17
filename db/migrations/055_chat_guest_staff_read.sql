-- Guest inbox unread badge: staff aren't a participant on an unclaimed guest
-- conversation until they reply, so MarkReadAsync had nothing to persist "I've
-- read this" onto, and the inbox badge kept counting every open guest thread
-- forever, read or not. This is a shared marker instead of a per-participant
-- one, matching how a shared team inbox actually works: any staff member
-- opening the thread marks it read for everyone.
ALTER TABLE dbo.ChatConversation ADD StaffLastReadMessageId INT NULL;
