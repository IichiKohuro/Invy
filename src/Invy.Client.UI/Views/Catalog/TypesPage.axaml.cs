using Avalonia.Controls;
using Invy.Client.Core.Catalog;

namespace Invy.Client.UI.Views.Catalog;

/// <summary>«Типы»: двойной щелчок по типу — открыть конструктор.</summary>
public partial class TypesPage : UserControl
{
    public TypesPage()
    {
        InitializeComponent();
        TypesList.DoubleTapped += (_, _) =>
        {
            if (DataContext is TypesViewModel { Selected: { } row } page)
            {
                page.EditCommand.Execute(row);
            }
        };
    }
}
