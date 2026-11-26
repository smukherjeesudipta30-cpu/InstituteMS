using System.Security.Claims;
using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using InstituteMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// A parent's own page: their children (matched by the guardian email or phone
/// on the child's record to the parent's sign-in), with admission details, fees,
/// receipts and exam results. Never another family's. The PreSchool admin and
/// SuperAdmin may open it to preview what a parent sees.
/// </summary>
[ApiController]
[Route("api/v1/preschool/parent")]
[Authorize(Roles = $"{UserRoles.Parent},{UserRoles.PreSchoolHead},{UserRoles.SuperAdmin}")]
public class PreSchoolParentController : ApiControllerBase
{
    private readonly IPreSchoolStudentService _students;
    private readonly AppDbContext _db;

    public PreSchoolParentController(IPreSchoolStudentService students, AppDbContext db)
    {
        _students = students;
        _db = db;
    }

    /// <summary>The signed-in parent's email and phone, from their account.</summary>
    private async Task<(string? Email, string? Phone)> WhoAsync(CancellationToken ct)
    {
        var email = User.FindFirstValue(ClaimTypes.Email);
        var id = CurrentUserId;
        var phone = id is int uid
            ? await _db.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.PhoneNumber).FirstOrDefaultAsync(ct)
            : null;
        return (email, phone);
    }

    [HttpGet("children")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolParentChildDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Children(CancellationToken ct)
    {
        var (email, phone) = await WhoAsync(ct);
        return Envelope(Result<IReadOnlyList<PreSchoolParentChildDto>>.Success(await _students.ParentChildrenAsync(email, phone, ct)));
    }

    [HttpGet("receipts/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolReceiptDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
    {
        var (email, phone) = await WhoAsync(ct);
        return Envelope(await _students.ParentReceiptAsync(id, email, phone, ct));
    }
}
