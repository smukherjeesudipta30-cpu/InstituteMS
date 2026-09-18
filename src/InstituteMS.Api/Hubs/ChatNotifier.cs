using InstituteMS.Application.Chat;
using Microsoft.AspNetCore.SignalR;

namespace InstituteMS.Api.Hubs;

/// <summary>
/// The concrete, SignalR-backed side of <see cref="IChatNotifier"/> — registered
/// here, in the API project, so <c>ChatService</c> in Infrastructure can push
/// real-time events without that layer ever referencing SignalR types.
/// </summary>
public class ChatNotifier : IChatNotifier
{
    private readonly IHubContext<ChatHub> _hub;

    public ChatNotifier(IHubContext<ChatHub> hub) => _hub = hub;

    public Task MessageReceivedAsync(int conversationId, ChatMessageDto message, CancellationToken ct = default) =>
        _hub.Clients.Group(ChatHub.ConversationGroup(conversationId)).SendAsync("MessageReceived", message, ct);

    public Task ConversationsChangedAsync(IEnumerable<string> emails, CancellationToken ct = default) =>
        Task.WhenAll(emails.Select(email =>
            _hub.Clients.Group(ChatHub.PersonalGroup(email)).SendAsync("ConversationsChanged", cancellationToken: ct)));

    public Task GuestInboxChangedAsync(CancellationToken ct = default) =>
        _hub.Clients.Group(ChatHub.GuestInboxGroup).SendAsync("GuestInboxChanged", cancellationToken: ct);

    public Task PresenceChangedAsync(string email, string status, string? lastSeenOn = null, CancellationToken ct = default) =>
        _hub.Clients.Group(ChatHub.PresenceGroup).SendAsync("PresenceChanged", email, status, lastSeenOn, ct);

    public Task MessagesReadAsync(int conversationId, string readerEmail, int lastReadMessageId, CancellationToken ct = default) =>
        _hub.Clients.Group(ChatHub.ConversationGroup(conversationId))
            .SendAsync("MessagesRead", conversationId, readerEmail, lastReadMessageId, ct);
}
