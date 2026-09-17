using InstituteMS.Application.Fees;
using InstituteMS.Application.Students;

namespace InstituteMS.Application.Documents;

/// <summary>
/// Renders the two receipts as PDF, for attaching to email and for the download
/// link sent on WhatsApp.
/// </summary>
/// <remarks>
/// The printed receipts are produced in the browser from
/// <c>core/printing/legacy-documents.ts</c>; these are produced on the server. They
/// are two renderings of the same document and must be kept saying the same thing —
/// the figures come from the same DTOs, which is what stops them drifting.
/// </remarks>
public interface IReceiptPdfGenerator
{
    byte[] AdmissionReceipt(AdmissionReceiptDto receipt);

    byte[] MoneyReceipt(FeeReceiptDto receipt);
}
