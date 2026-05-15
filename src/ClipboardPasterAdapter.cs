namespace SpeechToText;

// Paste happens on the captured UI thread because Clipboard / Forms.Timer require
// a Windows Forms message pump. The orchestrator invokes Paste from a threadpool
// thread (transcription completion); this adapter posts the actual paste to the UI
// thread and fires `onPasted` once the paste has been dispatched.
internal sealed class ClipboardPasterAdapter : IClipboardPaster
{
    private readonly SynchronizationContext _ui;

    public ClipboardPasterAdapter(SynchronizationContext ui) { _ui = ui; }

    public void Paste(string text, Action onPasted)
    {
        _ui.Post(_ =>
        {
            try { ClipboardPaster.Paste(text); }
            finally { onPasted(); }
        }, null);
    }
}
