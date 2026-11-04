using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Invy.Client.Core.Session;

namespace Invy.Client.UI.Services;

/// <summary>Файлы из диалога, перетаскивания или буфера обмена → <see cref="PickedFile"/>.</summary>
public static class PickedFiles
{
    public static IReadOnlyList<PickedFile> From(IEnumerable<IStorageItem>? items) =>
        items?.OfType<IStorageFile>().Select(f => new PickedFile(f.Name, f.OpenReadAsync)).ToList() ?? [];

    /// <summary>Картинки из буфера: скопированные файлы или само изображение (снимок экрана) — как PNG.</summary>
    public static async Task<IReadOnlyList<PickedFile>> FromClipboardAsync(TopLevel? topLevel)
    {
        if (topLevel?.Clipboard is not { } clipboard)
        {
            return [];
        }

        if (From(await clipboard.TryGetFilesAsync()) is { Count: > 0 } files)
        {
            return files;
        }

        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
            var bytes = stream.ToArray();
            return [new PickedFile($"Снимок {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png", () => Task.FromResult<Stream>(new MemoryStream(bytes)))];
        }

        return [];
    }
}
