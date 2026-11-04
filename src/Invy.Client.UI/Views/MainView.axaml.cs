using Avalonia.Controls;
using Invy.Client.Core.Shell;

namespace Invy.Client.UI.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        NoticeBar.Closed += (_, _) =>
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.Notice = null;
            }
        };
    }
}
