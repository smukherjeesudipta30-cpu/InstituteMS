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

    Task<Result<FeeReceiptDto>> GetReceiptAsync(int id, CancellationToken ct = default);
}
