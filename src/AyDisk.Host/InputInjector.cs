using System.Runtime.InteropServices;

namespace AyDisk.Host;

/// <summary>Applies remote mouse/keyboard events using Win32 SendInput.</summary>
public static class InputInjector
{
    public static void Handle(string type, double x, double y, int button, int delta, int keyCode)
    {
        switch (type)
        {
            case "move": Move(x, y); break;
            case "down": Move(x, y); Mouse(button switch { 1 => RIGHTDOWN, 2 => MIDDLEDOWN, _ => LEFTDOWN }); break;
            case "up": Move(x, y); Mouse(button switch { 1 => RIGHTUP, 2 => MIDDLEUP, _ => LEFTUP }); break;
            case "wheel": Mouse(WHEEL, delta); break;
            case "keydown": Key((ushort)keyCode, false); break;
            case "keyup": Key((ushort)keyCode, true); break;
        }
    }

    private static void Move(double x, double y)
    {
        x = Math.Clamp(x, 0, 1); y = Math.Clamp(y, 0, 1);
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion { mi = new MOUSEINPUT { dx = (int)(x * 65535), dy = (int)(y * 65535), dwFlags = MOVE | ABSOLUTE } }
        };
        Send(input);
    }

    private static void Mouse(uint flags, int data = 0) =>
        Send(new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags, mouseData = unchecked((uint)data) } } });

    private static void Key(ushort vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (ExtendedKeys.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKey(vk, 0), dwFlags = flags } }
        });
    }

    private static void Send(INPUT input) => SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

    // Arrows, Ins/Del, Home/End, PgUp/PgDn, Win keys, Apps, RCtrl, RAlt, NumLock, Divide
    private static readonly HashSet<ushort> ExtendedKeys = new()
    { 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28, 0x2D, 0x2E, 0x5B, 0x5C, 0x5D, 0xA3, 0xA5, 0x90, 0x6F };

    // ---- Win32 ----
    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOVE = 0x0001, LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010,
                       MIDDLEDOWN = 0x0020, MIDDLEUP = 0x0040, WHEEL = 0x0800, ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);
}
