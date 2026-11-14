namespace Expenses.Application.Dtos;

/// <summary>
/// Why the fiscal step got the invoice or did not. It only reports: it never changes which steps
/// run, the extraction state, or whether a capture can be confirmed, and it is never persisted. Failure
/// is still "nothing, not an error" (D26). This record only says which kind of nothing it was (D42).
/// </summary>
public sealed record FiscalDiagnosis(
    FiscalDiagnosisKind Kind,
    string Detail,

    /// <summary>The verification parameters that were absent: any of <c>iic</c>, <c>tin</c>, <c>crtd</c>.</summary>
    IReadOnlyList<string>? MissingParameters = null,

    /// <summary>The status the verification service answered, where it refused the request.</summary>
    int? PortalStatus = null,

    /// <summary>The start of an answer that could not be read, never longer than <see cref="MaximumExcerptLength"/>.</summary>
    string? ResponseExcerpt = null)
{
    /// <summary>
    /// The longest excerpt kept from an unreadable answer. It is enough to tell an error page from a
    /// changed JSON shape, and too short to carry a whole invoice anywhere (D46).
    /// </summary>
    public const int MaximumExcerptLength = 200;

    /// <summary>The start of <paramref name="body"/>, trimmed and cut to <see cref="MaximumExcerptLength"/>.</summary>
    public static string Excerpt(string body)
    {
        string trimmed = body.Trim();

        return trimmed.Length <= MaximumExcerptLength ? trimmed : trimmed[..MaximumExcerptLength];
    }
}
