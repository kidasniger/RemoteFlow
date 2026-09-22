using System.Runtime.InteropServices;

namespace RemoteFlow.Windows.Input;

public static class WindowsMouseController
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

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
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static bool Execute(string? type, float? x, float? y, float? dx, float? dy)
    {
        return (type ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "MOVE" => MoveToNormalized(x ?? 0f, y ?? 0f),
            "MOVE_RELATIVE" => MoveRelative(dx ?? 0f, dy ?? 0f),
            "LEFT_CLICK" => Click(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
            "RIGHT_CLICK" => Click(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            "DOUBLE_CLICK" => DoubleClick(),
            "SCROLL_UP" => Scroll(120),
            "SCROLL_DOWN" => Scroll(-120),
            _ => false
        };
    }

    private static bool MoveToNormalized(float normalizedX, float normalizedY)
    {
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
            return false;

        var x = Math.Clamp(normalizedX, 0f, 1f);
        var y = Math.Clamp(normalizedY, 0f, 1f);

        var absoluteX = (int)Math.Round(x * 65535d);
        var absoluteY = (int)Math.Round(y * 65535d);

        return Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = absoluteX,
                    dy = absoluteY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
                }
            }
        });
    }

    private static bool MoveRelative(float deltaX, float deltaY)
    {
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
            return false;

        var dx = (int)Math.Round(deltaX * width);
        var dy = (int)Math.Round(deltaY * height);

        if (dx == 0 && dy == 0)
            return true;

        return Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_MOVE
                }
            }
        });
    }

    private static bool DoubleClick() =>
        Click(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP) &&
        Click(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

    private static bool Scroll(int delta) =>
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    mouseData = unchecked((uint)delta),
                    dwFlags = MOUSEEVENTF_WHEEL
                }
            }
        });

    private static bool Click(uint down, uint up)
    {
        var inputs = new[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = down } }
            },
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion { mi = new MOUSEINPUT { dwFlags = up } }
            }
        };

        return Send(inputs);
    }

    private static bool Send(INPUT input) => Send(new[] { input });

    private static bool Send(INPUT[] inputs) =>
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
}
