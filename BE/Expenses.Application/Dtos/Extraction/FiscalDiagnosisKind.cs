namespace Expenses.Application.Dtos;

/// <summary>
/// The result of trying to get the invoice through the receipt's fiscal code. The set is closed, so a
/// client can choose a message by kind rather than by matching English text (D42).
/// </summary>
public enum FiscalDiagnosisKind
{
    /// <summary>No payload was supplied, and none could be decoded from the image.</summary>
    NoPayload,

    /// <summary>A payload was read, but it carries none of the verification parameters.</summary>
    NotFiscal,

    /// <summary>Some verification parameters were found, but not all three the service needs.</summary>
    IncompleteIdentifiers,

    /// <summary>The verification service could not be reached, or did not answer in time.</summary>
    PortalUnreachable,

    /// <summary>The verification service answered with a non-success status.</summary>
    PortalRejected,

    /// <summary>The verification service answered successfully with no invoice.</summary>
    NoRecord,

    /// <summary>The verification service answered with something that is not a readable invoice.</summary>
    UnparseableResponse,

    /// <summary>The invoice was retrieved, freshly or from an earlier answer.</summary>
    Retrieved,
}
