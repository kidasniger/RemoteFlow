using System.Runtime.InteropServices;

namespace RemoteFlow.Windows.Input;

public static class WindowsKeyboardController
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    private const ushort VK_BACK = 0x08;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_PAUSE = 0x13;
    private const ushort VK_CAPITAL = 0x14;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_PRIOR = 0x21;
    private const ushort VK_NEXT = 0x22;
    private const ushort VK_END = 0x23;
    private const ushort VK_HOME = 0x24;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_UP = 0x26;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_INSERT = 0x2D;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_NUMLOCK = 0x90;
    private const ushort VK_SCROLL = 0x91;
    private const ushort VK_F1 = 0x70;
    private const ushort VK_F2 = 0x71;
    private const ushort VK_F3 = 0x72;
    private const ushort VK_F4 = 0x73;
    private const ushort VK_F5 = 0x74;
    private const ushort VK_F6 = 0x75;
    private const ushort VK_F7 = 0x76;
    private const ushort VK_F8 = 0x77;
    private const ushort VK_F9 = 0x78;
    private const ushort VK_F10 = 0x79;
    private const ushort VK_F11 = 0x7A;
    private const ushort VK_F12 = 0x7B;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public static bool PressLabel(string? label, bool special = false, bool modifier = false)
    {
        var key = (label ?? string.Empty).Trim();
        if (key.Length == 0)
            return false;

        if (!special && key.Length == 1)
            return SendUnicode(key[0]);

        return TryResolveVirtualKey(key, out var virtualKey) && TapVirtualKey(virtualKey);
    }

    private static bool TapVirtualKey(ushort virtualKey)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(virtualKey, 0),
            CreateVirtualKeyInput(virtualKey, KEYEVENTF_KEYUP)
        };

        return Send(inputs);
    }

    private static bool SendUnicode(char character)
    {
        var inputs = new[]
        {
            CreateUnicodeInput(character, 0),
            CreateUnicodeInput(character, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP)
        };

        return Send(inputs);
    }

    private static INPUT CreateVirtualKeyInput(ushort virtualKey, uint flags) =>
        new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = flags
                }
            }
        };

    private static INPUT CreateUnicodeInput(char character, uint flags) =>
        new()
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = character,
                    dwFlags = flags
                }
            }
        };

    private static bool TryResolveVirtualKey(string label, out ushort virtualKey)
    {
        var key = label.ToUpperInvariant();
        virtualKey = key switch
        {
            "BACKSPACE" or "BACK" => VK_BACK,
            "TAB" => VK_TAB,
            "ENTER" or "RETURN" => VK_RETURN,
            "SHIFT" => VK_SHIFT,
            "CTRL" or "CONTROL" => VK_CONTROL,
            "ALT" or "OPTION" => VK_MENU,
            "PAUSE" or "BREAK" => VK_PAUSE,
            "CAPSLOCK" or "CAPS LOCK" => VK_CAPITAL,
            "ESC" or "ESCAPE" => VK_ESCAPE,
            "SPACE" => VK_SPACE,
            "PAGEUP" or "PAGE UP" => VK_PRIOR,
            "PAGEDOWN" or "PAGE DOWN" => VK_NEXT,
            "END" => VK_END,
            "HOME" => VK_HOME,
            "LEFT" or "ARROWLEFT" => VK_LEFT,
            "UP" or "ARROWUP" => VK_UP,
            "RIGHT" or "ARROWRIGHT" => VK_RIGHT,
            "DOWN" or "ARROWDOWN" => VK_DOWN,
            "INSERT" or "INS" => VK_INSERT,
            "DELETE" or "DEL" => VK_DELETE,
            "WIN" or "WINDOWS" or "META" or "CMD" => VK_LWIN,
            "RWIN" => VK_RWIN,
            "NUMLOCK" => VK_NUMLOCK,
            "SCROLLLOCK" => VK_SCROLL,
            "F1" => VK_F1,
            "F2" => VK_F2,
            "F3" => VK_F3,
            "F4" => VK_F4,
            "F5" => VK_F5,
            "F6" => VK_F6,
            "F7" => VK_F7,
            "F8" => VK_F8,
            "F9" => VK_F9,
            "F10" => VK_F10,
            "F11" => VK_F11,
            "F12" => VK_F12,
            _ => ResolveAsciiVirtualKey(key)
        };

        return virtualKey != 0;
    }

    private static ushort ResolveAsciiVirtualKey(string key)
    {
        if (key.Length != 1)
            return 0;

        var c = key[0];

        if (c is >= 'A' and <= 'Z')
            return (ushort)c;
        if (c is >= '0' and <= '9')
            return (ushort)c;

        return c switch
        {
            ',' => 0xBC,
            '.' => 0xBE,
            '/' => 0xBF,
            ';' => 0xBA,
            (char)92 => 0xDC,
            '[' => 0xDB,
            ']' => 0xDD,
            '-' => 0xBD,
            '=' => 0xBB,
            (char)96 => 0xC0,
            (char)39 => 0xDE,
            _ => 0
        };
    }

    private static bool Send(INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
}
