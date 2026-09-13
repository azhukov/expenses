namespace Expenses.Desktop.Core.Navigation;

/// <summary>
/// A file chosen or dropped for capture: its name, and a way to open it. The bytes are read by the
/// capture screen, once, never by home (D6).
/// </summary>
public sealed record ReceiptImage(string Name, Func<CancellationToken, Task<Stream>> Open);
