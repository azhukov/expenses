namespace Expenses.Application.Dtos;

/// <summary>
/// The request sent to the fiscal verification service, exactly as it went over the wire: the method,
/// the address, and the form fields. It is reported with the diagnosis so the person holding the receipt
/// can see what the site was asked, beside what the QR code said (D47).
/// </summary>
public sealed record FiscalPortalRequest(
    string Method,
    string Url,
    IReadOnlyDictionary<string, string> Form);
