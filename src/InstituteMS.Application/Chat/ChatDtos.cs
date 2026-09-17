using FluentValidation;
using InstituteMS.Application.Common;

namespace InstituteMS.Application.Chat;

/* ================================================================ shared */

/// <summary>Someone the signed-in user is allowed to start a direct chat with.</summary>
public record ChatDirectoryEntryDto(string Email, string DisplayName, string Role);

/// <summary>What a browser hands back from `PushManager.subscribe()`, flattened.</summary>
public record ChatPushSubscribeRequest(string Endpoint, string P256dh, string Auth);

public class ChatPushSubscribeRequestValidator : AbstractValidator<ChatPushSubscribeRequest>
{
    public ChatPushSubscribeRequestValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty().MaximumLength(500);
        RuleFor(x => x.P256dh).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Auth).NotEmpty().MaximumLength(100);
    }
}

/// <summary>The installed app's Firebase Cloud Messaging registration token — the Android equivalent of a Web Push subscription.</summary>
public record ChatFcmSubscribeRequest(string Token);

public class ChatFcmSubscribeRequestValidator : AbstractValidator<ChatFcmSubscribeRequest>
{
    public ChatFcmSubscribeRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(300);
    }
}

public record ChatMessageDto(
    int Id,
    int ConversationId,
    /// <summary>Null when the sender was a guest.</summary>
    string? SenderEmail,
    string SenderDisplayName,
    string SenderRole,
    string Body,
    DateTime SentOn,
    /// <summary>True when the caller reading this message was the one who sent it.</summary>
    bool IsMine,
    /// <summary>Set together, or not at all. The file's bytes are fetched separately, by <see cref="Id"/>, not carried here.</summary>
    string? AttachmentFileName = null,
    string? AttachmentContentType = null,
    long? AttachmentSize = null,
    /// <summary>
    /// True only for a Direct conversation's own message, once the other
    /// participant's read position has reached it. Left false for Group and
    /// Guest — "seen by everyone" across several people needs a different
    /// display (a count, or a list) than a single tick, which is a fuller
    /// feature than a read receipt needs to be right now.
    /// </summary>
    bool IsSeen = false);

/// <summary>
/// A file attached to an outgoing message. <see cref="Base64"/> may carry a
/// <c>data:&lt;type&gt;;base64,</c> prefix exactly as a browser's FileReader
/// produces it — stripped server-side before decoding.
/// </summary>
public record ChatAttachmentUpload
{
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public string Base64 { get; init; } = string.Empty;
}

public record ChatParticipantDto(string? Email, string DisplayName, string Role, string? Contact = null);

public record ChatConversationSummaryDto(
    int Id,
    string Type,
    /// <summary>Set for a Group only.</summary>
    string? Title,
    IReadOnlyList<ChatParticipantDto> Participants,
    string? LastMessagePreview,
    DateTime? LastMessageOn,
    int UnreadCount);

/* ================================================================= guest */

/// <summary>Opens a new conversation with the front desk. No sign-in — a website visitor.</summary>
public record StartGuestChatRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Contact { get; init; }
    public string Message { get; init; } = string.Empty;
}

public class StartGuestChatRequestValidator : AbstractValidator<StartGuestChatRequest>
{
    public StartGuestChatRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Tell us your name.").MaximumLength(100);
        RuleFor(x => x.Contact).MaximumLength(150);
        RuleFor(x => x.Message).NotEmpty().WithMessage("Type a message to start the chat.").MaximumLength(2000);
    }
}

/// <summary>
/// <see cref="GuestToken"/> is the visitor's whole credential — kept by their
/// browser (e.g. localStorage) and sent back on every later read or reply to
/// this same conversation, since there is no account to sign back in with.
/// </summary>
public record GuestStartedDto(int ConversationId, string GuestToken, ChatMessageDto FirstMessage);

public record SendGuestMessageRequest
{
    public string GuestToken { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public ChatAttachmentUpload? Attachment { get; init; }
}

public class SendGuestMessageRequestValidator : AbstractValidator<SendGuestMessageRequest>
{
    public SendGuestMessageRequestValidator()
    {
        RuleFor(x => x.GuestToken).NotEmpty();
        RuleFor(x => x.Body).MaximumLength(2000);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Body) || x.Attachment is not null)
            .WithMessage("Type a message or attach a file.");
    }
}

/* ============================================================ authenticated */

public record SendMessageRequest
{
    public string Body { get; init; } = string.Empty;
    public ChatAttachmentUpload? Attachment { get; init; }
}

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Body).MaximumLength(2000);
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Body) || x.Attachment is not null)
            .WithMessage("Type a message or attach a file.");
    }
}

public record StartDirectChatRequest
{
    public string TargetEmail { get; init; } = string.Empty;
}

public class StartDirectChatRequestValidator : AbstractValidator<StartDirectChatRequest>
{
    public StartDirectChatRequestValidator()
    {
        RuleFor(x => x.TargetEmail).NotEmpty().EmailAddress();
    }
}

