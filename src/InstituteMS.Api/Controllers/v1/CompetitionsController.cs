using InstituteMS.Application.Common;
using InstituteMS.Application.Competitions;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The yearly Drawing &amp; Dance competition.
/// </summary>
/// <remarks>
/// <para>Two audiences in one controller. The advertisement and the registration
/// form are open to anyone — entrants are children from any school, with no sign-in
/// and no connection to the centre's own students. Everything else is the office's.</para>
/// </remarks>
[Route("api/v1/competitions")]
[Tags("Competition")]
public class CompetitionsController : ApiControllerBase
{
    /// <summary>Who may see the year-wise report and confirm a payment.</summary>
    private const string Office = $"{UserRoles.Admin},{UserRoles.Editor},{UserRoles.Receptionist}";

    private readonly ICompetitionService _competitions;

    public CompetitionsController(ICompetitionService competitions) => _competitions = competitions;

    /* ============================================================== public */

    /// <summary>
    /// The advertisement for the current year, in English and Bengali, with the
    /// divisions, groups, fees, times and rules. Null when nothing is set up.
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CompetitionPublicDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Public(CancellationToken ct)
        => Envelope(await _competitions.GetPublicAsync(ct));

    /// <summary>
    /// Takes one registration from the public and issues its numbers.
    /// </summary>
    /// <remarks>
    /// Anonymous by design. The amount, the division, and whether paying at the venue
    /// is allowed are all decided server-side from the competition's own settings —
    /// nothing the browser sends about money is trusted.
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CompetitionRegisteredDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CompetitionRegisteredDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] CompetitionRegisterRequest request, CancellationToken ct)
        => Envelope(await _competitions.RegisterAsync(request, ClientIp, ct));

    /// <summary>
    /// The chest number sheet, as a PDF the entrant prints and pins on.
    /// </summary>
    /// <remarks>
    /// Anonymous: the entrant has no account, and identifies themselves with the
    /// registration number plus the phone number on their form. Available only once
    /// the office has confirmed the payment.
    /// </remarks>
    [HttpGet("chest-card")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChestCard(
        [FromQuery] string registrationNo, [FromQuery] string phone, CancellationToken ct)
    {
        var result = await _competitions.GetChestCardAsync(registrationNo, phone, ct);

        if (!result.Succeeded || result.Value is null)
            return Envelope(Result<bool>.Failure(result.Error!, result.Status));

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /* ==================================================== save and resume */

    /// <summary>
    /// The quick-start step behind "Register now": a name, a phone and a password,
    /// which issues a registration id so the rest of the form can be finished later.
    /// </summary>
    /// <remarks>
    /// Anonymous, and reserves nothing — no seat, no capacity, no chest number.
    /// Notifies Admin and Editor on the dashboard so an abandoned start can be
    /// followed up by phone.
    /// </remarks>
    [HttpPost("drafts")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CompetitionDraftStartedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CompetitionDraftStartedDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StartDraft(
        [FromBody] CompetitionDraftStartRequest request, CancellationToken ct)
        => Envelope(await _competitions.StartDraftAsync(request, ct));

    /// <summary>Resumes a draft with its registration id and password.</summary>
    [HttpPost("drafts/login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CompetitionDraftDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CompetitionDraftDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResumeDraft(
        [FromBody] CompetitionDraftLoginRequest request, CancellationToken ct)
        => Envelope(await _competitions.ResumeDraftAsync(request, ct));

    /// <summary>Changes the password on a draft — the visitor's own, not the office's.</summary>
    [HttpPost("drafts/change-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeDraftPassword(
        [FromBody] CompetitionDraftChangePasswordRequest request, CancellationToken ct)
        => Envelope(await _competitions.ChangeDraftPasswordAsync(request, ct));

    /// <summary>
    /// Emails a reset code for a draft that cannot sign in at all. Always reports
    /// success — whether the code matched a draft is not this response's to reveal.
    /// </summary>
    [HttpPost("drafts/forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForgotDraftPassword(
        [FromBody] CompetitionDraftForgotPasswordRequest request, CancellationToken ct)
        => Envelope(
            await _competitions.ForgotDraftPasswordAsync(request, ct),
            "If that registration has an email on file, a reset code has been sent to it.");

    /// <summary>Spends the emailed code to set a new draft password without the old one.</summary>
    [HttpPost("drafts/reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetDraftPassword(
        [FromBody] CompetitionDraftResetPasswordRequest request, CancellationToken ct)
        => Envelope(await _competitions.ResetDraftPasswordAsync(request, ct), "Password reset. Please sign in.");

    /// <summary>
    /// Every draft for the year, started or completed — the office's follow-up list
    /// for someone who began a registration and never finished it.
    /// </summary>
    [HttpGet("drafts")]
    [Authorize(Roles = Office)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CompetitionDraftRowDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Drafts([FromQuery] int? competitionId, CancellationToken ct)
        => Envelope(await _competitions.GetDraftsAsync(competitionId, ct));

    /* ============================================================== office */

    /// <summary>
    /// Year-wise report of every registration.
    /// </summary>
    /// <remarks>
    /// Admin, Editor and Receptionist. The Editor role is the one the owner asked to
    /// be given to Arkita Nath, who runs the programme section.
    /// </remarks>
    [HttpGet("report")]
    [Authorize(Roles = Office)]
    [ProducesResponseType(typeof(ApiResponse<CompetitionReportDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(
        [FromQuery] CompetitionReportQuery query, CancellationToken ct)
        => Envelope(await _competitions.GetReportAsync(query, ct));

    /// <summary>
    /// Confirms or rejects a payment, after somebody has looked at the account.
    /// </summary>
    /// <remarks>
    /// This is the whole of "verification". The UPI QR is static and no gateway
    /// reports back, so nothing here can ask the bank whether the money arrived —
    /// what the portal does on its own is refuse a reference another entrant has
    /// already used. The rest is a person checking, exactly as the students' own
    /// online fee payments are confirmed.
    /// </remarks>
    [HttpPatch("registrations/{applicantId:int}/payment")]
    [Authorize(Roles = $"{UserRoles.Admin},{UserRoles.Receptionist}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetPaymentStatus(
        int applicantId, [FromQuery] string status, CancellationToken ct)
        => Envelope(
            await _competitions.SetPaymentStatusAsync(applicantId, status, CurrentUserName, ct),
            "Payment updated.");

    /// <summary>
    /// Cancels a registration and refunds it in full.
    /// </summary>
    /// <remarks>
    /// Admin only. A refund is money leaving the centre, and the owner asked for it
    /// to be the administrator's decision: "if admin wants admin can cancel
    /// registration if criteria not matching properly". The dance place and the
    /// chest number return to the group.
    /// </remarks>
    [HttpPatch("registrations/{applicantId:int}/cancel")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(
        int applicantId, [FromQuery] string? reason, CancellationToken ct)
        => Envelope(
            await _competitions.CancelAsync(applicantId, reason, CurrentUserName, ct),
            "Registration cancelled. The full amount is to be refunded.");

    /// <summary>A document attached to one registration.</summary>
    [HttpGet("registrations/{applicantId:int}/document")]
    [Authorize(Roles = Office)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Document(
        int applicantId, [FromQuery] string kind, CancellationToken ct)
    {
        var result = await _competitions.GetDocumentAsync(applicantId, kind, ct);

        if (!result.Succeeded || result.Value is null)
            return NotFound();

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /* ============================================================= posters */

    /// <summary>
    /// One of the two printed posters, for the public portal.
    /// </summary>
    /// <remarks>
    /// <para>Anonymous, because it is an advertisement on a page that needs no
    /// sign-in.</para>
    ///
    /// <para><c>no-cache</c> rather than a long <c>max-age</c>, and the difference
    /// matters: with a day's max-age the browser does not ask again, so replacing
    /// the artwork left returning visitors looking at last year's poster until the
    /// cache expired. This was caught by uploading a second poster and watching the
    /// page keep the first. The ETag still does the saving — the browser asks, the
    /// server answers 304, and nothing is re-sent unless the picture actually
    /// changed.</para>
    /// </remarks>
    [HttpGet("{id:int}/advert")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Advert(int id, [FromQuery] string slot, CancellationToken ct)
    {
        var result = await _competitions.GetAdvertAsync(id, slot, ct);

        if (!result.Succeeded || result.Value is null)
            return NotFound();

        var etag = $"\"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(result.Value.Content).AsSpan(0, 8))}\"";

        if (Request.Headers.IfNoneMatch.Contains(etag))
            return StatusCode(StatusCodes.Status304NotModified);

        Response.Headers.CacheControl = "no-cache";
        Response.Headers.ETag = etag;

        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    /// <summary>Uploads or replaces one of the two posters.</summary>
    [HttpPost("{id:int}/advert")]
    [Authorize(Roles = UserRoles.Admin)]
    [RequestSizeLimit(8 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadAdvert(
        int id,
        [FromQuery] string slot,
        IFormFile file,
        [FromQuery] int? width,
        [FromQuery] int? height,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return Envelope(Result<bool>.Failure("Choose a poster to upload."));

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);

        return Envelope(
            await _competitions.SetAdvertAsync(
                id, slot, stream.ToArray(), file.FileName, file.ContentType,
                width, height, CurrentUserName, ct),
            "Advertisement uploaded.");
    }

    [HttpDelete("{id:int}/advert")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveAdvert(int id, [FromQuery] string slot, CancellationToken ct)
        => Envelope(await _competitions.RemoveAdvertAsync(id, slot, ct), "Advertisement removed.");

    /* ============================================================ settings */

    /// <summary>Every year set up, newest first.</summary>
    [HttpGet]
    [Authorize(Roles = UserRoles.BackOffice)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CompetitionPublicDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> All(CancellationToken ct)
        => Envelope(await _competitions.GetAllAsync(ct));

    /// <summary>Creates a year, or edits its fees, times, rules and dates.</summary>
    [HttpPost]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CompetitionSaveRequest request, CancellationToken ct)
        => Envelope(await _competitions.SaveCompetitionAsync(null, request, CurrentUserName, ct), "Competition saved.");

    [HttpPut("{id:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        int id, [FromBody] CompetitionSaveRequest request, CancellationToken ct)
        => Envelope(await _competitions.SaveCompetitionAsync(id, request, CurrentUserName, ct), "Competition saved.");

    /// <summary>Adds or renames a name shown on the registration form.</summary>
    [HttpPost("{id:int}/organisers")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddOrganiser(
        int id, [FromBody] CompetitionOrganiserSaveRequest request, CancellationToken ct)
        => Envelope(await _competitions.SaveOrganiserAsync(id, null, request, ct), "Name saved.");

    [HttpPut("{id:int}/organisers/{organiserId:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateOrganiser(
        int id, int organiserId, [FromBody] CompetitionOrganiserSaveRequest request, CancellationToken ct)
        => Envelope(await _competitions.SaveOrganiserAsync(id, organiserId, request, ct), "Name saved.");

    [HttpDelete("organisers/{organiserId:int}")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeleteOrganiser(int organiserId, CancellationToken ct)
        => Envelope(await _competitions.DeleteOrganiserAsync(organiserId, ct), "Name removed.");
}
