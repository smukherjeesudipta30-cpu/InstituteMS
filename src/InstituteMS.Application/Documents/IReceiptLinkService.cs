namespace InstituteMS.Application.Documents;

/// <summary>Which receipt a download link points at.</summary>
public enum ReceiptKind
{
    Admission = 1,
    Fee = 2
}

/// <summary>
/// Makes and checks the tokens behind the receipt download links sent on WhatsApp.
/// </summary>
/// <remarks>
/// <para>The link has to work for a student who is not signed in — they tap it on
/// their phone, straight from the message. So the token itself is the
/// authorisation, and it must be unguessable and impossible to alter: swapping the
/// id in the URL for another student's must not work.</para>
///
/// <para>Signed rather than stored. A table of issued tokens would need writing,
/// indexing and eventually clearing out, and would put a database round trip in
/// front of every download; a signature carries the receipt's identity and its
/// expiry inside the token, so nothing needs remembering.</para>
/// </remarks>
public interface IReceiptLinkService
{
    /// <summary>The token for one receipt. Expires, so an old message stops working.</summary>
    string CreateToken(ReceiptKind kind, int id);

    /// <summary>
    /// The full public URL a student taps. Built here rather than in each caller so
    /// there is one place that knows the shape of the link.
    /// </summary>
    string CreateUrl(ReceiptKind kind, int id);

    /// <summary>
    /// Reads a token back. Returns null when it is malformed, has been tampered
    /// with, or has expired — the caller cannot tell which, deliberately.
    /// </summary>
    (ReceiptKind Kind, int Id)? Validate(string token);
}
