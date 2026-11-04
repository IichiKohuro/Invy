using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Invy.Client.Core.Catalog;

namespace Invy.Client.UI.Views.Catalog;

/// <summary>Конструктор типа: форма слева, предпросмотр формы вещи справа.</summary>
public partial class TypeEditorWindow : Window
{
    private TypeEditorViewModel? _editor;

    public TypeEditorWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            Background = Brushes.Transparent;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        NameBox.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_editor is not null)
        {
            _editor.CloseRequested -= Close;
        }

        _editor = DataContext as TypeEditorViewModel;
        if (_editor is not null)
        {
            _editor.CloseRequested += Close;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_editor is not null)
        {
            _editor.CloseRequested -= Close;
        }

        base.OnClosed(e);
    }
}