/// <summary>Starts a group. The creator is added automatically alongside everyone named here.</summary>
public record StartGroupChatRequest
{
    public string Title { get; init; } = string.Empty;
    public IReadOnlyList<string> MemberEmails { get; init; } = [];
}

public class StartGroupChatRequestValidator : AbstractValidator<StartGroupChatRequest>
{
    public StartGroupChatRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("Give the group a name.").MaximumLength(150);
        RuleFor(x => x.MemberEmails)
            .Must(m => m.Count > 0).WithMessage("Add at least one other member.")
            .Must(m => m.Count <= 50).WithMessage("A group is limited to 50 members.");
    }
}

public record AddGroupMemberRequest
{
    public string MemberEmail { get; init; } = string.Empty;
}

public class AddGroupMemberRequestValidator : AbstractValidator<AddGroupMemberRequest>
{
    public AddGroupMemberRequestValidator()
    {
        RuleFor(x => x.MemberEmail).NotEmpty().EmailAddress();
    }
}

/// <summary>The decoded bytes for one message's attachment, ready to serve back.</summary>
public record ChatAttachmentDto(string FileName, string ContentType, byte[] Content);

/* ================================================================ service */

public interface IChatService
{
    /* -------------------------------------------------------------- guest */

    Task<Result<GuestStartedDto>> StartGuestConversationAsync(
        StartGuestChatRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ChatMessageDto>>> GetGuestMessagesAsync(
        int conversationId, string guestToken, CancellationToken ct = default);

    Task<Result<ChatMessageDto>> SendGuestMessageAsync(
        int conversationId, SendGuestMessageRequest request, CancellationToken ct = default);

    /// <summary>The guest's own attachment, proved by the same token that reads their messages.</summary>
    Task<Result<ChatAttachmentDto>> GetGuestAttachmentAsync(
        int conversationId, int messageId, string guestToken, CancellationToken ct = default);

    /* --------------------------------------------------------- signed in */

    /// <summary>Who the signed-in user is allowed to start a direct chat with.</summary>
    Task<Result<IReadOnlyList<ChatDirectoryEntryDto>>> GetDirectoryAsync(
        string email, CancellationToken ct = default);

    /// <summary>Every conversation (direct and group) the signed-in user is a participant of.</summary>
    Task<Result<IReadOnlyList<ChatConversationSummaryDto>>> GetMyConversationsAsync(
        string email, CancellationToken ct = default);

    /// <summary>
    /// Every guest conversation, whether or not staff has replied yet — a shared
    /// inbox. Admin, Editor and Receptionist only.
    /// </summary>
    Task<Result<IReadOnlyList<ChatConversationSummaryDto>>> GetGuestInboxAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Finds the existing direct conversation between the two, or opens one —
    /// refused unless the chat access rule allows this pair to message each
    /// other. Returns the conversation id either way.
    /// </summary>
    Task<Result<int>> StartOrGetDirectConversationAsync(
        string email, string targetEmail, CancellationToken ct = default);

    Task<Result<int>> StartGroupChatAsync(
        string email, StartGroupChatRequest request, CancellationToken ct = default);

    Task<Result<bool>> AddGroupMemberAsync(
        string email, int conversationId, AddGroupMemberRequest request, CancellationToken ct = default);

    Task<Result<IReadOnlyList<ChatMessageDto>>> GetMessagesAsync(
        string email, int conversationId, CancellationToken ct = default);

    /// <summary>
    /// Sends into a conversation the caller already belongs to — or, for
    /// Admin/Editor/Receptionist replying to a Guest conversation for the first
    /// time, joins them as a participant on the spot.
    /// </summary>
    Task<Result<ChatMessageDto>> SendMessageAsync(
        string email, int conversationId, SendMessageRequest request, CancellationToken ct = default);

    Task<Result<bool>> MarkReadAsync(
        string email, int conversationId, CancellationToken ct = default);

    /// <summary>An attachment in a conversation the caller may access — same rule as reading messages there.</summary>
    Task<Result<ChatAttachmentDto>> GetAttachmentAsync(
        string email, int conversationId, int messageId, CancellationToken ct = default);

    /// <summary>Everyone currently holding at least one live connection — a snapshot for hydrating the panel on open.</summary>
    Task<Result<ChatPresenceSnapshotDto>> GetOnlinePresenceAsync(
        string email, CancellationToken ct = default);

    /// <summary>
    /// Sends a device push for an incoming call, so a phone whose app is closed
    /// still rings — the live SignalR "IncomingCall" reaches only a connected
    /// app. Carries the call's own fields as data so the app, opened from the
    /// notification, can join the very call that is still ringing rather than
    /// just landing on the chat list.
    /// </summary>
    Task NotifyCallPushAsync(
        string targetEmail, string fromEmail, string fromName, string callId, string kind,
        CancellationToken ct = default);

    /// <summary>Registers (or re-registers) this browser to receive a push notification for a new chat message.</summary>
    Task<Result<bool>> SubscribePushAsync(
        string email, ChatPushSubscribeRequest request, CancellationToken ct = default);

    /// <summary>Forgets one browser's subscription — called when the person turns notifications off.</summary>
    Task<Result<bool>> UnsubscribePushAsync(
        string email, string endpoint, CancellationToken ct = default);

    /// <summary>Registers (or re-registers) this installed app's device to receive a push notification via Firebase Cloud Messaging.</summary>
    Task<Result<bool>> SubscribeFcmAsync(
        string email, ChatFcmSubscribeRequest request, CancellationToken ct = default);

    /// <summary>Forgets one device's FCM registration — called when the person signs out of the app.</summary>
    Task<Result<bool>> UnsubscribeFcmAsync(
        string email, string token, CancellationToken ct = default);

    /// <summary>
    /// Whether two people are allowed to call each other — the same rule as
    /// chatting (<c>CanMessage</c>), checked again at the signaling hub so a
    /// call invite can't reach someone the chat rules wouldn't let this
    /// person message in the first place.
    /// </summary>
    Task<bool> CanCallAsync(string email, string targetEmail, CancellationToken ct = default);
}

/// <summary>
/// A one-off presence snapshot for hydrating the panel when it first opens —
/// <c>AwayEmails</c> is always a subset of <c>OnlineEmails</c>, since away is
/// a state a live connection is in, not an alternative to having one.
/// </summary>
public record ChatPresenceSnapshotDto(
    IReadOnlyList<string> OnlineEmails, IReadOnlyList<string> AwayEmails);

/// <summary>
/// Who currently has the app open. In-memory and per-process by design: presence
/// is a courtesy ("is it worth waiting for a reply right now"), not a record
/// anything else depends on, so it does not need a database row or to survive an
/// app-pool recycle — it rebuilds itself from scratch as connections reconnect.
/// </summary>
/// <remarks>
/// Counted, not a flag: the same person can have the chat panel open in two tabs
/// (or a phone and a desktop), and closing one of them must not show them as
/// offline while the other is still live.
/// </remarks>
public interface IChatPresenceTracker
{
    /// <summary>True exactly when this was the person's first live connection.</summary>
    bool MarkConnected(string email);

