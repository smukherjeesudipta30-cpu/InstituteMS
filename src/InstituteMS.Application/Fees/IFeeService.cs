using InstituteMS.Application.Common;

namespace InstituteMS.Application.Fees;

public interface IFeeService
{
    /// <summary>The "Go" button: student header, installments paid, last payment, due.</summary>
    Task<Result<StudentFeeBillDto>> GetBillAsync(string registrationId, CancellationToken ct = default);

    Task<PagedResult<FeePaymentDto>> GetPaymentsAsync(
        string registrationId, PagedQuery query, CancellationToken ct = default);

    /// <summary>
    /// Works out days elapsed, late fine, total and new due. The legacy app did this
    /// in the browser; doing it here means the saved figures cannot disagree with the
    /// displayed ones, and the APK gets the same numbers.
    /// </summary>
    Task<Result<FeeCalculationDto>> CalculateAsync(
        FeeCalculationRequest request, CancellationToken ct = default);

    /// <summary>Records a payment — writes MonthlyFees, Fees, LateFees and one TecFees row per teacher.</summary>
    Task<Result<FeeRecordedDto>> RecordPaymentAsync(
        FeeSaveRequest request, string? actor, CancellationToken ct = default);

    Task<Result<bool>> UpdatePaymentAsync(
        int id, FeeSaveRequest request, string? actor, CancellationToken ct = default);

    /// <summary>
    /// Removes a payment entered by mistake or duplicated — the fee ledger row and
    /// every sibling row keyed to its bill number (the income roll-up, the late-fine
    /// row, and each teacher's payout), so nothing is left pointing at a bill number
    /// that no longer has a payment behind it.
    /// </summary>
    Task<Result<bool>> DeletePaymentAsync(int id, CancellationToken ct = default);

    Task<Result<FeeReceiptDto>> GetReceiptAsync(int id, CancellationToken ct = default);

    /* ----------------------------------- online payment from the portal */

    /// <summary>
    /// What the signed-in student owes for their next unpaid month, with the fine
    /// already worked out, plus the institute's UPI details to pay against.
    /// </summary>
    Task<Result<MyFeeDueDto>> GetMyDueAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// Records a fee the student has paid against the QR code.
    /// </summary>
    /// <remarks>
    /// Writes the same rows as a payment taken at the counter — MonthlyFees, Fees,
    /// LateFees and a TecFees row per teacher — so the ledger and the teacher payout
    /// are correct immediately. The teacher percentage is carried over from the
    /// student's previous payment, since nothing else records what was agreed.
    ///
    /// <para>The payment is stored <b>unverified</b>: the QR is static, so the
    /// server has no way to know the money arrived and the office confirms it
    /// afterwards from the dashboard.</para>
    /// </remarks>
    Task<Result<FeeRecordedDto>> PayOnlineAsync(
        string email, OnlineFeePaymentRequest request, CancellationToken ct = default);

    /// <summary>Online payments recorded but not yet checked against the Paytm account.</summary>
    Task<Result<IReadOnlyList<OnlinePaymentNoticeDto>>> GetUnverifiedOnlinePaymentsAsync(
        int take = 25, CancellationToken ct = default);

    /// <summary>Marks an online payment as checked and confirmed.</summary>
    Task<Result<bool>> VerifyOnlinePaymentAsync(
        int feeId, string? actor, CancellationToken ct = default);
}
