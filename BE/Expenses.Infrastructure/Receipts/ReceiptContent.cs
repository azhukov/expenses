using Expenses.Application.Errors;

namespace Expenses.Infrastructure.Receipts;

/// <summary>
/// What an uploaded file is, decided by reading it. A declared content type and a file extension
/// are both claims made by whoever uploaded the file; the bytes are not.
/// </summary>
internal static class ReceiptContent
{
    /// <summary>15 MB, enforced before the bytes are buffered anywhere they would cost memory.</summary>
    public const long MaximumSizeInBytes = 15 * 1024 * 1024;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";
    public const string Heic = "image/heic";
    public const string Pdf = "application/pdf";

    /// <summary>The formats a receipt may arrive in, for the message a rejection has to carry.</summary>
    public static readonly string[] Accepted = [Jpeg, Png, WebP, Heic, Pdf];

    private static readonly byte[] s_jpegMagic = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] s_pngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] s_riff = "RIFF"u8.ToArray();
    private static readonly byte[] s_webP4Cc = "WEBP"u8.ToArray();
    private static readonly byte[] s_fileTypeBox = "ftyp"u8.ToArray();
    private static readonly byte[] s_pdfMagic = "%PDF-"u8.ToArray();

    /// <summary>
    /// HEIC declares itself in the brand of its file-type box. The variants are all the same
    /// container as far as storage is concerned, so they are reported as one type.
    /// </summary>
    private static readonly string[] s_heicBrands = ["heic", "heix", "hevc", "heim", "heis", "mif1", "msf1"];

    /// <summary>Null when the content is not one of the accepted formats.</summary>
    public static string? Detect(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(s_jpegMagic))
        {
            return Jpeg;
        }

        if (content.StartsWith(s_pngMagic))
        {
            return Png;
        }

        if (content.StartsWith(s_pdfMagic))
        {
            return Pdf;
        }

        // RIFF....WEBP — the four bytes between the container tag and the format tag are a length.
        if (content.Length >= 12 && content.StartsWith(s_riff) && content[8..12].SequenceEqual(s_webP4Cc))
        {
            return WebP;
        }

        if (content.Length >= 12
            && content[4..8].SequenceEqual(s_fileTypeBox)
            && s_heicBrands.Contains(System.Text.Encoding.ASCII.GetString(content[8..12])))
        {
            return Heic;
        }

        return null;
    }

    /// <summary>
    /// Rejects an oversized or unrecognised file before anything is written, even temporarily,
    /// and returns the content type sniffed from the bytes. Shared by every place a receipt image
    /// is accepted, so the same rules apply whether it is captured or attached.
    /// </summary>
    public static string Validate(byte[] content)
    {
        if (content.LongLength > MaximumSizeInBytes)
        {
            throw ExpensesException.For(
                ApplicationErrors.ReceiptImageTooLarge,
                $"A receipt image may be at most {MaximumSizeInBytes / (1024 * 1024)} MB.",
                ("sizeInBytes", content.LongLength),
                ("maximumSizeInBytes", MaximumSizeInBytes));
        }

        return Detect(content)
            ?? throw ExpensesException.For(
                ApplicationErrors.ReceiptImageUnsupportedFormat,
                $"A receipt image must be one of {string.Join(", ", Accepted)}.",
                ("acceptedContentTypes", Accepted));
    }
}
