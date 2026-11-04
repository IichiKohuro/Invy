using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Invy.Client.Core.Explorer;
using Invy.Client.UI.Converters;
using Invy.Contracts.Catalog;

namespace Invy.Client.UI.Controls;

/// <summary>
/// Иконка вещи или типа: эмодзи («📦»), символ Fluent или своя картинка (фото вещи);
/// без своей иконки — по роли (место, контейнер, вещь).
/// </summary>
public sealed class ItemIcon : Panel
{
    public static readonly StyledProperty<IconDto?> IconProperty = AvaloniaProperty.Register<ItemIcon, IconDto?>(nameof(Icon));

    public static readonly StyledProperty<TypeRole> RoleProperty = AvaloniaProperty.Register<ItemIcon, TypeRole>(nameof(Role));

    public static readonly StyledProperty<string?> TintProperty = AvaloniaProperty.Register<ItemIcon, string?>(nameof(Tint));

    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<ItemIcon, double>(nameof(Size), 18);

    private readonly TextBlock _emoji = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly FASymbolIcon _symbol = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, IsVisible = false };
    private Guid? _imageId;

    public ItemIcon()
    {
        Children.Add(_emoji);
        Children.Add(_symbol);
        Children.Add(_image);
        ClipToBounds = true;
        Update();
    }

    /// <summary>Загрузка своих картинок (превью фото по id); задаёт приложение при подключении к серверу.</summary>
    public static Func<Guid, Task<byte[]?>>? PhotoLoader { get; set; }

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
        var custom = Icon is { Kind: IconKind.Custom } && Guid.TryParse(Icon.Value, out var imageId) ? imageId : (Guid?)null;
        if (custom != _imageId)
        {
            _imageId = custom;
            _image.Source = null;
            _image.IsVisible = false;
            if (custom is { } id && PhotoLoader is { } loader)
            {
                _ = LoadImageAsync(id, loader);
            }
        }

        var glyph = ItemVisuals.Glyph(Icon);
        _emoji.Text = glyph;
        _emoji.FontSize = Size;
        _emoji.IsVisible = glyph is not null;

        _symbol.IsVisible = glyph is null && !_image.IsVisible;
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

    private async Task LoadImageAsync(Guid id, Func<Guid, Task<byte[]?>> loader)
    {
        var bytes = await loader(id);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_imageId == id && BitmapConverters.Decode(bytes, 96) is { } bitmap)
            {
                _image.Source = bitmap;
                _image.IsVisible = true;
                _symbol.IsVisible = false;
                _emoji.IsVisible = false;
            }
        });
    }
}
