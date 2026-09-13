using System.Net;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Platform.Storage;
using Expenses.Desktop.Core.Screens;
using Expenses.Desktop.Core.Tests.Fakes;
using Expenses.Desktop.UI.Tests.Harness;

namespace Expenses.Desktop.UI.Tests;

/// <summary>
/// Scenarios from desktop-client, through a drop onto the real window: "Dropping a file captures it",
/// "Several files are dropped at once", "Something other than a file is dropped". The dropped files are
/// real files obtained from the window's own storage provider, because the platform does not allow its
/// storage file type to be implemented outside it.
/// </summary>
public sealed class DropTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("expenses-drop-");

    [AvaloniaFact]
    public async Task Dropping_a_file_captures_it()
    {
        using var app = await DesktopHarness.Home();
        app.Api.Respond("POST", "/receipts/capture", HttpStatusCode.OK, Captures.Extracted([]));

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(await File(app, "dropped.jpg", "the dropped photograph")));
        await Drop(app, data);
        await app.Until(() => app.Api.To("POST", "/receipts/capture").Any(), "the dropped file is uploaded");

        Assert.IsType<CaptureViewModel>(app.Current);
        Assert.Contains("the dropped photograph", Assert.Single(app.Api.To("POST", "/receipts/capture")).BodyText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Several_files_are_dropped_at_once()
    {
        using var app = await DesktopHarness.Home();
        var home = app.Current;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateFile(await File(app, "one.jpg", "one")));
        data.Add(DataTransferItem.CreateFile(await File(app, "two.jpg", "two")));
        await Drop(app, data);

        Assert.Same(home, app.Current);
        Assert.Equal("Receipts are captured one at a time. Drop a single file.", app.Find<Avalonia.Controls.TextBlock>("CaptureNotice").Text);
        Assert.Empty(app.Api.To("POST", "/receipts/capture"));
    }

    [AvaloniaFact]
    public async Task Something_other_than_a_file_is_dropped()
    {
        using var app = await DesktopHarness.Home();
        var home = app.Current;
        var requests = app.Api.Requests.Count;

        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText("some text"));
        await Drop(app, data);

        Assert.Same(home, app.Current);
        Assert.False(app.IsShown("CaptureNotice"));
        Assert.Equal(requests, app.Api.Requests.Count);
    }

    public void Dispose()
    {
        _directory.Delete(recursive: true);
    }

    private static async Task Drop(DesktopHarness app, IDataTransfer data)
    {
        // Onto the recent purchases, away from any control, so the drop lands on the window at large.
        var point = new Point(app.Window.ClientSize.Width / 2, app.Window.ClientSize.Height - 40);

        app.Window.DragDrop(point, RawDragEventType.DragEnter, data, DragDropEffects.Copy, RawInputModifiers.None);
        app.Window.DragDrop(point, RawDragEventType.DragOver, data, DragDropEffects.Copy, RawInputModifiers.None);
        app.Window.DragDrop(point, RawDragEventType.Drop, data, DragDropEffects.Copy, RawInputModifiers.None);
        await app.Settle();
    }

    private async Task<IStorageFile> File(DesktopHarness app, string name, string contents)
    {
        var path = Path.Combine(_directory.FullName, name);
        await System.IO.File.WriteAllTextAsync(path, contents);

        return await app.Window.StorageProvider.TryGetFileFromPathAsync(path)
            ?? throw new Xunit.Sdk.XunitException("The headless storage provider could not supply a file, so a drop cannot be simulated.");
    }
}
