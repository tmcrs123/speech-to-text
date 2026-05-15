using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeechToText;

internal static class ClipboardPaster
{
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const int RESTORE_DELAY_MS = 100;

    public static void Paste(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        IDataObject? saved = SaveClipboard();

        // UnicodeText format ensures emoji and non-ASCII glyphs survive the round trip.
        Clipboard.SetText(text, TextDataFormat.UnicodeText);

        SendCtrlV();

        ScheduleRestore(saved);
    }

    private static IDataObject? SaveClipboard()
    {
        try
        {
            IDataObject? current = Clipboard.GetDataObject();
            if (current == null) return null;

            var copy = new DataObject();
            foreach (string format in current.GetFormats(autoConvert: false))
            {
                try
                {
                    object? data = current.GetData(format, autoConvert: false);
                    if (data != null) copy.SetData(format, autoConvert: false, data);
                }
                catch
                {
                    // Some formats (e.g. live OLE handles from a closed owner) refuse to round-trip; skip them.
                }
            }
            return copy;
        }
        catch
        {
            return null;
        }
    }

    private static void ScheduleRestore(IDataObject? saved)
    {
        if (saved == null) return;

        // Give the target app time to read the clipboard from the dispatched Ctrl+V
        // before we overwrite it with the prior contents.
        var timer = new System.Windows.Forms.Timer { Interval = RESTORE_DELAY_MS };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            try
            {
                Clipboard.SetDataObject(saved, copy: true);
            }
            catch
            {
                // Clipboard may be locked by another process; drop silently.
            }
        };
        timer.Start();
    }

    private static void SendCtrlV()
    {
        var inputs = new INPUT[4];
        inputs[0] = KeyInput(VK_CONTROL, up: false);
        inputs[1] = KeyInput(VK_V, up: false);
        inputs[2] = KeyInput(VK_V, up: true);
        inputs[3] = KeyInput(VK_CONTROL, up: true);

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
