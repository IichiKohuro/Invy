using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Invy.Client.Core.Catalog;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Shell;
using Invy.Client.Tests.Support;
using Invy.Client.UI.Services;
using Invy.Client.UI.Views;
using Invy.Client.UI.Views.Catalog;
using Invy.Contracts.Catalog;
using Invy.Contracts.Items;

namespace Invy.Client.Tests.Views;

/// <summary>Экран подключения, «Типы» с конструктором, «Справочники», «Теги» — на живом сервере в памяти.</summary>
public class CatalogViewTests
{
    private static readonly string ScreenshotsDir = Path.Combine(AppContext.BaseDirectory, "screenshots");

    [AvaloniaFact]
    public async Task Connect_screen_shows_pairing_code_while_waiting()
    {
        await using var backend = new TestBackend("Pairing");
        var shell = new ShellViewModel(new ThemeService(Avalonia.Application.Current!));
        var connect = new ConnectViewModel(backend.NewConnection(deviceName: "Ноутбук"), backend.Server.BaseAddress.ToString(), _ => Task.CompletedTask);
        shell.Connect = connect;
        var window = new MainWindow { DataContext = shell, Width = 1100, Height = 720 };
        window.Show();
        shell.ShowConnect.ShouldBeTrue();

        var connecting = connect.ConnectCommand.ExecuteAsync(null);
        await TestBackend.WaitUntilAsync(() => connect.IsWaitingForApproval);
        Render();
        connect.PairingCode!.Length.ShouldBe(6);
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PairingCode").Text.ShouldBe(connect.PairingCode);
        Save(window, "connect-pairing");

        connect.ConnectCancelCommand.Execute(null);
        await connecting.WaitAsync(TimeSpan.FromSeconds(10));
        connect.IsBusy.ShouldBeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public async Task Types_page_type_editor_with_preview_and_lists_tab()
    {
        await using var backend = new TestBackend();
        var (shell, window) = await OpenAsync(backend);
        shell.Navigate("types");
        var page = (TypesViewModel)shell.CurrentPage!;
        page.Selected = page.Rows.Single(r => r.Name == "Кабель");
        Render();
        page.Rows.Where(r => r.IsGroupStart).Select(r => r.GroupTitle).ShouldBe(["Места", "Контейнеры", "Вещи"]);
        Save(window, "types");

        // Конструктор: форма слева, предпросмотр справа.
        var editing = page.EditCommand.ExecuteAsync(page.Selected);
        await TestBackend.WaitUntilAsync(() => window.OwnedWindows.OfType<TypeEditorWindow>().Any());
        var editorWindow = window.OwnedWindows.OfType<TypeEditorWindow>().Single();
        var editor = (TypeEditorViewModel)editorWindow.DataContext!;
        editor.PreviewDelay = TimeSpan.Zero;
        editor.Fields[0].IsExpanded = true;
        var preview = editor.Preview!;
        preview.Fields.Single(f => f.Definition.Name == "Длина").Number = 150;
        var first = preview.Fields.Single(f => f.Definition.Name == "Разъём 1");
        first.SelectedOption = first.Options.Single(o => o.Value == "Type-C");
        Render();
        preview.AutoName.ShouldBe("Type-C, 150 см");
        editorWindow.GetVisualDescendants().OfType<TextBox>().Count(t => t.IsEffectivelyVisible).ShouldBeGreaterThan(8);
        Save(editorWindow, "type-editor");
        editor.CancelCommand.Execute(null);
        await editing.WaitAsync(TimeSpan.FromSeconds(10));

        // Вкладка «Справочники».
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 1;
        page.Lists.Selected = page.Lists.Lists.Single(l => l.Name == "Разъёмы");
        Render();
        page.Lists.Editor!.Options.Count.ShouldBe(23);
        Save(window, "value-lists");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Tags_page_lists_tags_with_counts()
    {
        await using var backend = new TestBackend();
        var (shell, window) = await OpenAsync(backend);
        var api = backend.Session!.Api;
        var charging = await api.CreateTagAsync(new SaveTagRequest("зарядка", "#107C10"));
        await api.CreateTagAsync(new SaveTagRequest("в ремонт", "#D13438"));
        await api.CreateTagAsync(new SaveTagRequest("отдать"));
        var cable = backend.Session.Catalog.ActiveTypes.Single(t => t.Name == "Кабель");
        await api.CreateItemAsync(new SaveItemRequest(cable.Id, Name: "USB-C 1 м", TagIds: [charging.Id]));

        shell.Navigate("tags");
        var page = (TagsViewModel)shell.CurrentPage!;
        await TestBackend.WaitUntilAsync(() => page.Tags.Count == 3 && page.Tags.Any(t => t.Tag.ItemCount == 1));
        Render();
        page.Tags.Single(t => t.Name == "зарядка").CountText.ShouldBe("1 вещь");
        Save(window, "tags");
        window.Close();
    }

    private static async Task<(ShellViewModel Shell, MainWindow Window)> OpenAsync(TestBackend backend)
    {
        var themes = new ThemeService(Avalonia.Application.Current!);
        themes.Apply(ThemeMode.Light);
        var shell = new ShellViewModel(themes);
        var window = new MainWindow { DataContext = shell, Width = 1360, Height = 820 };
        backend.Host = window;
        await backend.ConnectAsync(shell);
        window.Show();
        return (shell, window);
    }

    private static void Render()
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);
    }

    private static void Save(TopLevel topLevel, string name)
    {
        using var frame = topLevel.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        Directory.CreateDirectory(ScreenshotsDir);
        frame.Save(Path.Combine(ScreenshotsDir, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
