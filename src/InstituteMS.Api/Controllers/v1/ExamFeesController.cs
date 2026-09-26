using InstituteMS.Application.Common;
using InstituteMS.Application.ExamFees;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// Exam fees: the fee per course and branch (Admin), a student's own amount
/// (Admin), and collecting it (Admin, Editor or Receptionist).
/// </summary>
[Route("api/v1/exam-fees")]
[Tags("Exam fees")]
[Authorize(Roles = UserRoles.MasterData)]
public class ExamFeesController : ApiControllerBase
{
    private readonly IExamFeeService _fees;

    public ExamFeesController(IExamFeeService fees) => _fees = fees;

    /// <summary>Every course at every branch, with its exam fee where set.</summary>
    [HttpGet("rates")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamFeeRateDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rates(CancellationToken ct)
        => Envelope(Result<IReadOnlyList<ExamFeeRateDto>>.Success(await _fees.GetRatesAsync(ct)));

    /// <summary>Sets the exam fees. A null amount clears that course/branch.</summary>
    [HttpPut("rates")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamFeeRateDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveRates([FromBody] ExamFeeRatesSaveRequest request, CancellationToken ct)
        => Envelope(await _fees.SaveRatesAsync(request, CurrentUserName, ct), "Exam fees saved.");

    /// <summary>A student's exam fee: charged, paid, owed, and each payment.</summary>
    [HttpGet("students/{studentId:int}")]
    [ProducesResponseType(typeof(ApiResponse<StudentExamFeeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Student(int studentId, CancellationToken ct)
        => Envelope(await _fees.GetStudentAsync(studentId, ct));

    /// <summary>Records exam fee received from a student.</summary>
    [HttpPost("students/{studentId:int}/collect")]
    [ProducesResponseType(typeof(ApiResponse<StudentExamFeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentExamFeeDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Collect(int studentId, [FromBody] ExamFeeCollectRequest request, CancellationToken ct)
        => Envelope(await _fees.CollectAsync(studentId, request, CurrentUserName, ct), "Exam fee collected.");

    /// <summary>Gives one student a different exam fee (or clears it with a null amount).</summary>
    [HttpPut("students/{studentId:int}/override")]
    [Authorize(Roles = UserRoles.Admin)]
    [ProducesResponseType(typeof(ApiResponse<StudentExamFeeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Override(int studentId, [FromBody] ExamFeeOverrideRequest request, CancellationToken ct)
        => Envelope(await _fees.SetOverrideAsync(studentId, request, CurrentUserName, ct), "Exam fee updated.");

    /// <summary>Removes a mistaken exam fee payment.</summary>
    [HttpDelete("payments/{paymentId:int}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeletePayment(int paymentId, CancellationToken ct)
        => Envelope(await _fees.DeletePaymentAsync(paymentId, ct), "Exam fee payment removed.");

    /// <summary>Moves an exam fee typed into Other income onto a student's exam fee.</summary>
    [HttpPost("move-from-other-income")]
    [ProducesResponseType(typeof(ApiResponse<StudentExamFeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<StudentExamFeeDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MoveFromOtherIncome([FromBody] ExamFeeMoveRequest request, CancellationToken ct)
        => Envelope(await _fees.MoveFromOtherIncomeAsync(request, CurrentUserName, ct), "Moved to the student's exam fee.");

    /// <summary>Students scheduled for an exam who still owe exam fee.</summary>
    [HttpGet("dues")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ExamFeeDueRowDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Dues([FromQuery] string? branch, CancellationToken ct)
        => Envelope(Result<IReadOnlyList<ExamFeeDueRowDto>>.Success(await _fees.GetDuesAsync(branch, ct)));
}

/// <summary>The signed-in student's own exam fee.</summary>
[Route("api/v1/my-exam-fee")]
[Tags("Exam fees")]
[Authorize(Roles = UserRoles.Student)]
public class MyExamFeeController : ApiControllerBase
{
    private readonly IExamFeeService _fees;

    public MyExamFeeController(IExamFeeService fees) => _fees = fees;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<StudentExamFeeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine(CancellationToken ct)
        => Envelope(await _fees.GetMineAsync(CurrentUserName ?? string.Empty, ct));
}
