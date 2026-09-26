using InstituteMS.Application.Chat;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Chat: an anonymous website visitor reaching Admin/Editor/Receptionist, and
/// internal chat between whoever the role rules allow to message each other.
/// </summary>
/// <remarks>
/// No class-level <c>[Authorize]</c> here, deliberately: the guest actions are
/// anonymous and the rest authenticated, on the same controller, so each is
/// stated on its own action rather than relying on one of the two attributes
/// to override the other — <c>[AllowAnonymous]</c> at class level would make
/// every action anonymous regardless of a narrower <c>[Authorize]</c> beside
/// it, since AllowAnonymous wins wherever it appears on an endpoint.
/// </remarks>
[Route("api/v1/chat")]
[Tags("Chat")]
public class ChatController : ApiControllerBase
{
    private readonly IChatService _chat;
    private readonly IConfiguration _configuration;

    public ChatController(IChatService chat, IConfiguration configuration)
    {
        _chat = chat;
        _configuration = configuration;
    }

    /* -------------------------------------------------------------- guest */

    /// <summary>Opens a new conversation with the front desk. Rate limited — open to the internet.</summary>
    [HttpPost("guest/start")]
    [AllowAnonymous]
    [EnableRateLimiting("contact")]
    [ProducesResponseType(typeof(ApiResponse<GuestStartedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<GuestStartedDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GuestStart([FromBody] StartGuestChatRequest request, CancellationToken ct)
        => Envelope(await _chat.StartGuestConversationAsync(request, ct));

    /// <summary>The guest's own message history — proved by the token issued when the chat started.</summary>
    [HttpGet("guest/{conversationId:int}/messages")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatMessageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatMessageDto>>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GuestMessages(
        int conversationId, [FromQuery] string guestToken, CancellationToken ct)
        => Envelope(await _chat.GetGuestMessagesAsync(conversationId, guestToken, ct));

    /// <summary>The guest's reply. Rate limited — open to the internet.</summary>
    [HttpPost("guest/{conversationId:int}/messages")]
    [AllowAnonymous]
    [EnableRateLimiting("contact")]
    [ProducesResponseType(typeof(ApiResponse<ChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ChatMessageDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GuestReply(
        int conversationId, [FromBody] SendGuestMessageRequest request, CancellationToken ct)
        => Envelope(await _chat.SendGuestMessageAsync(conversationId, request, ct));

    /// <summary>The guest's own attachment — proved by the same token that reads their messages.</summary>
    [HttpGet("guest/{conversationId:int}/messages/{messageId:int}/attachment")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GuestAttachment(
        int conversationId, int messageId, [FromQuery] string guestToken, CancellationToken ct)
    {
        var result = await _chat.GetGuestAttachmentAsync(conversationId, messageId, guestToken, ct);
        if (!result.Succeeded || result.Value is null) return NotFound();

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /* --------------------------------------------------------- signed in */

    /// <summary>Who the signed-in user is allowed to start a direct chat with.</summary>
    [HttpGet("directory")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatDirectoryEntryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Directory(CancellationToken ct)
        => Envelope(await _chat.GetDirectoryAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>Every conversation (direct and group) the signed-in user belongs to.</summary>
    [HttpGet("conversations")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatConversationSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Conversations(CancellationToken ct)
        => Envelope(await _chat.GetMyConversationsAsync(CurrentUserName ?? string.Empty, ct));

    /// <summary>Every guest conversation, claimed or not — a shared inbox.</summary>
    [HttpGet("guest-inbox")]
    [Authorize(Roles = UserRoles.MasterData)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatConversationSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GuestInbox(CancellationToken ct)
        => Envelope(await _chat.GetGuestInboxAsync(ct));

    /// <summary>Finds or opens the direct conversation with one other person.</summary>
    [HttpPost("conversations/direct")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartDirect([FromBody] StartDirectChatRequest request, CancellationToken ct)
        => Envelope(await _chat.StartOrGetDirectConversationAsync(
            CurrentUserName ?? string.Empty, request.TargetEmail, ct));

    /// <summary>Starts a group. The caller is added automatically alongside everyone named.</summary>
    [HttpPost("conversations/group")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartGroup([FromBody] StartGroupChatRequest request, CancellationToken ct)
        => Envelope(await _chat.StartGroupChatAsync(CurrentUserName ?? string.Empty, request, ct));

    /// <summary>Adds one more member to a group the caller already belongs to.</summary>
    [HttpPost("conversations/{conversationId:int}/members")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMember(
        int conversationId, [FromBody] AddGroupMemberRequest request, CancellationToken ct)
        => Envelope(await _chat.AddGroupMemberAsync(CurrentUserName ?? string.Empty, conversationId, request, ct));

    /// <summary>Message history for one conversation the caller may access.</summary>
    [HttpGet("conversations/{conversationId:int}/messages")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatMessageDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ChatMessageDto>>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Messages(int conversationId, CancellationToken ct)
        => Envelope(await _chat.GetMessagesAsync(CurrentUserName ?? string.Empty, conversationId, ct));

    /// <summary>
    /// Sends into a conversation the caller belongs to — or, for
    /// Admin/Editor/Receptionist, claims and replies to an unclaimed guest
    /// conversation in the same call.
    /// </summary>
    [HttpPost("conversations/{conversationId:int}/messages")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ChatMessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ChatMessageDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ChatMessageDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Send(
        int conversationId, [FromBody] SendMessageRequest request, CancellationToken ct)
        => Envelope(await _chat.SendMessageAsync(CurrentUserName ?? string.Empty, conversationId, request, ct));

    /// <summary>An attachment in a conversation the caller may access — same rule as reading messages there.</summary>
    [HttpGet("conversations/{conversationId:int}/messages/{messageId:int}/attachment")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Attachment(int conversationId, int messageId, CancellationToken ct)
    {
        var result = await _chat.GetAttachmentAsync(CurrentUserName ?? string.Empty, conversationId, messageId, ct);
        if (!result.Succeeded || result.Value is null) return NotFound();

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>Marks everything in the conversation read, up to the newest message right now.</summary>
    [HttpPost("conversations/{conversationId:int}/read")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkRead(int conversationId, CancellationToken ct)
        => Envelope(await _chat.MarkReadAsync(CurrentUserName ?? string.Empty, conversationId, ct));

    /// <summary>
    /// Everyone currently holding a live connection — a one-off snapshot for
    /// when the panel opens; PresenceChanged over the socket keeps it live
    /// after that.
    /// </summary>
    /// <summary>
    /// STUN/TURN servers for calls, from <c>WebRtc:IceServers</c> in settings.
    /// STUN alone connects two phones directly, which mobile data and many
    /// routers allow only briefly or not at all - the call then connects and
    /// goes silent. A TURN server relays the audio when that happens.
    /// </summary>
    [HttpGet("ice-servers")]
    [Authorize]
    public IActionResult IceServers()
    {
        var configured = _configuration.GetSection("WebRtc:IceServers").GetChildren()
            .Select(s => new
            {
                urls = s.GetSection("Urls").Get<string[]>() ?? (s["Urls"] is { } one ? new[] { one } : []),
                username = s["Username"],
                credential = s["Credential"]
            })
            .Where(s => s.urls.Length > 0)
            .ToList<object>();

        if (configured.Count == 0)
            configured.Add(new { urls = new[] { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" }, username = (string?)null, credential = (string?)null });

        return Ok(new { success = true, data = configured });
    }

    [HttpGet("presence")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ChatPresenceSnapshotDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Presence(CancellationToken ct)
        => Envelope(await _chat.GetOnlinePresenceAsync(CurrentUserName ?? string.Empty, ct));

    /* ---------------------------------------------------------- web push */

    /// <summary>
    /// The VAPID public key the browser needs to subscribe. Not a secret —
    /// every subscribing browser is handed it as part of subscribing — so no
    /// [Authorize]: the guest widget could reasonably want this too one day,
    /// and there is nothing here worth gating behind sign-in today either.
    /// </summary>
    [HttpGet("push/public-key")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public IActionResult PushPublicKey()
        => Envelope(Result<string>.Success(_configuration["WebPush:PublicKey"] ?? string.Empty));

    [HttpPost("push/subscribe")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PushSubscribe(ChatPushSubscribeRequest request, CancellationToken ct)
        => Envelope(await _chat.SubscribePushAsync(CurrentUserName ?? string.Empty, request, ct));

    [HttpPost("push/unsubscribe")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PushUnsubscribe([FromBody] string endpoint, CancellationToken ct)
        => Envelope(await _chat.UnsubscribePushAsync(CurrentUserName ?? string.Empty, endpoint, ct));

    /// <summary>The installed app's counterpart to <see cref="PushSubscribe"/> — a Firebase Cloud Messaging token rather than a Web Push subscription.</summary>
    [HttpPost("push/fcm/subscribe")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FcmSubscribe(ChatFcmSubscribeRequest request, CancellationToken ct)
        => Envelope(await _chat.SubscribeFcmAsync(CurrentUserName ?? string.Empty, request, ct));

    [HttpPost("push/fcm/unsubscribe")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FcmUnsubscribe([FromBody] string token, CancellationToken ct)
        => Envelope(await _chat.UnsubscribeFcmAsync(CurrentUserName ?? string.Empty, token, ct));
}
