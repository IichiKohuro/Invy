using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Windowing;

namespace Invy.Client.UI.Views;

public partial class MainWindow : FAAppWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // Свой заголовок со строкой поиска, как в Проводнике Windows 11.
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.Height = 48;

        // Mica — только Windows 11 (сборка 22000+); на Windows 10 и Linux остаётся сплошной фон темы.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            Background = Brushes.Transparent;
        }

        KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.F, KeyModifiers.Control),
            Command = new RelayCommand(() => TitleSearch.Focus()),
        });
    }
}
