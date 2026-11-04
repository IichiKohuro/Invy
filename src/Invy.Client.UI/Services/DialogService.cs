using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Session;
using Invy.Client.Core.Shell;
using Invy.Client.UI.Views.Editor;
using Invy.Client.UI.Views.Explorer;
using Invy.Contracts.Items;

namespace Invy.Client.UI.Services;

/// <summary>
/// Диалоги в стиле Windows 11: вопросы и выбор места — <see cref="FAContentDialog"/> поверх активного окна,
/// редактор вещи — отдельное окно, уведомления — полоса внизу главного окна (исчезает сама).
/// </summary>
/// <param name="shell">Оболочка — для уведомлений.</param>
/// <param name="host">Окно-хозяин, если его не дают платформа и окна приложения (headless-тесты).</param>
public sealed class DialogService(ShellViewModel shell, Func<Window?>? host = null) : IDialogService
{
    private IDisposable? _noticeTimer;

    public async Task<DialogChoice> AskAsync(string title, string message, string primary, string? secondary = null, string close = "Отмена")
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 },
            PrimaryButtonText = primary,
            SecondaryButtonText = secondary ?? string.Empty,
            CloseButtonText = close,
            DefaultButton = FAContentDialogButton.Primary,
        };

        return await ShowAsync(dialog) switch
        {
            FAContentDialogResult.Primary => DialogChoice.Primary,
            FAContentDialogResult.Secondary => DialogChoice.Secondary,
            _ => DialogChoice.Cancel,
        };
    }

    public async Task<ItemDetailsDto?> EditItemAsync(ItemEditorViewModel editor)
    {
        var window = new ItemEditorWindow { DataContext = editor };
        if (ActiveWindow() is Window { IsVisible: true } owner)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            // Быстрое добавление из трея: главное окно может быть скрыто.
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.ShowInTaskbar = true;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.Show();
            await closed.Task;
        }

        return editor.Result;
    }

    public async Task<MoveTarget?> PickLocationAsync(string title, ClientSession session)
    {
        var picker = new LocationPickerViewModel(session, title);
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new LocationPickerView { DataContext = picker },
            PrimaryButtonText = "Выбрать",
            CloseButtonText = "Отмена",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };
        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LocationPickerViewModel.HasSelection))
            {
                dialog.IsPrimaryButtonEnabled = picker.HasSelection;
            }
        };
        _ = picker.LoadAsync();

        return await ShowAsync(dialog) == FAContentDialogResult.Primary ? picker.Result : null;
    }

    public async Task<IReadOnlyList<PickedFile>> PickImagesAsync()
    {
        if (TopLevel.GetTopLevel(ActiveWindow()) is not { StorageProvider.CanOpen: true } topLevel)
        {
            return [];
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Добавить фото",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        return files.Select(f => new PickedFile(f.Name, f.OpenReadAsync)).ToList();
    }

    public void Notify(string message, NotifyKind kind = NotifyKind.Info) => Dispatcher.UIThread.Post(() =>
    {
        shell.ShowNotice(message, kind);
        _noticeTimer?.Dispose();
        var notice = shell.Notice;
        _noticeTimer = DispatcherTimer.RunOnce(
            () =>
            {
                if (ReferenceEquals(shell.Notice, notice))
                {
                    shell.Notice = null;
                }
            },
            TimeSpan.FromSeconds(kind == NotifyKind.Error ? 8 : 4));
    });

    /// <summary>Активное окно приложения (редактор поверх главного — тоже), иначе главное.</summary>
    private Control? ActiveWindow()
    {
        if (host?.Invoke() is { } window)
        {
            return window.OwnedWindows.LastOrDefault(w => w.IsVisible) ?? window;
        }

        return Application.Current?.ApplicationLifetime switch
        {
            IClassicDesktopStyleApplicationLifetime desktop =>
                desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.Windows.LastOrDefault(w => w.IsVisible) ?? desktop.MainWindow,
            ISingleViewApplicationLifetime single => single.MainView,
            _ => null,
        };
    }

    private Task<FAContentDialogResult> ShowAsync(FAContentDialog dialog) =>
        TopLevel.GetTopLevel(ActiveWindow()) is { } topLevel ? dialog.ShowAsync(topLevel) : dialog.ShowAsync();
}
