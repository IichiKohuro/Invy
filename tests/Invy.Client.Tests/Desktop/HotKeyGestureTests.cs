using Invy.Desktop.Hotkeys;

namespace Invy.Client.Tests.Desktop;

public class HotKeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+I", HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x49u, "Ctrl+Alt+I")]
    [InlineData("alt + ctrl + i", HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x49u, "Ctrl+Alt+I")]
    [InlineData("Control+Shift+F2", HotKeyModifiers.Control | HotKeyModifiers.Shift, 0x71u, "Ctrl+Shift+F2")]
    [InlineData("Win+Shift+Space", HotKeyModifiers.Win | HotKeyModifiers.Shift, 0x20u, "Shift+Win+Space")]
    [InlineData("Ctrl+Alt+7", HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x37u, "Ctrl+Alt+7")]
    [InlineData("Ctrl+F24", HotKeyModifiers.Control, 0x87u, "Ctrl+F24")]
    public void Parses_valid_gestures(string text, HotKeyModifiers modifiers, uint virtualKey, string display)
    {
        HotKeyGesture.TryParse(text, out var gesture).ShouldBeTrue();

        gesture.Modifiers.ShouldBe(modifiers);
        gesture.VirtualKey.ShouldBe(virtualKey);
        gesture.ToString().ShouldBe(display);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I")]                 // без модификатора
    [InlineData("Ctrl+Alt")]          // без клавиши
    [InlineData("Ctrl+I+J")]          // две клавиши
    [InlineData("Ctrl+Ctrl+I")]       // повтор модификатора
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Ж")]            // VK зависит от раскладки — не поддерживаем
    public void Rejects_invalid_gestures(string? text) => HotKeyGesture.TryParse(text, out _).ShouldBeFalse();
}
