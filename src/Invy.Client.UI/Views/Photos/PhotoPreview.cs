using Avalonia.Controls;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Photos;
using Invy.Client.UI.Converters;

namespace Invy.Client.UI.Views.Photos;

/// <summary>Просмотр фото крупно; отсюда же можно сделать его обложкой.</summary>
public static class PhotoPreview
{
    public static async Task ShowAsync(Control anchor, PhotoStripViewModel strip, PhotoViewModel photo)
    {
        ArgumentNullException.ThrowIfNull(strip);
        ArgumentNullException.ThrowIfNull(photo);
        var bytes = await strip.LoadPreviewAsync(photo);
        var dialog = new FAContentDialog
        {
            Title = photo.FileName,
            Content = new Image
            {
                Source = BitmapConverters.Decode(bytes, 1600),
                Stretch = Stretch.Uniform,
                MaxWidth = 900,
                MaxHeight = 620,
            },
            CloseButtonText = "Закрыть",
            PrimaryButtonText = strip.CanEdit && !photo.IsCover ? "Сделать обложкой" : string.Empty,
            DefaultButton = FAContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 980.0;

        var result = TopLevel.GetTopLevel(anchor) is { } topLevel ? await dialog.ShowAsync(topLevel) : await dialog.ShowAsync();
        if (result == FAContentDialogResult.Primary)
        {
            await strip.SetCoverCommand.ExecuteAsync(photo);
        }
    }
}
