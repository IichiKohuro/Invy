using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Invy.Client.Core.Shell;
using Invy.Client.UI.Services;
using Invy.Client.UI.Views;

namespace Invy.Client.Tests.Views;

/// <summary>
/// Спайк фазы 0: окно с FluentAvalonia рендерится в обеих темах и переключает разделы.
/// Скриншоты сохраняются в <c>bin/…/screenshots</c> для ручного просмотра.
/// </summary>
public class MainWindowTests
{
    private static readonly string ScreenshotsDir = Path.Combine(AppContext.BaseDirectory, "screenshots");

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light, "home")]
    [InlineData(ThemeMode.Dark, "home")]
    [InlineData(ThemeMode.Light, "places")]
    [InlineData(ThemeMode.Dark, "places")]
    [InlineData(ThemeMode.Dark, "settings")]
    public void Renders_page_in_theme(ThemeMode theme, string page)
    {
        var themes = new ThemeService(Application.Current!);
        themes.Apply(theme);
        var shell = new ShellViewModel(themes);
        shell.Navigate(page);

        var window = new MainWindow { DataContext = shell, Width = 1100, Height = 720 };
        window.Show();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(3);

        using var frame = window.CaptureRenderedFrame();
        frame.ShouldNotBeNull();
        Directory.CreateDirectory(ScreenshotsDir);
        frame.Save(Path.Combine(ScreenshotsDir, $"{page}-{theme.ToString().ToLowerInvariant()}.png"), PngBitmapEncoderOptions.Default);

        // Фон области содержимого: светлая тема — светлый, тёмная — тёмный.
        var luminance = Luminance(frame, x: 1050, y: 690);
        if (theme == ThemeMode.Dark)
        {
            luminance.ShouldBeLessThan(0.3);
        }
        else
        {
            luminance.ShouldBeGreaterThan(0.7);
        }

        window.Close();
    }

    private static double Luminance(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        var scale = buffer.Size.Width / 1100.0;
        var px = (int)(x * scale);
        var py = (int)(y * scale);
        var bgra = Marshal.ReadInt32(buffer.Address + (py * buffer.RowBytes) + (px * 4));
        var b = (bgra & 0xFF) / 255.0;
        var g = ((bgra >> 8) & 0xFF) / 255.0;
        var r = ((bgra >> 16) & 0xFF) / 255.0;
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }
}
