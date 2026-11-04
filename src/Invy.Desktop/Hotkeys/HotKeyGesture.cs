using System.Diagnostics.CodeAnalysis;

namespace Invy.Desktop.Hotkeys;

/// <summary>Модификаторы. Значения совпадают с MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN WinAPI.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Термин WinAPI")]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>Глобальное сочетание клавиш, например «Ctrl+Alt+I». Хранится в настройках строкой.</summary>
public readonly record struct HotKeyGesture(HotKeyModifiers Modifiers, uint VirtualKey, string KeyName)
{
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotKeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotKeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    /// <summary>
    /// Разбирает «Ctrl+Alt+I», «ctrl + shift + f2», «Win+Shift+Space».
    /// Нужен хотя бы один модификатор: глобальная клавиша без модификаторов ломает обычный ввод.
    /// </summary>
    public static bool TryParse(string? text, out HotKeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = HotKeyModifiers.None;
        (uint Code, string Name)? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var modifier = ParseModifier(raw);
            if (modifier != HotKeyModifiers.None)
            {
                if (modifiers.HasFlag(modifier))
                {
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (key is not null || ParseKey(raw) is not { } parsed)
            {
                return false;
            }

            key = parsed;
        }

        if (modifiers == HotKeyModifiers.None || key is null)
        {
            return false;
        }

        gesture = new HotKeyGesture(modifiers, key.Value.Code, key.Value.Name);
        return true;
    }

    private static HotKeyModifiers ParseModifier(string token) => token.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotKeyModifiers.Control,
        "ALT" => HotKeyModifiers.Alt,
        "SHIFT" => HotKeyModifiers.Shift,
        "WIN" or "WINDOWS" => HotKeyModifiers.Win,
        _ => HotKeyModifiers.None,
    };

    private static (uint Code, string Name)? ParseKey(string token)
    {
        var upper = token.ToUpperInvariant();

        if (upper.Length == 1 && (char.IsAsciiLetterUpper(upper[0]) || char.IsAsciiDigit(upper[0])))
        {
            return (upper[0], upper);   // VK_A..VK_Z и VK_0..VK_9 совпадают с ASCII
        }

        if (upper.Length is 2 or 3 && upper[0] == 'F' && int.TryParse(upper.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            return ((uint)(0x70 + n - 1), upper);   // VK_F1 = 0x70
        }

        return upper switch
        {
            "SPACE" => (0x20u, "Space"),
            "INSERT" or "INS" => (0x2Du, "Insert"),
            "HOME" => (0x24u, "Home"),
            "END" => (0x23u, "End"),
            _ => null,
        };
    }
}
