using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Invy.Desktop.Views;

/// <summary>
/// Всплывающий быстрый поиск (горячая клавиша или клик по иконке в трее).
/// Создаётся один раз и скрывается, а не закрывается, чтобы появляться мгновенно.
/// </summary>
public partial class QuickSearchWindow : Window
{
    public QuickSearchWindow()
    {
        InitializeComponent();

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TransparencyLevelHint = [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
        }

        Deactivated += (_, _) => Hide();
    }

    /// <summary>Пользователь нажал Enter; аргумент — текст запроса.</summary>
    public event EventHandler<string>? Submitted;

    public void ShowAndFocus()
    {
        Query.Text = string.Empty;
        Show();
        Activate();
        Query.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Hide();
                e.Handled = true;
                break;
            case Key.Enter:
                var query = Query.Text?.Trim() ?? string.Empty;
                Hide();
                Submitted?.Invoke(this, query);
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    // Закрытие (Alt+F4) тоже только скрывает окно.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason is not WindowCloseReason.ApplicationShutdown and not WindowCloseReason.OSShutdown)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }
}
