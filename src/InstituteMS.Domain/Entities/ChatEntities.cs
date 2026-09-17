namespace InstituteMS.Domain.Entities;

/// <summary>
/// What kind of conversation a row is. <c>Guest</c> is the one type with no
/// signed-in participant on one side -- a visitor talking to whichever of
/// Admin/Editor/Receptionist picks it up.
/// </summary>
public enum ChatConversationType
{
    Direct = 0,
    Group = 1,
    Guest = 2
}

/// <summary>
/// One chat thread — a direct pair, a group, or a guest's conversation with the
/// front desk. Holds no messages itself; <see cref="ChatMessage"/> and
/// <see cref="ChatParticipant"/> both point back at it.
/// </summary>
public class ChatConversation
{
    public int Id { get; set; }
    public ChatConversationType Type { get; set; }

    /// <summary>Set for a Group conversation only; null for Direct and Guest.</summary>
    public string? Title { get; set; }

    /// <summary>The email of whoever started it, or null for a guest.</summary>
    public string? CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }

    /// <summary>Denormalised onto the row so the conversation list can sort/preview without a join per row.</summary>
    public DateTime? LastMessageOn { get; set; }

    /// <summary>
    /// Guest conversations only. The shared inbox has no single reader to keep
    /// a per-participant LastReadMessageId for — staff aren't even a
    /// participant until they reply — so "read" here means any staff member
    /// has opened it, the same way a shared team mailbox works.
    /// </summary>
    public int? StaffLastReadMessageId { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ChatParticipant> Participants { get; set; } = [];
    public ICollection<ChatMessage> Messages { get; set; } = [];
}

/// <summary>
/// One person's membership in one conversation. A signed-in participant is
/// identified by <see cref="UserEmail"/>; a guest instead carries a
/// <see cref="GuestToken"/> — the secret their browser holds in place of a
/// login, since a website visitor has no account to authenticate with.
/// </summary>
public class ChatParticipant
{
    public int Id { get; set; }
    public int ConversationId { get; set; }

    /// <summary>Null for the guest side of a Guest conversation.</summary>
    public string? UserEmail { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Role held at the moment they joined — "Admin", "Editor", "Receptionist", "Teacher", "Student", or "Guest".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Set only on the guest's own participant row. Returned once, when the
    /// conversation starts, and kept by the browser (e.g. in localStorage) to
    /// prove it is still the same visitor on every later read or reply —
    /// there is no password to sign back in with.
    /// </summary>
    public string? GuestToken { get; set; }

    /// <summary>Set only on the guest side — a phone number or email they gave, so staff has a way to follow up off-chat.</summary>
    public string? Contact { get; set; }

    public DateTime JoinedOn { get; set; }

    /// <summary>The last message this participant is known to have seen, for an unread count.</summary>
    public int? LastReadMessageId { get; set; }

    public ChatConversation? Conversation { get; set; }
}

/// <summary>One message in one conversation.</summary>
public class ChatMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }

    /// <summary>Null when the sender was the guest side of a Guest conversation.</summary>
    public string? SenderEmail { get; set; }

    public string SenderDisplayName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime SentOn { get; set; }

    /// <summary>
    /// The four go together or not at all. Held as Base64 text in the row
    /// itself — the same choice already made for <c>StudentPhoto</c> and
    /// <c>CompetitionDocument</c> — rather than a file on disk, so an
    /// attachment travels with the database backup instead of living on a
    /// file share the backup knows nothing about.
    /// </summary>
    public string? AttachmentFileName { get; set; }
    public string? AttachmentContentType { get; set; }
    public string? AttachmentContent { get; set; }
    public long? AttachmentSize { get; set; }

    public ChatConversation? Conversation { get; set; }
}

/// <summary>
/// One browser's registration to receive a chat notification even when it has
/// no live SignalR connection — closed tab, phone locked, another site open
/// in front. One row per browser/device rather than per person: the same
/// person signed in on a phone and a desktop gets a push on both.
/// </summary>
public class ChatPushSubscription
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>The browser push service's URL for this subscription — unique per browser install.</summary>
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}

/// <summary>
/// One row per installed-app device, so a chat message can reach the Android
/// app as a native notification. Kept separate from <see cref="ChatPushSubscription"/>
/// rather than widened into it: a browser subscription is three coordinates
/// (Endpoint/P256dh/Auth) meaningful only to the Web Push API, an FCM
/// registration is a single opaque token meaningful only to Firebase — the
/// two providers do not share a shape, only a purpose.
/// </summary>
public class ChatFcmSubscription
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;

    /// <summary>The device's current FCM registration token — unique per device, and replaced (not accumulated) whenever Firebase reissues one.</summary>
    public string Token { get; set; } = string.Empty;
    public DateTime CreatedOn { get; set; }
}
