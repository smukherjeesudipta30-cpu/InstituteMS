using InstituteMS.Application.Common;

namespace InstituteMS.Application.About;

public interface IAboutService
{
    Task<AboutPageDto> GetAboutPageAsync(CancellationToken ct = default);

    /// <summary>Stores a contact-us enquiry (legacy <c>dbo.CusMessage</c>).</summary>
    Task<Result<int>> SubmitContactAsync(ContactRequest request, string? sourceIp, CancellationToken ct = default);
}
