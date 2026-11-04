using Avalonia.Headless.XUnit;
using Invy.Client.UI.Views;

namespace Invy.Client.Tests.Views;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Main_window_opens()
    {
        var window = new MainWindow();

        window.Show();

        window.IsVisible.ShouldBeTrue();
        window.Title.ShouldBe("Invy");
    }
}
