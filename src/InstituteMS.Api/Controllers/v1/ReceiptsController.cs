using InstituteMS.Application.Documents;
using InstituteMS.Application.Fees;
using InstituteMS.Application.Students;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The receipt a student downloads from the link sent to them on WhatsApp.
/// </summary>
/// <remarks>
/// <para><b>Anonymous by design.</b> The student taps this on their phone, from the
/// message, and most of them will not be signed in. The signed token in the URL is
/// the authorisation: it names one receipt, cannot be altered without invalidating
/// its signature, and expires. Changing the id in the URL does not reach another
/// student's receipt — it simply fails.</para>
///
/// <para>Rate-limited so the token cannot be brute-forced by trying values. Guessing
/// one is already implausible against an HMAC, but there is no reason to allow the
/// attempt at speed.</para>
/// </remarks>
[Route("r")]
[Tags("Receipts")]
[AllowAnonymous]
public class ReceiptsController : ControllerBase
{
    private readonly IReceiptLinkService _links;
    private readonly IReceiptPdfGenerator _pdf;
    private readonly IStudentService _students;
    private readonly IFeeService _fees;

    public ReceiptsController(
        IReceiptLinkService links,
        IReceiptPdfGenerator pdf,
        IStudentService students,
        IFeeService fees)
    {
        _links = links;
        _pdf = pdf;
        _students = students;
        _fees = fees;
    }

    /// <summary>Returns the receipt as a PDF, inline so a phone shows it straight away.</summary>
    [HttpGet("{token}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        var claim = _links.Validate(token);

        /* One response for malformed, tampered, expired and unknown alike. Telling
           them apart would say whether a receipt exists, which is not something a
           stranger with a guessed link should learn. */
        if (claim is null) return NotFound();

        var (kind, id) = claim.Value;

        if (kind == ReceiptKind.Admission)
        {
            var receipt = await _students.GetReceiptAsync(id, ct);
            if (!receipt.Succeeded || receipt.Value is null) return NotFound();

            return File(
                _pdf.AdmissionReceipt(receipt.Value),
                "application/pdf",
                $"Avijan-Admission-Receipt-{Safe(receipt.Value.BillNo)}.pdf");
        }

        var fee = await _fees.GetReceiptAsync(id, ct);
        if (!fee.Succeeded || fee.Value is null) return NotFound();

        return File(
            _pdf.MoneyReceipt(fee.Value),
            "application/pdf",
            $"Avijan-Money-Receipt-{Safe(fee.Value.BillNo)}.pdf");
    }

    /// <summary>Bill numbers carry slashes; a file name cannot.</summary>
    private static string Safe(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "receipt"
            : string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
}
