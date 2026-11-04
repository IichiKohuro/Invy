using Avalonia.Controls;
using Avalonia.Interactivity;
using Invy.Client.Core.Catalog;

namespace Invy.Client.UI.Views.Catalog;

public partial class TagsPage : UserControl
{
    public TagsPage() => InitializeComponent();

    /// <summary>Открыли палитру — цвет назначится этому тегу.</summary>
    private void OnColorClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TagRowViewModel row } && DataContext is TagsViewModel page)
        {
            page.ColorTarget = row;
        }
    }
}
