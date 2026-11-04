using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Invy.Client.UI.Views.Editor;

public partial class ItemEditorView : UserControl
{
    public ItemEditorView() => InitializeComponent();

    /// <summary>Сразу можно печатать название.</summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        NameBox.Focus();
    }
}
