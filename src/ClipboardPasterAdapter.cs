namespace SpeechToText;

// Paste happens on the captured UI thread because Clipboard / Forms.Timer require
// a Windows Forms message pump. The orchestrator invokes Paste from a threadpool
// thread (transcription completion); this adapter posts the actual paste to the UI
// thread and fires `onPasted` once the paste has been dispatched.
//
// `copyOnlyProvider` is read live at paste time (so a Settings change takes effect
// without a restart): when it returns true, the transcript is left on the clipboard
// for manual pasting instead of being auto-pasted via Ctrl+V.
internal sealed class ClipboardPasterAdapter : IClipboardPaster
{
    private readonly SynchronizationContext _ui;
    private readonly Func<bool> _copyOnlyProvider;

    public ClipboardPasterAdapter(SynchronizationContext ui, Func<bool> copyOnlyProvider)
    {
        _ui = ui;
        _copyOnlyProvider = copyOnlyProvider;
    }

    public void Paste(string text, Action onPasted)
    {
        _ui.Post(_ =>
        {
            try
            {
                if (_copyOnlyProvider()) ClipboardPaster.Copy(text);
                else ClipboardPaster.Paste(text);
            }
            finally { onPasted(); }
        }, null);
    }
}