    /// <summary>
    /// True exactly when this was the person's last live connection. Also
    /// clears any away flag left over for them, so a later reconnect starts
    /// active rather than reappearing already marked away.
    /// </summary>
    bool MarkDisconnected(string email);

    /// <summary>
    /// Records whether the person is away from an otherwise-live connection
    /// (tab hidden, or no activity for a while) — set from whichever of their
    /// tabs most recently reported a change. Returns true only when this
    /// actually changed their away flag while they are still online; false
    /// for a no-op (already in that state) or for someone with no live
    /// connection at all, since away only means something on top of online.
    /// </summary>
    bool SetAway(string email, bool away);

    IReadOnlyCollection<string> OnlineEmails { get; }

    /// <summary>Always a subset of <see cref="OnlineEmails"/>.</summary>
    IReadOnlyCollection<string> AwayEmails { get; }
}

/// <summary>
/// Pushes a chat event over the real-time channel. Implemented in the API
/// project, which owns the SignalR hub — Application and Infrastructure never
/// need to reference SignalR types directly, the same separation the rest of
/// this codebase keeps between "what happens" and "how it is delivered".
/// </summary>
public interface IChatNotifier
{
    /// <summary>A message was persisted — push it to everyone already listening on that conversation.</summary>
    Task MessageReceivedAsync(int conversationId, ChatMessageDto message, CancellationToken ct = default);

    /// <summary>
    /// A conversation one of these people now belongs to changed (a new one was
    /// opened with them in it). Tells each person's own channel to refresh its
    /// conversation list.
    /// </summary>
    Task ConversationsChangedAsync(IEnumerable<string> emails, CancellationToken ct = default);

    /// <summary>A new guest conversation started — tells the shared staff inbox channel to refresh.</summary>
    Task GuestInboxChangedAsync(CancellationToken ct = default);

    /// <summary>
    /// Someone's presence changed — tells every connected client so it can
    /// update a presence dot. <paramref name="status"/> is one of "online",
    /// "away" or "offline"; a plain bool stopped being enough once away
    /// became a third state rather than a variant of online.
    /// </summary>
    Task PresenceChangedAsync(string email, string status, CancellationToken ct = default);

    /// <summary>
    /// Someone read up to a point in a conversation — tells everyone else
    /// already listening there so an open thread can flip "Seen" live,
    /// without the sender needing to reopen it.
    /// </summary>
    Task MessagesReadAsync(int conversationId, string readerEmail, int lastReadMessageId, CancellationToken ct = default);
}
