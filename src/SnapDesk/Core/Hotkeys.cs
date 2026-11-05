using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SnapDesk.Core;

/// <summary>
/// Global kısayol kaydı. Çakışma durumunu (Win32 hata 1409) sessizce yutmaz,
/// çağırana hangi kısayolun neden kaydedilemediğini bildirir.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly Dictionary<int, string> _actions = new();
    private int _nextId = 1;
    private bool _created;

    public event Action<string>? Pressed;

    public sealed record Registration(string Action, string Shortcut, bool Success, int ErrorCode, string? ErrorText);

    public HotkeyManager()
    {
        CreateHandle(new CreateParams
        {
            Caption = "SnapDesk.Hotkeys",
            Parent = new IntPtr(-3), // HWND_MESSAGE: yalnızca mesaj alan pencere
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0,
            Style = 0,
            ExStyle = 0,
        });
        _created = true;
    }

    /// <summary>Tüm kısayolları yeniden kurar. Öncekiler bırakılır.</summary>
    public IReadOnlyList<Registration> Register(IReadOnlyDictionary<string, string> actionToShortcut)
    {
        UnregisterAll();
        var results = new List<Registration>();

        foreach (var (action, shortcut) in actionToShortcut)
        {
            if (string.IsNullOrWhiteSpace(shortcut))
            {
                continue;
            }

            if (!HotkeyParser.TryParse(shortcut, out var modifiers, out var key))
            {
                results.Add(new Registration(action, shortcut, false, 0, "Kısayol biçimi geçersiz."));
                continue;
            }

            var id = _nextId++;
            if (!RegisterHotKey(Handle, id, modifiers, (uint)key))
            {
                var error = Marshal.GetLastWin32Error();
                var text = error switch
                {
                    ErrorHotkeyAlreadyRegistered => "Bu kısayol başka bir uygulama tarafından kullanılıyor.",
                    _ => new Win32Exception(error).Message,
                };
                results.Add(new Registration(action, shortcut, false, error, text));
                continue;
            }

            _actions[id] = action;
            results.Add(new Registration(action, shortcut, true, 0, null));
        }

        return results;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
        {
            UnregisterHotKey(Handle, id);
        }

        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey)
        {
            var id = m.WParam.ToInt32();
            if (_actions.TryGetValue(id, out var action))
            {
                Pressed?.Invoke(action);
            }
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        if (_created)
        {
            DestroyHandle();
            _created = false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>"Ctrl+Shift+1" gibi metinleri Win32 kısayoluna çevirir.</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public static bool TryParse(string text, out uint modifiers, out Keys key)
    {
        modifiers = ModNoRepeat;
        key = Keys.None;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    modifiers |= ModControl;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "win" or "windows":
                    modifiers |= ModWin;
                    break;
                default:
                    return false;
            }
        }

        var last = parts[^1];
        var hasModifier = (modifiers & (ModControl | ModAlt | ModShift | ModWin)) != 0;
        if (last.Length == 1)
        {
            var ch = char.ToUpperInvariant(last[0]);
            if (ch is >= 'A' and <= 'Z')
            {
                key = (Keys)ch;
                return hasModifier;
            }

            if (ch is >= '0' and <= '9')
            {
                key = (Keys)ch;
                return hasModifier;
            }

            key = ch switch
            {
                ' ' => Keys.Space,
                _ => Keys.None,
            };
            return key != Keys.None && hasModifier;
        }

        var upper = last.ToUpperInvariant();
        if (upper.StartsWith('F') && int.TryParse(upper[1..], out var fn) && fn is >= 1 and <= 24)
        {
            key = (Keys)(0x70 + fn - 1);
            return true;
        }

        key = last.ToLowerInvariant() switch
        {
            "esc" or "escape" => Keys.Escape,
            "enter" or "return" => Keys.Enter,
            "tab" => Keys.Tab,
            "space" => Keys.Space,
            "printscreen" or "prtsc" => Keys.PrintScreen,
            "insert" => Keys.Insert,
            "home" => Keys.Home,
            "end" => Keys.End,
            "pageup" => Keys.PageUp,
            "pagedown" => Keys.PageDown,
            "left" => Keys.Left,
            "right" => Keys.Right,
            "up" => Keys.Up,
            "down" => Keys.Down,
            _ => Keys.None,
        };

        // Tek başına harf/rakam global kısayol olamaz: normal yazmayı engeller.
        return key != Keys.None && (hasModifier || key == Keys.PrintScreen);
    }

    public static string Describe(uint modifiers, Keys key)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
