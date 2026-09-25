namespace Expenses.Domain.Entities;

/// <summary>
/// The tax-authority invoice a purchase was recorded against: the fiscal QR payload and the
/// identifiers read from it or answered for it, with how each was established. A value inside the
/// aggregate, like <see cref="Receipt"/>, and independent of it (D35): a purchase read from a QR alone
/// carries this and no image, and one whose image is deleted keeps it.
/// </summary>
public sealed class FiscalInvoice
{
    /// <summary>
    /// Where a fiscal identifier came from. A value decoded from a fiscal code is exact; a value
    /// read as text by a vision stage is not (D20).
    /// </summary>
    public enum FiscalSource
    {
        None = 0,

        /// <summary>Decoded by a client at capture and sent alongside the upload, or on its own.</summary>
        SuppliedAtUpload = 1,

        /// <summary>Decoded from the stored image by the server-side fiscal-code stage.</summary>
        DecodedFromCode = 2,

        /// <summary>Read as printed text by a vision stage.</summary>
        ReadAsText = 3,

        /// <summary>
        /// Answered by the fiscal verification service. The JIKR is knowable no other way: it is
        /// absent from the fiscal code and printed nowhere the server can read it (D24).
        /// </summary>
        RetrievedFromService = 4,
    }

    /// <summary>
    /// Whether the identifiers known from more than one source agree. Reported rather than
    /// silently resolved: preferring one source without saying so would hide a misread receipt.
    /// </summary>
    public enum FiscalCorroboration
    {
        /// <summary>The receipt carried no fiscal identifiers at all.</summary>
        Absent = 0,

        /// <summary>Known from one source only; nothing to check it against.</summary>
        Unverified = 1,

        /// <summary>Two sources agree.</summary>
        Corroborated = 2,

        /// <summary>Two sources disagree. Both values are retained.</summary>
        Disagreed = 3,
    }

    private FiscalInvoice()
    {
        // EF materialisation.
    }

    /// <summary>
    /// Fiscal identifiers as read, no format imposed. Supplied and extracted values are held
    /// separately so a disagreement can be reported rather than one silently preferred.
    /// </summary>
    public string? FiscalIkofSupplied { get; private set; }

    public string? FiscalIkofExtracted { get; private set; }

    public string? FiscalJikrSupplied { get; private set; }

    public string? FiscalJikrExtracted { get; private set; }

    /// <summary>How the extracted identifiers were obtained — decoded exactly, or read as text.</summary>
    public FiscalSource FiscalExtractedSource { get; private set; }

    /// <summary>
    /// What the receipt's fiscal QR carries, verbatim, however it was obtained. Retained whether or
    /// not any identifier could be read from it: the unparseable payload is precisely the one a
    /// later parser would want back, and it cannot be recovered once discarded (D32).
    ///
    /// It is retained for re-runs above all. Decoding a stored image reads one symbol in three, so a
    /// purchase whose code a client read at capture would lose that reading on every later run if
    /// only the parsed identifiers survived — and a purchase captured from its code alone has no
    /// image to decode at all.
    /// </summary>
    public string? FiscalPayload { get; private set; }

    /// <summary>How the payload was obtained. <see cref="FiscalSource.None"/> where none is held.</summary>
    public FiscalSource FiscalPayloadSource { get; private set; }

    /// <summary>
    /// Nothing recorded at all. The aggregate never keeps an empty invoice: it would persist as a
    /// row claiming a fiscal identity while holding no value of one (D36).
    /// </summary>
    public bool IsEmpty
        => FiscalPayload is null
        && FiscalIkofSupplied is null
        && FiscalIkofExtracted is null
        && FiscalJikrSupplied is null
        && FiscalJikrExtracted is null;

    /// <summary>Derived; never stored, because it follows from the four identifiers above.</summary>
    public FiscalCorroboration Corroboration
    {
        get
        {
            var pairs = new[]
            {
                (Supplied: FiscalIkofSupplied, Extracted: FiscalIkofExtracted),
                (Supplied: FiscalJikrSupplied, Extracted: FiscalJikrExtracted),
            };

            if (pairs.All(pair => pair.Supplied is null && pair.Extracted is null))
            {
                return FiscalCorroboration.Absent;
            }

            var comparable = pairs
                .Where(pair => pair.Supplied is not null && pair.Extracted is not null)
                .ToList();

            if (comparable.Count == 0)
            {
                return FiscalCorroboration.Unverified;
            }

            // A disagreement is reported rather than resolved: preferring one source without
            // saying so would hide a misread receipt.
            return comparable.Any(pair => !SameFiscalIdentifier(pair.Supplied, pair.Extracted))
                ? FiscalCorroboration.Disagreed
                : FiscalCorroboration.Corroborated;
        }
    }

    /// <summary>
    /// An invoice with nothing recorded yet, for the caller to record onto. The aggregate drops it
    /// again if nothing ever is (<see cref="IsEmpty"/>).
    /// </summary>
    public static FiscalInvoice Create() => new();

    /// <summary>
    /// Whether two readings of a fiscal identifier are the same identifier. The JIKR is printed
    /// hyphenated by one ERP and unhyphenated by another, so punctuation is not a disagreement —
    /// while both readings are still retained exactly as they were read, no format imposed (D10,
    /// D24). It lives on the entity because the entity is what reports corroboration, and one
    /// implementation is what keeps the ledger and the cascade from disagreeing about it.
    /// </summary>
    public static bool SameFiscalIdentifier(string? left, string? right)
        => string.Equals(WithoutHyphens(left), WithoutHyphens(right), StringComparison.Ordinal);

    /// <summary>Identifiers a client decoded at capture, accepted without extraction having run.</summary>
    public void SupplyIdentifiers(string? ikof, string? jikr)
    {
        FiscalIkofSupplied = Normalise(ikof) ?? FiscalIkofSupplied;
        FiscalJikrSupplied = Normalise(jikr) ?? FiscalJikrSupplied;
    }

    /// <summary>Identifiers a step established, by decode, by reading text, or from the service.</summary>
    public void RecordExtractedIdentifiers(string? ikof, string? jikr, FiscalSource source)
    {
        string? normalisedIkof = Normalise(ikof);
        string? normalisedJikr = Normalise(jikr);

        // A run that read nothing leaves the previous run's values and source alone: a miss is an
        // ordinary outcome and must not erase what an earlier stage established (D20).
        if (normalisedIkof is null && normalisedJikr is null)
        {
            return;
        }

        FiscalIkofExtracted = normalisedIkof ?? FiscalIkofExtracted;
        FiscalJikrExtracted = normalisedJikr ?? FiscalJikrExtracted;
        FiscalExtractedSource = source;
    }

    /// <summary>
    /// The payload a fiscal QR carried, kept as it was read. The first payload obtained is the one
    /// retained: a payload supplied at capture is preferred outright over a later reading of the
    /// same image, because a client reading a live camera has focus and retries available to it
    /// that a single stored frame does not (D31).
    /// </summary>
    public void RecordPayload(string? payload, FiscalSource source)
    {
        if (Normalise(payload) is not { } recorded || FiscalPayload is not null)
        {
            return;
        }

        FiscalPayload = recorded;
        FiscalPayloadSource = source;
    }

    private static string? WithoutHyphens(string? value)
        => value?.Replace("-", string.Empty, StringComparison.Ordinal);

    private static string? Normalise(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
