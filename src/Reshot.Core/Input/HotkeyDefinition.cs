using System.Diagnostics.CodeAnalysis;

namespace Reshot.Core.Input;

/// <summary>Win32 <c>RegisterHotKey</c> modifier flags (fsModifiers).</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0x0,
    Alt = 0x1,      // MOD_ALT
    Control = 0x2,  // MOD_CONTROL
    Shift = 0x4,    // MOD_SHIFT
    Win = 0x8,      // MOD_WIN
    NoRepeat = 0x4000, // MOD_NOREPEAT
}

/// <summary>
/// A parsed global hotkey: a set of modifiers plus a single virtual-key code.
/// Pure, Windows-API-free, and fully testable — the App layer feeds
/// <see cref="Modifiers"/> and <see cref="VirtualKey"/> straight into RegisterHotKey.
/// Accepts strings like "PrtScn", "Ctrl+Shift+A", "Alt+F4", "Win+D".
/// </summary>
public sealed class HotkeyDefinition
{
    public HotkeyModifiers Modifiers { get; }

    /// <summary>Virtual-key code (VK_*) of the main key.</summary>
    public uint VirtualKey { get; }

    /// <summary>Canonical name of the main key (e.g. "PrtScn", "A", "F4").</summary>
    public string KeyName { get; }

    private HotkeyDefinition(HotkeyModifiers modifiers, uint virtualKey, string keyName)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        KeyName = keyName;
    }

    /// <summary>Parses a hotkey string, throwing <see cref="FormatException"/> on failure.</summary>
    public static HotkeyDefinition Parse(string text)
    {
        if (!TryParse(text, out var def, out var error))
            throw new FormatException(error);
        return def;
    }

    /// <summary>
    /// Tries to parse a hotkey string. Tokens are '+'-separated; the last token is
    /// the main key, the rest are modifiers. Whitespace and case are ignored.
    /// </summary>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out HotkeyDefinition? definition,
        out string error)
    {
        definition = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey string is empty.";
            return false;
        }

        var tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = "Hotkey string has no keys.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? mainToken = null;

        foreach (var token in tokens)
        {
            if (TryMapModifier(token, out var mod))
            {
                modifiers |= mod;
            }
            else if (mainToken is null)
            {
                mainToken = token;
            }
            else
            {
                error = $"More than one non-modifier key in '{text}'.";
                return false;
            }
        }

        if (mainToken is null)
        {
            error = $"Hotkey '{text}' has modifiers but no main key.";
            return false;
        }

        if (!TryMapKey(mainToken, out var vk, out var canonical))
        {
            error = $"Unknown key '{mainToken}' in '{text}'.";
            return false;
        }

        // MOD_NOREPEAT: one press = one event, no auto-repeat while held.
        definition = new HotkeyDefinition(modifiers | HotkeyModifiers.NoRepeat, vk, canonical);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }

    private static bool TryMapModifier(string token, out HotkeyModifiers modifier)
    {
        modifier = token.ToLowerInvariant() switch
        {
            "ctrl" or "control" => HotkeyModifiers.Control,
            "alt" => HotkeyModifiers.Alt,
            "shift" => HotkeyModifiers.Shift,
            "win" or "windows" or "meta" or "super" => HotkeyModifiers.Win,
            _ => HotkeyModifiers.None,
        };
        return modifier != HotkeyModifiers.None;
    }

    private static bool TryMapKey(string token, out uint vk, out string canonical)
    {
        var key = token.ToLowerInvariant();

        // Single letter A-Z → VK matches ASCII uppercase.
        if (key.Length == 1 && key[0] is >= 'a' and <= 'z')
        {
            vk = (uint)char.ToUpperInvariant(key[0]);
            canonical = key.ToUpperInvariant();
            return true;
        }

        // Single digit 0-9 → VK matches ASCII digit.
        if (key.Length == 1 && key[0] is >= '0' and <= '9')
        {
            vk = key[0];
            canonical = key;
            return true;
        }

        // Function keys F1-F24 → VK_F1 (0x70) .. VK_F24 (0x87).
        if (key.Length is 2 or 3 && key[0] == 'f' && int.TryParse(key.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + (fn - 1));
            canonical = "F" + fn;
            return true;
        }

        if (NamedKeys.TryGetValue(key, out var mapped))
        {
            vk = mapped.Vk;
            canonical = mapped.Canonical;
            return true;
        }

        vk = 0;
        canonical = string.Empty;
        return false;
    }

    /// <summary>Named non-alphanumeric keys → (virtual-key code, canonical display name).</summary>
    private static readonly Dictionary<string, (uint Vk, string Canonical)> NamedKeys = new()
    {
        ["prtscn"] = (0x2C, "PrtScn"),
        ["printscreen"] = (0x2C, "PrtScn"),
        ["print"] = (0x2C, "PrtScn"),
        ["snapshot"] = (0x2C, "PrtScn"),
        ["insert"] = (0x2D, "Insert"),
        ["ins"] = (0x2D, "Insert"),
        ["delete"] = (0x2E, "Delete"),
        ["del"] = (0x2E, "Delete"),
        ["home"] = (0x24, "Home"),
        ["end"] = (0x23, "End"),
        ["pageup"] = (0x21, "PageUp"),
        ["pgup"] = (0x21, "PageUp"),
        ["pagedown"] = (0x22, "PageDown"),
        ["pgdn"] = (0x22, "PageDown"),
        ["space"] = (0x20, "Space"),
        ["spacebar"] = (0x20, "Space"),
        ["tab"] = (0x09, "Tab"),
        ["enter"] = (0x0D, "Enter"),
        ["return"] = (0x0D, "Enter"),
        ["esc"] = (0x1B, "Esc"),
        ["escape"] = (0x1B, "Esc"),
        ["backspace"] = (0x08, "Backspace"),
        ["pause"] = (0x13, "Pause"),
        ["scrolllock"] = (0x91, "ScrollLock"),
        ["up"] = (0x26, "Up"),
        ["down"] = (0x28, "Down"),
        ["left"] = (0x25, "Left"),
        ["right"] = (0x27, "Right"),
    };
}
