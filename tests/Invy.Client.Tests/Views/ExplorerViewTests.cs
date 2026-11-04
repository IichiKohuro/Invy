using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Editor;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Shell;
using Invy.Client.Tests.Support;
using Invy.Client.UI.Services;
using Invy.Client.UI.Views;
using Invy.Client.UI.Views.Editor;
using Invy.Client.UI.Views.Explorer;

namespace Invy.Client.Tests.Views;

/// <summary>
/// «Проводник», редактор вещи и выбор места на живых данных сервера в памяти.
/// Скриншоты — в <c>bin/…/screenshots</c>.
/// </summary>
public class ExplorerViewTests
{
    private static readonly string ScreenshotsDir = Path.Combine(AppContext.BaseDirectory, "screenshots");

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public async Task Explorer_shows_tree_contents_and_card(ThemeMode theme)
    {
        await using var backend = new TestBackend();
        var (shell, window) = await OpenAsync(backend, theme);
        var data = await SampleData.CreateAsync(backend.Session!);
        var explorer = await PlacesAsync(shell);

        await explorer.NavigateAsync(data.Bag);
        explorer.SelectedItem = explorer.Items.Single(i => i.Id == data.Cable);
        await TestBackend.WaitUntilAsync(() => explorer.Details?.Id == data.Cable);
        Render();

        explorer.Breadcrumbs.Select(b => b.Name).ShouldBe(["Места", "Дом", "Чердак", "Серая коробка", "Прозрачный пакет"]);
        explorer.Items.Select(i => i.Name).ShouldBe(["HDMI → HDMI, 200 см", "Type-A → Type-C, 100 см", "Type-C → Jack 3.5 мм"]);
        explorer.Details!.Fields.Select(f => $"{f.Label}: {f.Value}").ShouldBe(["Длина: 100 см", "Разъём 1: Type-A", "Разъём 2: Type-C", "Версия USB: 3.0", "Цвет: чёрный"]);

        var page = window.GetVisualDescendants().OfType<ExplorerPage>().Single();
        page.GetVisualDescendants().OfType<TreeViewItem>().Select(t => ((PlaceNodeViewModel)t.DataContext!).Name)
            .ShouldBe(["Дом", "Сарай", "Чердак", "Серая коробка", "Прозрачный пакет"], ignoreOrder: true);
        page.GetVisualDescendants().OfType<ListBoxItem>().Count(i => i.IsVisible && i.DataContext is ItemRowViewModel).ShouldBeGreaterThanOrEqualTo(3);
        Save(window, $"explorer-{theme.ToString().ToLowerInvariant()}");

        // Плитки.
        explorer.IsTiles = true;
        Render();
        Save(window, $"explorer-tiles-{theme.ToString().ToLowerInvariant()}");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Empty_places_offer_to_add_the_first_place()
    {
        await using var backend = new TestBackend();
        var (shell, window) = await OpenAsync(backend, ThemeMode.Light);
        var explorer = await PlacesAsync(shell, waitForItems: false);

        explorer.IsEmpty.ShouldBeTrue();
        explorer.EmptyTitle.ShouldBe("Пока нет ни одного места");
        Render();
        Save(window, "explorer-empty");
        window.Close();
    }

    [AvaloniaFact]
    public async Task Add_command_opens_editor_window_and_selects_saved_item()
    {
        await using var backend = new TestBackend();
        var (shell, window) = await OpenAsync(backend, ThemeMode.Light);
        var data = await SampleData.CreateAsync(backend.Session!);
        var explorer = await PlacesAsync(shell);
        await explorer.NavigateAsync(data.Bag);

        var adding = explorer.AddCommand.ExecuteAsync(null);
        await TestBackend.WaitUntilAsync(() => window.OwnedWindows.OfType<ItemEditorWindow>().Any());
        var editorWindow = window.OwnedWindows.OfType<ItemEditorWindow>().Single();
        var editor = (ItemEditorViewModel)editorWindow.DataContext!;

        // Тип по умолчанию — подходящий для пакета; выбираем кабель и заполняем характеристики.
        editor.LocationText.ShouldBe("Дом › Чердак › Серая коробка › Прозрачный пакет");
        editor.SelectedType = editor.Types.Single(t => t.Name == "Кабель");
        editor.Fields.Single(f => f.Definition.Name == "Длина").Number = 50;
        var first = editor.Fields.Single(f => f.Definition.Name == "Разъём 1");
        first.SelectedOption = first.Options.Single(o => o.Value == "Type-C");
        var second = editor.Fields.Single(f => f.Definition.Name == "Разъём 2");
        second.SelectedOption = second.Options.Single(o => o.Value == "Lightning");
        Render();
        Save(editorWindow, "editor-cable");

        editorWindow.GetVisualDescendants().OfType<NumericUpDown>().Count().ShouldBe(2);   // количество и длина
        editorWindow.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "NameBox").PlaceholderText.ShouldBe("Type-C → Lightning, 50 см");

        await editor.SaveCommand.ExecuteAsync(null);
        await adding.WaitAsync(TimeSpan.FromSeconds(10));

        editorWindow.IsVisible.ShouldBeFalse();
        explorer.SelectedItem!.Name.ShouldBe("Type-C → Lightning, 50 см");
        explorer.Items.Count.ShouldBe(4);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Move_to_dialog_lists_places_and_containers()
    {
        await using var backend = new TestBackend();
        var (shell, window) = await OpenAsync(backend, ThemeMode.Dark);
        var data = await SampleData.CreateAsync(backend.Session!);
        var explorer = await PlacesAsync(shell);
        await explorer.NavigateAsync(data.Bag);

        var moving = explorer.MoveToCommand.ExecuteAsync(explorer.Items.Single(i => i.Id == data.Cable));
        await TestBackend.WaitUntilAsync(() => window.GetVisualDescendants().OfType<LocationPickerView>().Any());
        var picker = window.GetVisualDescendants().OfType<LocationPickerView>().Single();
        var pickerVm = (LocationPickerViewModel)picker.DataContext!;
        await TestBackend.WaitUntilAsync(() => pickerVm.Tree.Count > 0);
        pickerVm.Tree.Select(n => n.Name).ShouldBe(["Дом"]);
        pickerVm.Tree[0].IsExpanded = true;
        await TestBackend.WaitUntilAsync(() => pickerVm.Tree[0].IsLoaded);
        pickerVm.SelectedNode = pickerVm.Tree[0].Children.Single(n => n.Name == "Сарай");
        pickerVm.Result!.Name.ShouldBe("Дом › Сарай");
        Render();
        Save(window, "move-to-dialog");

        var dialog = window.GetVisualDescendants().OfType<FAContentDialog>().Single();
        dialog.Hide(FAContentDialogResult.Primary);
        await moving.WaitAsync(TimeSpan.FromSeconds(10));

        explorer.Items.ShouldNotContain(i => i.Id == data.Cable);
        shell.Notice!.Message.ShouldBe("«Type-A → Type-C, 100 см» → Дом › Сарай");
        (await backend.Session!.Api.GetItemAsync(data.Cable)).ParentId.ShouldBe(data.Shed);
        Render();
        Save(window, "explorer-notice");
        window.Close();
    }

    private static async Task<(ShellViewModel Shell, MainWindow Window)> OpenAsync(TestBackend backend, ThemeMode theme)
    {
        var themes = new ThemeService(Avalonia.Application.Current!);
        themes.Apply(theme);
        var shell = new ShellViewModel(themes);
        var window = new MainWindow { DataContext = shell, Width = 1360, Height = 820 };
        backend.Host = window;
        await backend.ConnectAsync(shell);
        window.Show();
        return (shell, window);
    }

    private static async Task<ExplorerViewModel> PlacesAsync(ShellViewModel shell, bool waitForItems = true)
    {
        shell.Navigate("places");
        var explorer = (ExplorerViewModel)shell.CurrentPage!;
        await explorer.ActivateAsync();
        if (waitForItems)
        {
            await TestBackend.WaitUntilAsync(() => explorer.Items.Count > 0);
        }

        return explorer;
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
