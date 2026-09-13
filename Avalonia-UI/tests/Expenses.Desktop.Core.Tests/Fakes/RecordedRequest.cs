using System.Text;

namespace Expenses.Desktop.Core.Tests.Fakes;

/// <summary>One request as the fake transport received it.</summary>
public sealed record RecordedRequest(string Method, Uri Uri, IReadOnlyList<byte>? Body, string? ContentType)
{
    public string BodyText => Body is null ? string.Empty : Encoding.UTF8.GetString([.. Body]);
}
