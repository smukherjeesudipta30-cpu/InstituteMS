using System.Security.Claims;
using InstituteMS.Application.Chat;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InstituteMS.Api.Hubs;

/// <summary>
/// Real-time delivery for chat. All the actual reads, writes and permission
/// checks live in <c>IChatService</c> behind the ordinary REST endpoints
/// (<see cref="Controllers.v1.ChatController"/>) — this hub only pushes
/// "something changed" to browsers already listening, and lets a client join
/// the SignalR groups it is allowed into. Nothing here persists data.
/// </summary>
public class ChatHub : Hub
{
    private readonly AppDbContext _db;
    private readonly IChatPresenceTracker _presence;
    private readonly IChatNotifier _notifier;
    private readonly IChatService _chat;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(AppDbContext db, IChatPresenceTracker presence, IChatNotifier notifier, IChatService chat,
        ILogger<ChatHub> logger)
    {
        _db = db;
        _presence = presence;
        _notifier = notifier;
        _chat = chat;
        _logger = logger;
    }

    /// <summary>
    /// A signed-in connection auto-joins its personal channel, the conversations
    /// it already belongs to, the shared presence channel, and — for staff — the
    /// shared guest inbox channel, so a page refresh (a fresh connection) starts
    /// receiving everything again without the client having to re-join one
    /// conversation at a time.
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);

        if (!string.IsNullOrWhiteSpace(email))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, PersonalGroup(email));
            await Groups.AddToGroupAsync(Context.ConnectionId, PresenceGroup);

            if (Context.User!.IsInRole(UserRoles.Admin)
                || Context.User.IsInRole(UserRoles.Editor)
                || Context.User.IsInRole(UserRoles.Receptionist))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, GuestInboxGroup);
            }

            var conversationIds = await _db.ChatParticipants
                .AsNoTracking()
                .Where(p => p.UserEmail == email)
                .Select(p => p.ConversationId)
                .Distinct()
                .ToListAsync();

            foreach (var id in conversationIds)
                await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(id));

            await TouchLastSeenAsync(email);

            // Only the person's FIRST live connection is a real state change —
            // a second tab opening must not re-announce someone already online.
            if (_presence.MarkConnected(email))
                await _notifier.PresenceChangedAsync(email, PresenceOnline);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Stamps the moment this person's chat connection was last live, for "last seen …".</summary>
    private async Task TouchLastSeenAsync(string email)
    {
        var now = DateTime.Now;
        await _db.Users
            .Where(u => u.Email == email)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenOn, now));
    }

    /// <summary>
    /// The mirror image of <see cref="OnConnectedAsync"/>'s presence bookkeeping —
    /// only announces offline once the person's LAST connection has gone, so
    /// closing one of two open tabs does not flicker them offline.
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);

        if (!string.IsNullOrWhiteSpace(email) && _presence.MarkDisconnected(email))
        {
            var now = DateTime.Now;
            await _db.Users
                .Where(u => u.Email == email)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenOn, now));

            await _notifier.PresenceChangedAsync(email, PresenceOffline, now.ToString("o"));
        }

        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Called by the client when the person's own tab goes idle or hidden, and
    /// again when they come back — a courtesy on top of the online/offline
    /// connection state, not a replacement for it. Silently a no-op for a
    /// connection the server does not recognise as signed in.
    /// </summary>
    [Authorize]
    public async Task SetPresenceStatus(bool away)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return;

        /* Someone on a call stays "on a call" to everyone else even when their
           screen turns off or they switch to another app mid-call: the phone
           reports itself away then, but they are very much there. The away
           flag is still recorded, so it shows once the call ends. */
        if (_presence.SetAway(email, away) && !IsInCall(email))
            await _notifier.PresenceChangedAsync(email, away ? PresenceAway : PresenceOnline);
    }

    /// <summary>
    /// The person is (or is no longer) on a call — a courtesy state on top of
    /// online, so a third person deciding whether to message or ring them sees
    /// they are busy. Each side of a call reports its own; a peer-to-peer call
    /// has no server record of who is talking to whom, only that they are.
    /// </summary>
    [Authorize]
    public async Task SetCallStatus(bool inCall)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return;

        if (_presence.SetInCall(email, inCall))
            await _notifier.PresenceChangedAsync(email,
                inCall ? PresenceOnCall : IsAway(email) ? PresenceAway : PresenceOnline);
    }

    /// <summary>
    /// Starts or stops "X is typing" for everyone else in one conversation —
    /// direct, group or guest thread alike. Refused silently unless the caller
    /// may access that conversation, so it cannot be used to probe or spam a
    /// thread they are not in. Still ephemeral: clients also let a start lapse
    /// on their own, so a lost "stop" (a closed tab) never leaves it stuck.
    /// </summary>
    [Authorize]
    public async Task SetTyping(int conversationId, bool isTyping)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return;

        var name = await _chat.GetTypingNameAsync(email, conversationId);
        if (name is null) return;

        await Clients.OthersInGroup(ConversationGroup(conversationId))
            .SendAsync("TypingChanged", conversationId, email, name, isTyping);
    }

    /// <summary>The guest widget's <see cref="SetTyping"/> — proved by its guest token instead of a sign-in.</summary>
    [AllowAnonymous]
    public async Task SetGuestTyping(int conversationId, string guestToken, bool isTyping)
    {
        if (string.IsNullOrWhiteSpace(guestToken)) return;

        var name = await _chat.GetGuestTypingNameAsync(conversationId, guestToken);
        if (name is null) return;

        await Clients.OthersInGroup(ConversationGroup(conversationId))
            .SendAsync("TypingChanged", conversationId, (string?)null, name, isTyping);
    }

    /// <summary>
    /// Called by the client right after it opens or is told about a conversation
    /// it was not already joined to (a brand new direct/group chat, or a staff
    /// member opening an unclaimed guest thread from the shared inbox).
    /// </summary>
    [Authorize]
    public async Task JoinConversation(int conversationId)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return;

        var isParticipant = await _db.ChatParticipants
            .AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId && p.UserEmail == email);

        var isStaffOnGuestThread = !isParticipant
            && (Context.User!.IsInRole(UserRoles.Admin)
                || Context.User.IsInRole(UserRoles.Editor)
                || Context.User.IsInRole(UserRoles.Receptionist))
            && await _db.ChatConversations
                .AsNoTracking()
                .AnyAsync(c => c.Id == conversationId && c.Type == ChatConversationType.Guest);

        if (isParticipant || isStaffOnGuestThread)
            await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
    }

    /// <summary>
    /// The guest widget's equivalent of <see cref="JoinConversation"/> — proves it
    /// holds the conversation's guest token rather than a signed-in identity.
    /// </summary>
    [AllowAnonymous]
    public async Task JoinGuestConversation(int conversationId, string guestToken)
    {
        if (string.IsNullOrWhiteSpace(guestToken)) return;

        var owns = await _db.ChatParticipants
            .AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId && p.GuestToken == guestToken);

        if (owns)
            await Groups.AddToGroupAsync(Context.ConnectionId, ConversationGroup(conversationId));
    }

    /* ------------------------------------------------------------- calls */

    /// <remarks>
    /// Pure relay — like the rest of this hub, no call state is persisted
    /// anywhere. WebRTC needs a channel to exchange an SDP offer/answer and
    /// ICE candidates before the two browsers can talk directly to each
    /// other at all; this hub is that channel, nothing more. Actual audio
    /// and video never pass through the server.
    ///
    /// One call at a time, 1:1 only (Direct conversations, not Group) — a
    /// mesh call among more than two people needs real infrastructure (an
    /// SFU) this app does not have, and that is a deliberately separate,
    /// much bigger project from what was asked for here.
    /// </remarks>
    [Authorize]
    public async Task CallInvite(string targetEmail, string callId, string kind, string fromName)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(targetEmail)) return;
        if (!await _chat.CanCallAsync(email, targetEmail)) return;

        // The live path — reaches the callee only if their app is open and
        // connected right now.
        await Clients.Group(PersonalGroup(targetEmail))
            .SendAsync("IncomingCall", email, fromName, callId, kind);

        // The closed-app path — a device push carrying the same call so a phone
        // whose app is shut still rings and can answer. Best-effort: a failed
        // push must never break the call for a callee who is in fact connected.
        try
        {
            await _chat.NotifyCallPushAsync(targetEmail, email, fromName, callId, kind);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Call push notification failed for {Target}", targetEmail);
        }
    }

    [Authorize]
    public async Task CallResponse(string targetEmail, string callId, bool accepted)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(targetEmail)) return;

        await Clients.Group(PersonalGroup(targetEmail)).SendAsync("CallResponded", email, callId, accepted);
    }

    /// <summary>Relays one SDP offer/answer or ICE candidate — <paramref name="type"/> is "offer", "answer" or "ice".</summary>
    [Authorize]
    public async Task CallSignal(string targetEmail, string callId, string type, string payload)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(targetEmail)) return;

        await Clients.Group(PersonalGroup(targetEmail)).SendAsync("CallSignal", email, callId, type, payload);
    }

    [Authorize]
    public async Task CallHangup(string targetEmail, string callId)
    {
        var email = Context.User?.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(targetEmail)) return;

        await Clients.Group(PersonalGroup(targetEmail)).SendAsync("CallEnded", email, callId);
    }

    public const string GuestInboxGroup = "staff-guest-inbox";
    public const string PresenceGroup = "presence-all";
    public static string ConversationGroup(int conversationId) => $"conv:{conversationId}";
    public static string PersonalGroup(string email) => $"user:{email}";

    private const string PresenceOnline = "online";
    private bool IsInCall(string email) =>
        _presence.InCallEmails.Contains(email, StringComparer.OrdinalIgnoreCase);

    private bool IsAway(string email) =>
        _presence.AwayEmails.Contains(email, StringComparer.OrdinalIgnoreCase);

    private const string PresenceAway = "away";
    private const string PresenceOffline = "offline";
    private const string PresenceOnCall = "incall";
}
