using CommunityToolkit.Mvvm.Input;

namespace Invy.Client.Core.Editor;

/// <summary>Выбор иконки и цвета — общий для редактора вещи и конструктора типа.</summary>
public interface ILookEditor
{
    bool HasOwnLook { get; }

    IRelayCommand<string?> SetEmojiCommand { get; }

    IRelayCommand<string?> SetSymbolCommand { get; }

    IRelayCommand<string?> SetColorCommand { get; }

    IRelayCommand ResetLookCommand { get; }
}
