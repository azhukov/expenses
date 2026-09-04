namespace Expenses.Domain;

/// <summary>
/// The receipt a purchase carries: the reference to its stored file, and what extraction has made
/// of it. A value inside the aggregate rather than an entity beside it (D2, D11) — the bytes live
/// in a file under the receipt store, and this holds the identity of that file, never its content.
/// </summary>
public sealed class Receipt
{
    public const int ContentHashLength = 32;

    public const int StorageKeyMaxLength = 256;

    /// <summary>
    /// The lifecycle of an attached receipt. Every member is terminal: extraction always runs to
    /// completion within the request that triggered it, so there is nothing between requests for a
    /// receipt to sit in. The numeric values are unchanged from when <c>Pending</c> and
    /// <c>Extracting</c> existed, so persisted terminal states need no remapping.
    /// </summary>
    public enum ExtractionState
    {
        Extracted = 2,
        NeedsReview = 3,
        Failed = 4,
    }

    /// <summary>
    /// Where a fiscal identifier came from. A value decoded from a fiscal code is exact; a value
    /// read as text by a vision stage is not (D20).
    /// </summary>
    public enum FiscalSource
    {
        None = 0,

        /// <summary>Decoded by a client at capture and sent alongside the upload.</summary>
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

    private Receipt()
    {
        // EF materialisation.
        ContentHash = null!;
        StorageKey = null!;
        ContentType = null!;
    }

    /// <summary>SHA-256 of the raw bytes — 32 bytes exactly. Identifies the file in the store.</summary>
    public byte[] ContentHash { get; private set; }

    /// <summary>
    /// Where the file is, relative to the receipt store. Retained as written rather than
    /// recomputed from the hash, so the layout of the store can change without rewriting
    /// history (D11).
    /// </summary>
    public string StorageKey { get; private set; }

    /// <summary>Determined from the file content, never from the declared type or extension.</summary>
    public string ContentType { get; private set; }

    public long SizeInBytes { get; private set; }

    public ExtractionState State { get; private set; }

    /// <summary>Why extraction failed, when it did. Null otherwise.</summary>
    public string? FailureReason { get; private set; }

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

    /// <summary>Derived; never stored, because it follows from the four values above.</summary>
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
    /// Whether two readings of a fiscal identifier are the same identifier. The JIKR is printed
    /// hyphenated by one ERP and unhyphenated by another, so punctuation is not a disagreement —
    /// while both readings are still retained exactly as they were read, no format imposed (D10,
    /// D24). It lives on the entity because the entity is what reports corroboration, and one
    /// implementation is what keeps the ledger and the cascade from disagreeing about it.
    /// </summary>
    public static bool SameFiscalIdentifier(string? left, string? right)
        => string.Equals(WithoutHyphens(left), WithoutHyphens(right), StringComparison.Ordinal);

    private static string? WithoutHyphens(string? value)
        => value?.Replace("-", string.Empty, StringComparison.Ordinal);

    /// <summary>Identifiers a client decoded at capture, accepted without extraction having run.</summary>
    public void SupplyFiscalIdentifiers(string? ikof, string? jikr)
    {
        FiscalIkofSupplied = Normalise(ikof) ?? FiscalIkofSupplied;
        FiscalJikrSupplied = Normalise(jikr) ?? FiscalJikrSupplied;
    }

    /// <summary>Identifiers obtained from the stored image, by decode or by reading text.</summary>
    public void RecordExtractedFiscalIdentifiers(string? ikof, string? jikr, FiscalSource source)
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

    private static string? Normalise(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The reference to a file already written to the store. Constructed only after the bytes are
    /// on disk, so a purchase can never point at a file that was never written (D11) — and only
    /// once extraction has already reached a terminal state, since extraction always runs to
    /// completion within the request that produced this receipt.
    /// </summary>
    public static Receipt Of(
        byte[] contentHash,
        string storageKey,
        string contentType,
        long sizeInBytes,
        ExtractionState state,
        string? failureReason = null)
    {
        if (contentHash.Length != ContentHashLength)
        {
            throw new ArgumentException(
                $"A content hash is a {ContentHashLength}-byte SHA-256, but {contentHash.Length} bytes were supplied.",
                nameof(contentHash));
        }

        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException(
                "A receipt requires the storage key of the file holding its bytes.",
                nameof(storageKey));
        }

        if (storageKey.Length > StorageKeyMaxLength)
        {
            throw new ArgumentException(
                $"A storage key is at most {StorageKeyMaxLength} characters.",
                nameof(storageKey));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException(
                "A receipt requires a content type determined from its content.",
                nameof(contentType));
        }

        if (sizeInBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeInBytes),
                sizeInBytes,
                "A receipt must have content.");
        }

        return new Receipt
        {
            ContentHash = contentHash,
            StorageKey = storageKey.Trim(),
            ContentType = contentType.Trim(),
            SizeInBytes = sizeInBytes,
            State = state,
            FailureReason = state == ExtractionState.Failed ? failureReason : null,
        };
    }

    /// <summary>
    /// Re-extracting an already-attached receipt: every state is terminal, so this is always a
    /// single, direct terminal-to-terminal move, never a multi-step lifecycle.
    /// </summary>
    public void TransitionTo(ExtractionState next, string? failureReason = null)
    {
        State = next;
        FailureReason = next == ExtractionState.Failed ? failureReason : null;
    }
}
