using Avalonia.Controls;
using Avalonia.Media;
using Invy.Client.Core.Editor;

namespace Invy.Client.UI.Views.Editor;

/// <summary>Окно редактора вещи: модальное над главным окном или само по себе (быстрое добавление из трея).</summary>
public partial class ItemEditorWindow : Window
{
    private ItemEditorViewModel? _editor;

    public ItemEditorWindow()
    {
        InitializeComponent();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            Background = Brushes.Transparent;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_editor is not null)
        {
            _editor.CloseRequested -= Close;
        }

        _editor = DataContext as ItemEditorViewModel;
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
