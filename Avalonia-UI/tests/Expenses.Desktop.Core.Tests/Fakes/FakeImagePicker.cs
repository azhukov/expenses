using System.Text;
using Expenses.Desktop.Core.Navigation;

namespace Expenses.Desktop.Core.Tests.Fakes;

/// <summary>A picker that hands back whatever the test put in it, or is dismissed when it holds nothing.</summary>
public sealed class FakeImagePicker : IImagePicker
{
    public ReceiptImage? Next { get; set; }

    public int Opened { get; private set; }

    public static ReceiptImage Image(string name, string contents = "jpeg bytes")
    {
        return Image(name, Encoding.UTF8.GetBytes(contents));
    }

    public static ReceiptImage Image(string name, byte[] bytes)
    {
        return new ReceiptImage(name, _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }

    public Task<ReceiptImage?> PickImage(CancellationToken cancellationToken = default)
    {
        Opened++;

        return Task.FromResult(Next);
    }
}
