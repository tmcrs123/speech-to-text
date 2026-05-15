namespace SpeechToText;

internal sealed class WindowTargeterAdapter : IWindowTargeter
{
    public IntPtr CaptureHwndNow() => WindowTargeter.CaptureHwndNow();
    public bool RestoreFocus(IntPtr hwnd) => WindowTargeter.RestoreFocus(hwnd);
}
