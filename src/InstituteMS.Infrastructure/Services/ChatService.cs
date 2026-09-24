using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FirebaseAdmin.Messaging;
using InstituteMS.Application.Chat;
using InstituteMS.Application.Common;
using InstituteMS.Domain.Common;
using InstituteMS.Domain.Entities;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebPush;

namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// Guest (anonymous) chat with the front desk, and internal chat between
/// whoever the role rules below allow to talk to each other.
/// </summary>
/// <remarks>
/// The access rule, stated once in <see cref="CanMessage"/> rather than spread
/// across every endpoint: Admin and Editor may message anyone and be messaged
/// by anyone; short of that, Student, Teacher and Receptionist may all reach
/// one another freely — a student and their teacher, a teacher and the front
/// desk, a receptionist and a student all count as a legitimate conversation
/// at the institute. A guest is a separate case entirely — routed to a shared
/// inbox rather than to one named person, since a website visitor has no way
/// to pick a specific staff member.
/// </remarks>
public class ChatService : IChatService
{
    /// <summary>
    /// Comfortably under Kestrel/IIS's ~28 MB default request-body ceiling once
    /// Base64 inflation (~33%) is accounted for, and in line with the 3 MB cap
    /// this app already applies to a competition document.
    /// </summary>
    private const int MaxAttachmentBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Extension to expected content type. Deliberately short: chat attachments
    /// are for sharing a photo, a scanned form or a small document with the
    /// office, not a general file drop.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedAttachmentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".txt"] = "text/plain"
    };

    private readonly AppDbContext _db;
    private readonly IChatNotifier _notifier;
    private readonly IChatPresenceTracker _presence;
    private readonly ILogger<ChatService> _logger;

    private readonly string? _vapidPublicKey;
    private readonly string? _vapidPrivateKey;
    private readonly string? _vapidSubject;
    private readonly string? _firebaseServiceAccountPath;

    public ChatService(
        AppDbContext db, IChatNotifier notifier, IChatPresenceTracker presence,
        IConfiguration configuration, ILogger<ChatService> logger)
    {
        _db = db;
        _notifier = notifier;
        _presence = presence;
        _logger = logger;

        _vapidPublicKey = configuration["WebPush:PublicKey"];
        _vapidPrivateKey = configuration["WebPush:PrivateKey"];
        _vapidSubject = configuration["WebPush:Subject"];
        _firebaseServiceAccountPath = configuration["Firebase:ServiceAccountJsonPath"];
    }

    /* ---------------------------------------------------------- identity */

    private sealed record Identity(string Email, string DisplayName, string[] Roles, string PrimaryRole);

    private async Task<Identity?> ResolveIdentityAsync(string email, CancellationToken ct)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email && u.IsActive, ct);

        if (user is null) return null;

        var roles = UserRoles.Split(user.Roles);
        if (roles.Length == 0) return null;

        return new Identity(user.Email!, user.Name ?? user.Email!, roles, UserRoles.Primary(user.Roles));
    }

    /// <summary>Every role short of Admin/Editor that may freely message any other one of these.</summary>
    private static readonly string[] OpenChatRoles =
        [UserRoles.Student, UserRoles.Teacher, UserRoles.Receptionist];

    /// <summary>Whether two people, by the roles they each hold, are allowed to message each other directly.</summary>
    private static bool CanMessage(IReadOnlyCollection<string> rolesA, IReadOnlyCollection<string> rolesB)
    {
        if (rolesA.Contains(UserRoles.SuperAdmin) || rolesB.Contains(UserRoles.SuperAdmin)) return true;
        if (rolesA.Contains(UserRoles.Admin) || rolesA.Contains(UserRoles.Editor)) return true;
        if (rolesB.Contains(UserRoles.Admin) || rolesB.Contains(UserRoles.Editor)) return true;

        return rolesA.Any(OpenChatRoles.Contains) && rolesB.Any(OpenChatRoles.Contains);
    }

    private static bool IsStaff(IReadOnlyCollection<string> roles) =>
        roles.Contains(UserRoles.SuperAdmin) || roles.Contains(UserRoles.Admin) ||
        roles.Contains(UserRoles.Editor) || roles.Contains(UserRoles.Receptionist);

    /// <summary>22 URL-safe characters from 16 random bytes — short enough for a browser to hold, long enough to stand in for a password.</summary>
    private static string NewGuestToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static ChatMessageDto ToDto(
        ChatMessage m, string? viewerEmail, bool viewerIsGuestOnThisConversation = false, int? otherLastRead = null)
    {
        var isMine = viewerIsGuestOnThisConversation ? m.SenderEmail is null : m.SenderEmail == viewerEmail;

        return new(m.Id, m.ConversationId, m.SenderEmail, m.SenderDisplayName, m.SenderRole, m.Body, m.SentOn,
            isMine, m.AttachmentFileName, m.AttachmentContentType, m.AttachmentSize,
            IsSeen: isMine && otherLastRead is not null && otherLastRead >= m.Id);
    }

    /// <summary>
    /// Validates and decodes an attachment onto a not-yet-saved message. Returns
    /// an error string on the way out when the upload is refused; the message's
    /// four Attachment* fields are set directly on success, so there is nothing
    /// further for the caller to assign.
    /// </summary>
    private static string? ApplyAttachment(ChatMessage message, ChatAttachmentUpload? upload)
    {
        if (upload is null) return null;

        var fileName = Path.GetFileName(upload.FileName.Trim());
        if (fileName.Length == 0) return "That attachment has no file name.";
        if (fileName.Length > 150) fileName = fileName[^150..];

        var extension = Path.GetExtension(fileName);

        if (!AllowedAttachmentTypes.TryGetValue(extension, out var expectedContentType))
        {
            return $"'{extension}' attachments are not supported here. " +
                   "Photos, PDFs, Word/Excel documents and plain text files are.";
        }

        var payload = upload.Base64;
        var comma = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            payload = payload[(comma + 1)..];

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            return "That attachment could not be read.";
        }

        if (bytes.Length == 0) return "That attachment is empty.";

        if (bytes.Length > MaxAttachmentBytes)
        {
            return $"That attachment is {bytes.Length / 1024 / 1024} MB. " +
                   $"Please use one under {MaxAttachmentBytes / 1024 / 1024} MB.";
        }

        // Images are cheap to verify from their own bytes, so a mismatched
        // extension is caught rather than trusted (the same rule StudentPhoto
        // upload already applies). Office/PDF formats are trusted by extension
        // here — properly verifying those needs a format-specific parser, which
        // is disproportionate for a chat attachment.
        var sniffedImage = SniffImageType(bytes);
        if (sniffedImage is not null && !string.Equals(sniffedImage, expectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return $"The file contents ({sniffedImage}) do not match its '{extension}' extension.";
        }

        message.AttachmentFileName = fileName;
        message.AttachmentContentType = expectedContentType;
        message.AttachmentContent = Convert.ToBase64String(bytes);
        message.AttachmentSize = bytes.Length;

        return null;
    }

    private static string? SniffImageType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "image/png";

        if (bytes.Length >= 6 &&
            bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38 &&
            (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61)
            return "image/gif";

        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";

        // Not a recognised image — fine, it may genuinely be a PDF/Office file.
        return null;
    }

    /* -------------------------------------------------------------- guest */

    public async Task<Result<GuestStartedDto>> StartGuestConversationAsync(
        StartGuestChatRequest request, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var token = NewGuestToken();
        var name = request.Name.Trim();
        var contact = string.IsNullOrWhiteSpace(request.Contact) ? null : request.Contact.Trim();

        var conversation = new ChatConversation
        {
            Type = ChatConversationType.Guest,
            CreatedOn = now,
            LastMessageOn = now,
            IsActive = true
        };

        conversation.Participants.Add(new ChatParticipant
        {
            DisplayName = name,
            Role = "Guest",
            GuestToken = token,
            Contact = contact,
            JoinedOn = now
        });

        var message = new ChatMessage
        {
            SenderDisplayName = name,
            SenderRole = "Guest",
            Body = request.Message.Trim(),
            SentOn = now
        };

        conversation.Messages.Add(message);

        _db.ChatConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        await _notifier.GuestInboxChangedAsync(ct);

        return Result<GuestStartedDto>.Success(
            new GuestStartedDto(conversation.Id, token, ToDto(message, null, true)));
    }

    private async Task<ChatParticipant?> FindGuestAsync(int conversationId, string guestToken, CancellationToken ct) =>
        string.IsNullOrWhiteSpace(guestToken)
            ? null
            : await _db.ChatParticipants
                .FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.GuestToken == guestToken, ct);

    public async Task<Result<IReadOnlyList<ChatMessageDto>>> GetGuestMessagesAsync(
        int conversationId, string guestToken, CancellationToken ct = default)
    {
        var guest = await FindGuestAsync(conversationId, guestToken, ct);

        if (guest is null)
        {
            return Result<IReadOnlyList<ChatMessageDto>>.Failure(
                "That conversation could not be found.", ResultStatus.NotFound);
        }

        var messages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        return Result<IReadOnlyList<ChatMessageDto>>.Success(
            messages.Select(m => ToDto(m, null, true)).ToList());
    }

    public async Task<Result<ChatMessageDto>> SendGuestMessageAsync(
        int conversationId, SendGuestMessageRequest request, CancellationToken ct = default)
    {
        var guest = await FindGuestAsync(conversationId, request.GuestToken, ct);

        if (guest is null)
        {
            return Result<ChatMessageDto>.Failure(
                "That conversation could not be found.", ResultStatus.NotFound);
        }

        var now = DateTime.Now;

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderDisplayName = guest.DisplayName,
            SenderRole = "Guest",
            Body = request.Body.Trim(),
            SentOn = now
        };

        var attachmentError = ApplyAttachment(message, request.Attachment);
        if (attachmentError is not null)
            return Result<ChatMessageDto>.Failure(attachmentError);

        _db.ChatMessages.Add(message);

        var conversation = await _db.ChatConversations.FirstAsync(c => c.Id == conversationId, ct);
        conversation.LastMessageOn = now;

        await _db.SaveChangesAsync(ct);

        var dto = ToDto(message, null, true);
        await _notifier.MessageReceivedAsync(conversationId, dto, ct);

        // No participant is auto-joined for an unclaimed thread, so there is no
        // per-conversation recipient list to read yet — reach the whole front
        // desk instead, the same "shared inbox" call StaffLastReadMessageId
        // already makes for marking guest messages read.
        var allActive = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null)
            .Select(u => new { u.Email, u.Roles })
            .ToListAsync(ct);

        var staffEmails = allActive
            .Where(u => IsStaff(UserRoles.Split(u.Roles)))
            .Select(u => u.Email!)
            .ToList();

        await PushToAsync(staffEmails, $"{guest.DisplayName} · Website visitor",
            PushPreview(message.Body, message.AttachmentFileName), ct);

        return Result<ChatMessageDto>.Success(dto);
    }

    /* --------------------------------------------------------- directory */

    public async Task<Result<IReadOnlyList<ChatDirectoryEntryDto>>> GetDirectoryAsync(
        string email, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<IReadOnlyList<ChatDirectoryEntryDto>>.Failure("No account found.", ResultStatus.Unauthorized);

        var everyone = await _db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Email != null && u.Email != me.Email)
            .Select(u => new { u.Email, u.Name, u.Roles, u.LastSeenOn })
            .ToListAsync(ct);

        var entries = everyone
            // 8 rows in production carry no email at all — placeholder teacher
            // rows the migration created for someone deleted from the legacy
            // system while students still referenced them (see
            // TeacherService.DeleteAsync's own remark on the same defect), plus
            // a few students who were never given a login. Nothing a chat could
            // actually reach — StartDirectChat needs a real address — so they
            // showed up as an unlabelled "?" entry with no one behind it. A
            // blank *name* is far more common (577 rows, including plenty of
            // real accounts that simply never had one typed in) and must keep
            // falling back to the email below, not be filtered out here.
            .Where(u => !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => new
            {
                u.Email,
                DisplayName = u.Name ?? u.Email!,
                Roles = UserRoles.Split(u.Roles),
                Primary = UserRoles.Primary(u.Roles),
                u.LastSeenOn
            })
            .Where(u => u.Roles.Length > 0 && CanMessage(me.Roles, u.Roles))
            .Select(u => new ChatDirectoryEntryDto(u.Email!, u.DisplayName, u.Primary, u.LastSeenOn))
            .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Result<IReadOnlyList<ChatDirectoryEntryDto>>.Success(entries);
    }

    /* ----------------------------------------------------- conversations */

    private async Task<ChatConversationSummaryDto> ToSummaryAsync(
        ChatConversation c, string? viewerEmail, CancellationToken ct)
    {
        var participants = await _db.ChatParticipants.AsNoTracking()
            .Where(p => p.ConversationId == c.Id)
            .Select(p => new ChatParticipantDto(p.UserEmail, p.DisplayName, p.Role, p.Contact))
            .ToListAsync(ct);

        var last = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == c.Id)
            .OrderByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);

        int unread;

        if (c.Type == ChatConversationType.Guest)
        {
            // Shared-inbox read state — see the remark on StaffLastReadMessageId.
            // Used regardless of who is asking (even the guest-inbox listing,
            // which has no single viewer), since one "read" applies to everyone.
            unread = await _db.ChatMessages.AsNoTracking()
                .Where(m => m.ConversationId == c.Id
                            && m.SenderEmail == null
                            && (c.StaffLastReadMessageId == null || m.Id > c.StaffLastReadMessageId))
                .CountAsync(ct);
        }
        else
        {
            var myLastRead = viewerEmail is null
                ? null
                : await _db.ChatParticipants.AsNoTracking()
                    .Where(p => p.ConversationId == c.Id && p.UserEmail == viewerEmail)
                    .Select(p => (int?)p.LastReadMessageId)
                    .FirstOrDefaultAsync(ct);

            unread = viewerEmail is null
                ? 0
                : await _db.ChatMessages.AsNoTracking()
                    .Where(m => m.ConversationId == c.Id
                                && m.SenderEmail != viewerEmail
                                && (myLastRead == null || m.Id > myLastRead))
                    .CountAsync(ct);
        }

        var preview = last is null
            ? null
            : last.Body.Length > 120 ? last.Body[..120] + "…" : last.Body;

        return new ChatConversationSummaryDto(
            c.Id, c.Type.ToString(), c.Title, participants, preview, c.LastMessageOn, unread);
    }

    public async Task<Result<IReadOnlyList<ChatConversationSummaryDto>>> GetMyConversationsAsync(
        string email, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<IReadOnlyList<ChatConversationSummaryDto>>.Failure("No account found.", ResultStatus.Unauthorized);

        var ids = await _db.ChatParticipants.AsNoTracking()
            .Where(p => p.UserEmail == email)
            .Select(p => p.ConversationId)
            .Distinct()
            .ToListAsync(ct);

        var conversations = await _db.ChatConversations.AsNoTracking()
            .Where(c => ids.Contains(c.Id) && c.IsActive)
            .OrderByDescending(c => c.LastMessageOn ?? c.CreatedOn)
            .ToListAsync(ct);

        var summaries = new List<ChatConversationSummaryDto>();
        foreach (var c in conversations)
            summaries.Add(await ToSummaryAsync(c, email, ct));

        return Result<IReadOnlyList<ChatConversationSummaryDto>>.Success(summaries);
    }

    public async Task<Result<IReadOnlyList<ChatConversationSummaryDto>>> GetGuestInboxAsync(
        CancellationToken ct = default)
    {
        var conversations = await _db.ChatConversations.AsNoTracking()
            .Where(c => c.Type == ChatConversationType.Guest && c.IsActive)
            .OrderByDescending(c => c.LastMessageOn ?? c.CreatedOn)
            .ToListAsync(ct);

        var summaries = new List<ChatConversationSummaryDto>();
        foreach (var c in conversations)
            summaries.Add(await ToSummaryAsync(c, null, ct));

        return Result<IReadOnlyList<ChatConversationSummaryDto>>.Success(summaries);
    }

    public async Task<Result<int>> StartOrGetDirectConversationAsync(
        string email, string targetEmail, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<int>.Failure("No account found.", ResultStatus.Unauthorized);

        var target = await ResolveIdentityAsync(targetEmail.Trim(), ct);
        if (target is null)
            return Result<int>.Failure("That person could not be found.", ResultStatus.NotFound);

        if (string.Equals(me.Email, target.Email, StringComparison.OrdinalIgnoreCase))
            return Result<int>.Failure("You cannot start a chat with yourself.");

        if (!CanMessage(me.Roles, target.Roles))
        {
            return Result<int>.Failure(
                $"{target.DisplayName} is not someone you can message directly.", ResultStatus.Forbidden);
        }

        // Reuse the existing thread rather than opening a second one every time
        // the same two people start a new chat.
        var existingId = await _db.ChatConversations.AsNoTracking()
            .Where(c => c.Type == ChatConversationType.Direct)
            .Where(c => _db.ChatParticipants.Any(p => p.ConversationId == c.Id && p.UserEmail == me.Email))
            .Where(c => _db.ChatParticipants.Any(p => p.ConversationId == c.Id && p.UserEmail == target.Email))
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (existingId is int found)
            return Result<int>.Success(found);

        var now = DateTime.Now;

        var conversation = new ChatConversation
        {
            Type = ChatConversationType.Direct,
            CreatedBy = me.Email,
            CreatedOn = now,
            LastMessageOn = now,
            IsActive = true
        };

        conversation.Participants.Add(new ChatParticipant
        {
            UserEmail = me.Email, DisplayName = me.DisplayName, Role = me.PrimaryRole, JoinedOn = now
        });
        conversation.Participants.Add(new ChatParticipant
        {
            UserEmail = target.Email, DisplayName = target.DisplayName, Role = target.PrimaryRole, JoinedOn = now
        });

        _db.ChatConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        await _notifier.ConversationsChangedAsync([me.Email, target.Email], ct);

        return Result<int>.Success(conversation.Id);
    }

    public async Task<Result<int>> StartGroupChatAsync(
        string email, StartGroupChatRequest request, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<int>.Failure("No account found.", ResultStatus.Unauthorized);

        var memberEmails = request.MemberEmails
            .Select(e => e.Trim())
            .Where(e => !string.Equals(e, me.Email, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var members = new List<Identity>();
        foreach (var memberEmail in memberEmails)
        {
            var member = await ResolveIdentityAsync(memberEmail, ct);
            if (member is null)
                return Result<int>.Failure($"'{memberEmail}' could not be found.", ResultStatus.NotFound);

            if (!CanMessage(me.Roles, member.Roles))
            {
                return Result<int>.Failure(
                    $"{member.DisplayName} is not someone you can add to a group.", ResultStatus.Forbidden);
            }

            members.Add(member);
        }

        if (members.Count == 0)
            return Result<int>.Failure("Add at least one other member.");

        var now = DateTime.Now;

        var conversation = new ChatConversation
        {
            Type = ChatConversationType.Group,
            Title = request.Title.Trim(),
            CreatedBy = me.Email,
            CreatedOn = now,
            LastMessageOn = now,
            IsActive = true
        };

        conversation.Participants.Add(new ChatParticipant
        {
            UserEmail = me.Email, DisplayName = me.DisplayName, Role = me.PrimaryRole, JoinedOn = now
        });

        foreach (var member in members)
        {
            conversation.Participants.Add(new ChatParticipant
            {
                UserEmail = member.Email, DisplayName = member.DisplayName, Role = member.PrimaryRole, JoinedOn = now
            });
        }

        _db.ChatConversations.Add(conversation);
        await _db.SaveChangesAsync(ct);

        await _notifier.ConversationsChangedAsync(
            members.Select(m => m.Email).Append(me.Email), ct);

        return Result<int>.Success(conversation.Id);
    }

    public async Task<Result<bool>> AddGroupMemberAsync(
        string email, int conversationId, AddGroupMemberRequest request, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<bool>.Failure("No account found.", ResultStatus.Unauthorized);

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.Type == ChatConversationType.Group, ct);

        if (conversation is null)
            return Result<bool>.Failure("That group could not be found.", ResultStatus.NotFound);

        var iAmMember = await _db.ChatParticipants.AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId && p.UserEmail == me.Email, ct);

        if (!iAmMember)
            return Result<bool>.Failure("You are not a member of that group.", ResultStatus.Forbidden);

        var newMemberEmail = request.MemberEmail.Trim();

        var alreadyIn = await _db.ChatParticipants.AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId
                           && p.UserEmail!.ToLower() == newMemberEmail.ToLower(), ct);

        if (alreadyIn)
            return Result<bool>.Failure("They are already in this group.", ResultStatus.Conflict);

        var newMember = await ResolveIdentityAsync(newMemberEmail, ct);
        if (newMember is null)
            return Result<bool>.Failure("That person could not be found.", ResultStatus.NotFound);

        if (!CanMessage(me.Roles, newMember.Roles))
        {
            return Result<bool>.Failure(
                $"{newMember.DisplayName} is not someone you can add to a group.", ResultStatus.Forbidden);
        }

        _db.ChatParticipants.Add(new ChatParticipant
        {
            ConversationId = conversationId,
            UserEmail = newMember.Email,
            DisplayName = newMember.DisplayName,
            Role = newMember.PrimaryRole,
            JoinedOn = DateTime.Now
        });

        await _db.SaveChangesAsync(ct);
        await _notifier.ConversationsChangedAsync([newMember.Email], ct);

        return Result<bool>.Success(true);
    }

    /* --------------------------------------------------------- messages */

    /// <summary>
    /// True when this signed-in person may read or reply in this conversation —
    /// either they are already a participant, or they hold a staff role and the
    /// conversation is the Guest kind, which any of Admin/Editor/Receptionist may
    /// pick up.
    /// </summary>
    private async Task<bool> CanAccessAsync(Identity me, int conversationId, CancellationToken ct)
    {
        var isParticipant = await _db.ChatParticipants.AsNoTracking()
            .AnyAsync(p => p.ConversationId == conversationId && p.UserEmail == me.Email, ct);

        if (isParticipant) return true;

        if (!IsStaff(me.Roles)) return false;

        return await _db.ChatConversations.AsNoTracking()
            .AnyAsync(c => c.Id == conversationId && c.Type == ChatConversationType.Guest, ct);
    }

    public async Task<Result<IReadOnlyList<ChatMessageDto>>> GetMessagesAsync(
        string email, int conversationId, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<IReadOnlyList<ChatMessageDto>>.Failure("No account found.", ResultStatus.Unauthorized);

        if (!await CanAccessAsync(me, conversationId, ct))
        {
            return Result<IReadOnlyList<ChatMessageDto>>.Failure(
                "That conversation is not available to you.", ResultStatus.Forbidden);
        }

        var messages = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        // Read receipts, Direct only (see ChatMessageDto.IsSeen) — the other
        // participant's read position, so a "Seen" tick can be shown against
        // each of the caller's own messages up to it.
        var conversationType = await _db.ChatConversations.AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => c.Type)
            .FirstOrDefaultAsync(ct);

        int? otherLastRead = null;
        if (conversationType == ChatConversationType.Direct)
        {
            otherLastRead = await _db.ChatParticipants.AsNoTracking()
                .Where(p => p.ConversationId == conversationId && p.UserEmail != me.Email)
                .Select(p => p.LastReadMessageId)
                .FirstOrDefaultAsync(ct);
        }

        return Result<IReadOnlyList<ChatMessageDto>>.Success(
            messages.Select(m => ToDto(m, me.Email, otherLastRead: otherLastRead)).ToList());
    }

    public async Task<Result<ChatMessageDto>> SendMessageAsync(
        string email, int conversationId, SendMessageRequest request, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<ChatMessageDto>.Failure("No account found.", ResultStatus.Unauthorized);

        var conversation = await _db.ChatConversations
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.IsActive, ct);

        if (conversation is null)
            return Result<ChatMessageDto>.Failure("That conversation could not be found.", ResultStatus.NotFound);

        var isParticipant = await _db.ChatParticipants
            .AnyAsync(p => p.ConversationId == conversationId && p.UserEmail == me.Email, ct);

        if (!isParticipant)
        {
            // A staff member replying to a guest conversation for the first time
            // — join them on the spot, the way opening the thread implicitly
            // claims it without a separate "assign to me" step to forget.
            if (IsStaff(me.Roles) && conversation.Type == ChatConversationType.Guest)
            {
                _db.ChatParticipants.Add(new ChatParticipant
                {
                    ConversationId = conversationId,
                    UserEmail = me.Email,
                    DisplayName = me.DisplayName,
                    Role = me.PrimaryRole,
                    JoinedOn = DateTime.Now
                });
            }
            else
            {
                return Result<ChatMessageDto>.Failure(
                    "That conversation is not available to you.", ResultStatus.Forbidden);
            }
        }

        var now = DateTime.Now;

        var message = new ChatMessage
        {
            ConversationId = conversationId,
            SenderEmail = me.Email,
            SenderDisplayName = me.DisplayName,
            SenderRole = me.PrimaryRole,
            Body = request.Body.Trim(),
            SentOn = now
        };

        var attachmentError = ApplyAttachment(message, request.Attachment);
        if (attachmentError is not null)
            return Result<ChatMessageDto>.Failure(attachmentError);

        _db.ChatMessages.Add(message);
        conversation.LastMessageOn = now;

        await _db.SaveChangesAsync(ct);

        var dto = ToDto(message, me.Email);
        await _notifier.MessageReceivedAsync(conversationId, dto, ct);

        var recipientEmails = await _db.ChatParticipants.AsNoTracking()
            .Where(p => p.ConversationId == conversationId && p.UserEmail != null && p.UserEmail != me.Email)
            .Select(p => p.UserEmail!)
            .ToListAsync(ct);

        var title = conversation.Type == ChatConversationType.Group
            ? $"{me.DisplayName} in {conversation.Title}"
            : me.DisplayName;

        await PushToAsync(recipientEmails, title, PushPreview(message.Body, message.AttachmentFileName), ct);

        return Result<ChatMessageDto>.Success(dto);
    }

    public async Task<Result<ChatAttachmentDto>> GetAttachmentAsync(
        string email, int conversationId, int messageId, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        if (me is null)
            return Result<ChatAttachmentDto>.Failure("No account found.", ResultStatus.Unauthorized);

        if (!await CanAccessAsync(me, conversationId, ct))
        {
            return Result<ChatAttachmentDto>.Failure(
                "That conversation is not available to you.", ResultStatus.Forbidden);
        }

        var message = await _db.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, ct);

        return ToAttachmentResult(message);
    }

    public async Task<Result<ChatAttachmentDto>> GetGuestAttachmentAsync(
        int conversationId, int messageId, string guestToken, CancellationToken ct = default)
    {
        var guest = await FindGuestAsync(conversationId, guestToken, ct);

        if (guest is null)
        {
            return Result<ChatAttachmentDto>.Failure(
                "That conversation could not be found.", ResultStatus.NotFound);
        }

        var message = await _db.ChatMessages.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId && m.ConversationId == conversationId, ct);

        return ToAttachmentResult(message);
    }

    private static Result<ChatAttachmentDto> ToAttachmentResult(ChatMessage? message)
    {
        if (message?.AttachmentContent is null || message.AttachmentFileName is null || message.AttachmentContentType is null)
            return Result<ChatAttachmentDto>.Failure("That attachment could not be found.", ResultStatus.NotFound);

        return Result<ChatAttachmentDto>.Success(new ChatAttachmentDto(
            message.AttachmentFileName,
            message.AttachmentContentType,
            Convert.FromBase64String(message.AttachmentContent)));
    }

    public async Task<Result<bool>> MarkReadAsync(
        string email, int conversationId, CancellationToken ct = default)
    {
        var participant = await _db.ChatParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.UserEmail == email, ct);

        if (participant is null)
        {
            // Staff opening an unclaimed Guest thread from the shared inbox is not
            // a participant yet — that alone doesn't mean nothing gets marked
            // read: the shared StaffLastReadMessageId on the conversation itself
            // is what the guest-inbox badge counts against, and it needs moving
            // here or "read" would never happen until someone actually replies.
            var me = await ResolveIdentityAsync(email, ct);

            var guestConversation = me is not null && IsStaff(me.Roles)
                ? await _db.ChatConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId && c.Type == ChatConversationType.Guest, ct)
                : null;

            if (guestConversation is null)
                return Result<bool>.Failure("You are not a participant of that conversation.", ResultStatus.Forbidden);

            var guestLastId = await _db.ChatMessages.AsNoTracking()
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Id)
                .Select(m => (int?)m.Id)
                .FirstOrDefaultAsync(ct);

            if (guestLastId is not null
                && (guestConversation.StaffLastReadMessageId is null || guestLastId > guestConversation.StaffLastReadMessageId))
            {
                guestConversation.StaffLastReadMessageId = guestLastId;
                await _db.SaveChangesAsync(ct);
            }

            return Result<bool>.Success(true);
        }

        var lastId = await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Id)
            .Select(m => (int?)m.Id)
            .FirstOrDefaultAsync(ct);

        if (lastId is not null)
        {
            participant.LastReadMessageId = lastId;

            // A staff member who has already replied once IS a participant, so
            // they took the branch above rather than this one — but the shared
            // guest-inbox marker still needs to move with their own read state,
            // the same as it does for the never-replied-yet case.
            if (participant.Role != "Guest")
            {
                var conversation = await _db.ChatConversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId && c.Type == ChatConversationType.Guest, ct);

                if (conversation is not null
                    && (conversation.StaffLastReadMessageId is null || lastId > conversation.StaffLastReadMessageId))
                {
                    conversation.StaffLastReadMessageId = lastId;
                }
            }

            await _db.SaveChangesAsync(ct);

            // Lets a sender's already-open thread flip to "Seen" live, rather
            // than only the next time they reopen it.
            await _notifier.MessagesReadAsync(conversationId, email, lastId.Value, ct);
        }

        return Result<bool>.Success(true);
    }

    public async Task<bool> CanCallAsync(string email, string targetEmail, CancellationToken ct = default)
    {
        var me = await ResolveIdentityAsync(email, ct);
        var target = await ResolveIdentityAsync(targetEmail, ct);
        return me is not null && target is not null && CanMessage(me.Roles, target.Roles);
    }

    public Task<Result<ChatPresenceSnapshotDto>> GetOnlinePresenceAsync(
        string email, CancellationToken ct = default) =>
        Task.FromResult(Result<ChatPresenceSnapshotDto>.Success(
            new ChatPresenceSnapshotDto(
                _presence.OnlineEmails.ToList(),
                _presence.AwayEmails.ToList(),
                _presence.InCallEmails.ToList())));

    /* ------------------------------------------------------ push notify */

    public async Task<Result<bool>> SubscribePushAsync(
        string email, ChatPushSubscribeRequest request, CancellationToken ct = default)
    {
        var existing = await _db.ChatPushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, ct);

        if (existing is not null)
        {
            // Re-subscribing the same browser (a token refresh, or simply
            // asking again) re-points it at whoever is signed in now, rather
            // than leaving it attached to a previous user of a shared machine.
            existing.Email = email;
            existing.P256dh = request.P256dh;
            existing.Auth = request.Auth;
        }
        else
        {
            _db.ChatPushSubscriptions.Add(new ChatPushSubscription
            {
                Email = email,
                Endpoint = request.Endpoint,
                P256dh = request.P256dh,
                Auth = request.Auth,
                CreatedOn = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> UnsubscribePushAsync(
        string email, string endpoint, CancellationToken ct = default)
    {
        var existing = await _db.ChatPushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint && s.Email == email, ct);

        if (existing is not null)
        {
            _db.ChatPushSubscriptions.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> SubscribeFcmAsync(
        string email, ChatFcmSubscribeRequest request, CancellationToken ct = default)
    {
        var existing = await _db.ChatFcmSubscriptions
            .FirstOrDefaultAsync(s => s.Token == request.Token, ct);

        if (existing is not null)
        {
            // Same reasoning as SubscribePushAsync: re-registering re-points the
            // token at whoever is signed in now, not whoever last was.
            existing.Email = email;
        }
        else
        {
            _db.ChatFcmSubscriptions.Add(new ChatFcmSubscription
            {
                Email = email,
                Token = request.Token,
                CreatedOn = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> UnsubscribeFcmAsync(
        string email, string token, CancellationToken ct = default)
    {
        var existing = await _db.ChatFcmSubscriptions
            .FirstOrDefaultAsync(s => s.Token == token && s.Email == email, ct);

        if (existing is not null)
        {
            _db.ChatFcmSubscriptions.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Pushes a notification to everyone in <paramref name="emails"/> who has
    /// a subscription of either kind — a browser (Web Push) and/or the
    /// installed app (FCM), whichever this person has registered — deliberately
    /// unconditional on whether they currently hold a live SignalR connection
    /// too, since the whole point is reaching a closed tab or a locked phone,
    /// not just a backgrounded one. A courtesy, same as presence: a failed
    /// send here must never fail the message it was about, so every error is
    /// caught and logged, not thrown.
    /// </summary>
    private async Task PushToAsync(
        IEnumerable<string> emails, string title, string body, CancellationToken ct)
    {
        var recipients = emails.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (recipients.Count == 0) return;

        await PushWebAsync(recipients, title, body, ct);
        await PushFcmAsync(recipients, title, body, ct);
    }

    /// <summary>
    /// The call-specific push. Unlike a message push it carries the call's
    /// fields as <c>data</c>, so the app opened from the notification rejoins
    /// the ringing call instead of just showing the chat list, and it targets
    /// the calls channel so a call rings differently from a message. FCM only —
    /// a browser tab that is closed cannot answer a WebRTC call anyway.
    /// </summary>
    public async Task NotifyCallPushAsync(
        string targetEmail, string fromEmail, string fromName, string callId, string kind,
        CancellationToken ct = default)
    {
        if (!EnsureFirebaseInitialized()) return;
        if (string.IsNullOrWhiteSpace(targetEmail)) return;

        var subscriptions = await _db.ChatFcmSubscriptions
            .Where(s => s.Email == targetEmail)
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        var messaging = FirebaseMessaging.DefaultInstance;
        var stale = new List<ChatFcmSubscription>();
        var label = kind == "video" ? "Incoming video call" : "Incoming call";

        foreach (var sub in subscriptions)
        {
            try
            {
                await messaging.SendAsync(new Message
                {
                    Token = sub.Token,
                    Notification = new Notification { Title = label, Body = fromName },
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = "call",
                        ["callId"] = callId,
                        ["fromEmail"] = fromEmail,
                        ["fromName"] = fromName,
                        ["kind"] = kind
                    },
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            // Must match AvijanApp.CALL_CHANNEL_ID — that channel now sets
                            // its own ringtone-stream sound (see its own comment for why
                            // "_v3"), so DefaultSound must be off or FCM overrides it back
                            // to the notification-stream default on delivery.
                            ChannelId = "incoming_calls_v3",
                            DefaultVibrateTimings = true,
                            DefaultSound = false,
                            Priority = NotificationPriority.MAX
                        }
                    }
                }, ct);
            }
            catch (FirebaseMessagingException ex) when (
                ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                stale.Add(sub);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat FCM call push failed for {Email}", sub.Email);
            }
        }

        if (stale.Count > 0) _db.ChatFcmSubscriptions.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
    }

    private async Task PushWebAsync(
        IReadOnlyList<string> recipients, string title, string body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_vapidPublicKey) || string.IsNullOrEmpty(_vapidPrivateKey)
            || string.IsNullOrEmpty(_vapidSubject))
            return;

        var subscriptions = await _db.ChatPushSubscriptions
            .Where(s => recipients.Contains(s.Email))
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        var vapid = new VapidDetails(_vapidSubject, _vapidPublicKey, _vapidPrivateKey);
        var client = new WebPushClient();
        var payload = JsonSerializer.Serialize(new { title, body });
        var stale = new List<ChatPushSubscription>();

        foreach (var sub in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(
                    new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth), payload, vapid, ct);
            }
            catch (WebPushException ex) when (
                ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The browser has revoked or forgotten this subscription (uninstalled,
                // cleared site data, notifications turned off at the OS level) — the
                // push service is telling us plainly to stop trying, so stop.
                stale.Add(sub);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat web push notification failed for {Email}", sub.Email);
            }
        }

        if (stale.Count > 0) _db.ChatPushSubscriptions.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The installed Android app's leg of the same courtesy — a device
    /// registered with Firebase Cloud Messaging rather than a browser's Web
    /// Push subscription. Skipped entirely, silently, until a real Firebase
    /// service account is configured (see appsettings' Firebase section) —
    /// same "not configured yet, so do nothing" rule PushWebAsync already
    /// follows for the VAPID keys.
    /// </summary>
    private async Task PushFcmAsync(
        IReadOnlyList<string> recipients, string title, string body, CancellationToken ct)
    {
        if (!EnsureFirebaseInitialized()) return;

        var subscriptions = await _db.ChatFcmSubscriptions
            .Where(s => recipients.Contains(s.Email))
            .ToListAsync(ct);

        if (subscriptions.Count == 0) return;

        var messaging = FirebaseMessaging.DefaultInstance;
        var stale = new List<ChatFcmSubscription>();

        foreach (var sub in subscriptions)
        {
            try
            {
                await messaging.SendAsync(new Message
                {
                    Token = sub.Token,
                    Notification = new Notification { Title = title, Body = body },

                    // From Android 8 the phone vibrates for a notification only
                    // if its channel says so, and only a high-priority message
                    // gets the heads-up banner and the buzz while the screen is
                    // off. ChannelId must match the one the app creates
                    // (PushNotificationsService.CHANNEL_ID); DefaultVibrateTimings
                    // covers the Android 7 and older phones where the payload,
                    // not a channel, still decides.
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            ChannelId = "chat_messages_v2",
                            DefaultVibrateTimings = true,
                            DefaultSound = true,
                            Priority = NotificationPriority.HIGH
                        }
                    }
                }, ct);
            }
            catch (FirebaseMessagingException ex) when (
                ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                // Firebase's equivalent of the browser's Gone/NotFound: the app
                // was uninstalled, or this token was replaced by a newer one —
                // either way, stop addressing pushes to it.
                stale.Add(sub);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat FCM push notification failed for {Email}", sub.Email);
            }
        }

        if (stale.Count > 0) _db.ChatFcmSubscriptions.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
    }

    private static readonly object FirebaseInitLock = new();
    private static bool _firebaseInitAttempted;

    /// <summary>
    /// Firebase's SDK is process-wide (one <c>FirebaseApp.DefaultInstance</c>
    /// for the whole app, not one per request), so this only ever really runs
    /// once — every call after the first sees it already set up (or already
    /// given up) and returns immediately. Given up permanently, not retried
    /// per call: a missing or invalid service account file is a deployment
    /// problem to fix and restart for, not something that becomes valid
    /// mid-run.
    /// </summary>
    private bool EnsureFirebaseInitialized()
    {
        if (FirebaseAdmin.FirebaseApp.DefaultInstance is not null) return true;
        if (_firebaseInitAttempted) return false;

        lock (FirebaseInitLock)
        {
            if (FirebaseAdmin.FirebaseApp.DefaultInstance is not null) return true;
            if (_firebaseInitAttempted) return false;

            _firebaseInitAttempted = true;

            if (string.IsNullOrEmpty(_firebaseServiceAccountPath) || !File.Exists(_firebaseServiceAccountPath))
                return false;

            try
            {
                FirebaseAdmin.FirebaseApp.Create(new FirebaseAdmin.AppOptions
                {
                    Credential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(_firebaseServiceAccountPath)
                });
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Firebase Cloud Messaging could not be initialized from {Path}", _firebaseServiceAccountPath);
                return false;
            }
        }
    }

    /// <summary>Trims a message body to something that reads as a preview, not the whole thing.</summary>
    private static string PushPreview(string body, string? attachmentFileName) =>
        body.Length > 0
            ? (body.Length > 120 ? body[..120] + "…" : body)
            : attachmentFileName is not null ? $"📎 {attachmentFileName}" : "Sent a message";
}
