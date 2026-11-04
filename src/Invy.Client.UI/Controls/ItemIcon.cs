using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Explorer;
using Invy.Contracts.Catalog;

namespace Invy.Client.UI.Controls;

/// <summary>Иконка вещи или типа: эмодзи («📦») или символ Fluent; без своей иконки — по роли (место, контейнер, вещь).</summary>
public sealed class ItemIcon : Panel
{
    public static readonly StyledProperty<IconDto?> IconProperty = AvaloniaProperty.Register<ItemIcon, IconDto?>(nameof(Icon));

    public static readonly StyledProperty<TypeRole> RoleProperty = AvaloniaProperty.Register<ItemIcon, TypeRole>(nameof(Role));

    public static readonly StyledProperty<string?> TintProperty = AvaloniaProperty.Register<ItemIcon, string?>(nameof(Tint));

    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<ItemIcon, double>(nameof(Size), 18);

    private readonly TextBlock _emoji = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly FASymbolIcon _symbol = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public ItemIcon()
    {
        Children.Add(_emoji);
        Children.Add(_symbol);
        Update();
    }

    public IconDto? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public TypeRole Role
    {
        get => GetValue(RoleProperty);
        set => SetValue(RoleProperty, value);
    }

    /// <summary>Цвет символа «#RRGGBB»; <c>null</c> — цвет темы.</summary>
    public string? Tint
    {
        get => GetValue(TintProperty);
        set => SetValue(TintProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconProperty || change.Property == RoleProperty || change.Property == TintProperty || change.Property == SizeProperty)
        {
            Update();
        }
    }

    private void Update()
    {
        var glyph = ItemVisuals.Glyph(Icon);
        _emoji.Text = glyph;
        _emoji.FontSize = Size;
        _emoji.IsVisible = glyph is not null;

        _symbol.IsVisible = glyph is null;
        _symbol.FontSize = Size;
        _symbol.Symbol = Enum.TryParse<FASymbol>(ItemVisuals.Symbol(Icon, Role), ignoreCase: true, out var symbol) ? symbol : FASymbol.Tag;
        if (Tint is { } tint && Color.TryParse(tint, out var color))
        {
            _symbol.Foreground = new SolidColorBrush(color);
        }
        else if (Role != TypeRole.Item)
        {
            _symbol.Bind(FAIconElement.ForegroundProperty, this.GetResourceObservable("AccentTextFillColorPrimaryBrush"));
        }
        else
        {
            _symbol.ClearValue(FAIconElement.ForegroundProperty);
        }

        Width = Size + 6;
        Height = Size + 6;
    }
}
