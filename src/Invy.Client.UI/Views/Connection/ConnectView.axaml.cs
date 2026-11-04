using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Invy.Client.UI.Views.Connection;

public partial class ConnectView : UserControl
{
    public ConnectView() => InitializeComponent();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AddressBox.Focus();
    }
}
