using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpeechToText;

internal sealed class KeyboardHook : IDisposable, IHotkeyListener
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_ESCAPE = 0x1B;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;

    private readonly object _chordLock = new();
    private ChordDescriptor _chord;

    public event Action? HotkeyPressed;

    // Returns true to consume Esc, false to pass it through to the focused window.
    public Func<bool>? EscPressed { get; set; }

    public KeyboardHook(ChordDescriptor initialChord)
    {
        if (!initialChord.IsValid)
            throw new ArgumentException("Initial chord must be valid (modifier + non-modifier key).", nameof(initialChord));
        _chord = initialChord;
        _proc = HookCallback;
    }

    public ChordDescriptor Chord
    {
        get { lock (_chordLock) return _chord; }
        set
        {
            if (!value.IsValid) throw new ArgumentException("Chord must be valid.", nameof(value));
            lock (_chordLock) _chord = value;
        }
    }

    public void Install()
    {
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam);
                ChordDescriptor chord;
                lock (_chordLock) chord = _chord;

                if (vk == (int)chord.Key && ModifiersMatch(chord.Modifiers))
                {
                    try { HotkeyPressed?.Invoke(); }
                    catch (Exception ex) { Console.Error.WriteLine($"hotkey handler threw: {ex}"); }
                    return (IntPtr)1;
                }

                if (vk == VK_ESCAPE)
                {
                    bool consumed = false;
                    try { consumed = EscPressed?.Invoke() ?? false; }
                    catch (Exception ex) { Console.Error.WriteLine($"esc handler threw: {ex}"); }
                    if (consumed) return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static bool ModifiersMatch(ChordModifiers required)
    {
        bool ctrl = IsDown(VK_LCONTROL, VK_RCONTROL);
        bool shift = IsDown(VK_LSHIFT, VK_RSHIFT);
        bool alt = IsDown(VK_LMENU, VK_RMENU);
        bool win = IsDown(VK_LWIN, VK_RWIN);
        return ctrl == required.HasFlag(ChordModifiers.Ctrl)
            && shift == required.HasFlag(ChordModifiers.Shift)
            && alt == required.HasFlag(ChordModifiers.Alt)
            && win == required.HasFlag(ChordModifiers.Win);
    }

    private static bool IsDown(int vk1, int vk2) =>
        (GetAsyncKeyState(vk1) & 0x8000) != 0 || (GetAsyncKeyState(vk2) & 0x8000) != 0;

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
