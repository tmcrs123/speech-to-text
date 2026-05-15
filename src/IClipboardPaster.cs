namespace SpeechToText;

internal interface IClipboardPaster
{
    // Pastes `text`, then invokes `onPasted`. Implementations may complete the paste
    // synchronously (call `onPasted` before returning) or asynchronously (e.g. post to
    // a UI thread and invoke `onPasted` once the paste has been dispatched).
    void Paste(string text, Action onPasted);
}
