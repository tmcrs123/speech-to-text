namespace SpeechToText;

internal interface IWindowTargeter
{
    IntPtr CaptureHwndNow();
    bool RestoreFocus(IntPtr hwnd);
}
