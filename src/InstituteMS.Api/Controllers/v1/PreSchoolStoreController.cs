using InstituteMS.Application.Common;
using InstituteMS.Application.PreSchool;
using InstituteMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InstituteMS.Api.Controllers.v1;

/// <summary>
/// The PreSchool store — tuition books, bags and the like, class by class.
/// The PreSchool admin and receptionist keep it; only the admin removes an
/// entry made in error.
/// </summary>
[Route("api/v1/preschool")]
[Tags("PreSchool")]
[Authorize(Roles = UserRoles.PreSchoolOffice)]
public class PreSchoolStoreController : ApiControllerBase
{
    private readonly IPreSchoolStoreService _store;

    public PreSchoolStoreController(IPreSchoolStoreService store) => _store = store;

    /// <summary>Every item with its stock, and the latest stock received and items given.</summary>
    [HttpGet("store")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStoreDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
        => Envelope(Result<PreSchoolStoreDto>.Success(await _store.GetAsync(ct)));

    [HttpPost("store/items")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStoreItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AddItem([FromBody] PreSchoolStoreItemSaveRequest request, CancellationToken ct)
        => Envelope(await _store.SaveItemAsync(null, request, CurrentUserName, ct), "Item added.");

    [HttpPut("store/items/{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStoreItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> SaveItem(int id, [FromBody] PreSchoolStoreItemSaveRequest request, CancellationToken ct)
        => Envelope(await _store.SaveItemAsync(id, request, CurrentUserName, ct), "Item saved.");

    /// <summary>Stock received for one item.</summary>
    [HttpPost("store/items/{id:int}/stock")]
    [ProducesResponseType(typeof(ApiResponse<PreSchoolStoreItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> StockIn(int id, [FromBody] PreSchoolStockInRequest request, CancellationToken ct)
        => Envelope(await _store.StockInAsync(id, request, CurrentUserName, ct), "Stock added.");

    [HttpDelete("store/movements/{id:int}")]
    [Authorize(Roles = UserRoles.PreSchoolHead)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveMovement(int id, CancellationToken ct)
        => Envelope(await _store.RemoveMovementAsync(id, ct), "Entry removed.");

    /// <summary>A child's store items and which they have been given.</summary>
    [HttpGet("students/{id:int}/store")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolStudentStoreLineDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ForStudent(int id, CancellationToken ct)
        => Envelope(await _store.ForStudentAsync(id, ct));

    /// <summary>Gives the child one of the item; it comes off the class's stock.</summary>
    [HttpPost("students/{id:int}/store/{itemId:int}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PreSchoolStudentStoreLineDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Give(int id, int itemId, CancellationToken ct)
        => Envelope(await _store.GiveAsync(id, itemId, CurrentUserName, ct), "Given and taken off the stock.");
}
