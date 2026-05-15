using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeechToText;

internal static class Paster
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    public static void Paste(string text, IntPtr targetHwnd)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (targetHwnd != IntPtr.Zero)
            SetForegroundWindow(targetHwnd);

        Clipboard.SetText(text);
        SendCtrlV();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void SendCtrlV()
    {
        var inputs = new INPUT[4];
        inputs[0] = KeyInput(VK_CONTROL, false);
        inputs[1] = KeyInput(VK_V, false);
        inputs[2] = KeyInput(VK_V, true);
        inputs[3] = KeyInput(VK_CONTROL, true);

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput sent {sent}/{inputs.Length}: {Marshal.GetLastWin32Error()}");
    }

    private static INPUT KeyInput(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
