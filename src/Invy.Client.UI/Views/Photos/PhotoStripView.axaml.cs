using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Invy.Client.Core.Photos;
using Invy.Client.UI.Services;

namespace Invy.Client.UI.Views.Photos;

/// <summary>Лента фото: двойной щелчок — просмотр; файлы можно перетащить на ленту.</summary>
public partial class PhotoStripView : UserControl
{
    public PhotoStripView()
    {
        InitializeComponent();
        AddHandler(DoubleTappedEvent, OnDoubleTapped);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropArea.Classes.Remove("drop-target"));
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private PhotoStripViewModel? ViewModel => DataContext as PhotoStripViewModel;

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.FindAncestorOfType<Border>(includeSelf: true) is { Tag: PhotoViewModel photo } && ViewModel is { } strip)
        {
            _ = PhotoPreview.ShowAsync(this, strip, photo);
        }
    }

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is PhotoViewModel photo && ViewModel is { } strip)
        {
            _ = PhotoPreview.ShowAsync(this, strip, photo);
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var accept = ViewModel is { CanEdit: true } && e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = accept ? DragDropEffects.Copy : DragDropEffects.None;
        if (accept)
        {
            DropArea.Classes.Add("drop-target");
        }

        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropArea.Classes.Remove("drop-target");
        if (ViewModel is { } strip && PickedFiles.From(e.DataTransfer.TryGetFiles()) is { Count: > 0 } files)
        {
            e.Handled = true;
            await strip.AddFilesAsync(files);
        }
    }
}
