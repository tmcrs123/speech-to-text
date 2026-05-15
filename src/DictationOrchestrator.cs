namespace SpeechToText;

internal enum DictationState
{
    Idle,
    Recording,
    Transcribing,
    Pasting,
}

internal sealed class DictationOrchestrator : IDisposable
{
    private readonly IHotkeyListener _hotkey;
    private readonly IAudioCapturer _audio;
    private readonly ITranscriptionBackend _backend;
    private readonly IWindowTargeter _targeter;
    private readonly IClipboardPaster _paster;
    private readonly IClock _clock;
    private readonly TimeSpan _maxDuration;

    private readonly object _lock = new();
    private readonly LinkedList<Dictation> _queue = new();
    private IDisposable? _maxDurationTimer;
    private bool _disposed;

    // Raised when a dictation is silently dropped because transcription failed or
    // returned empty/whitespace-only text. Tray (slice #6) consumes this to flash red.
    public event Action? ErrorFlashRequested;

    public DictationOrchestrator(
        IHotkeyListener hotkey,
        IAudioCapturer audio,
        ITranscriptionBackend backend,
        IWindowTargeter targeter,
        IClipboardPaster paster,
        IClock clock,
        TimeSpan? maxDuration = null)
    {
        _hotkey = hotkey;
        _audio = audio;
        _backend = backend;
        _targeter = targeter;
        _paster = paster;
        _clock = clock;
        _maxDuration = maxDuration ?? TimeSpan.FromSeconds(120);

        _hotkey.HotkeyPressed += OnHotkeyTapped;
        _hotkey.EscPressed = OnEscPressed;
    }

    public DictationState CurrentState
    {
        get
        {
            lock (_lock)
            {
                if (_queue.Count == 0) return DictationState.Idle;
                return _queue.First!.Value.Phase switch
                {
                    Phase.Recording => DictationState.Recording,
                    Phase.AwaitingTranscription or Phase.Transcribing => DictationState.Transcribing,
                    Phase.AwaitingPaste or Phase.Pasting => DictationState.Pasting,
                    _ => DictationState.Idle,
                };
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _hotkey.HotkeyPressed -= OnHotkeyTapped;
            _hotkey.EscPressed = null;
            _maxDurationTimer?.Dispose();
            _maxDurationTimer = null;
        }
    }

    private void OnHotkeyTapped()
    {
        lock (_lock)
        {
            if (_disposed) return;

            // Only one Dictation can be in Recording at a time (mic exclusivity).
            // A tap while something is mid-Recording stops it.
            var recording = FindByPhase(Phase.Recording);
            if (recording != null)
            {
                StopRecording(recording);
                return;
            }

            // No active recording — start a new Dictation. The front may still be in
            // Transcribing/Pasting; the new one runs Recording in parallel and queues
            // behind it.
            StartNewRecording();
        }
    }

    private bool OnEscPressed()
    {
        lock (_lock)
        {
            if (_disposed) return false;

            // Esc is honoured only when the public state is Recording, which means the
            // front-of-queue Dictation is in Recording (PRD/issue #5). Never honoured
            // when something earlier is Transcribing/Pasting.
            if (_queue.Count == 0) return false;
            var front = _queue.First!.Value;
            if (front.Phase != Phase.Recording) return false;

            AbortRecording(front);
            return true;
        }
    }

    private void StartNewRecording()
    {
        _audio.Start();
        var d = new Dictation { Phase = Phase.Recording };
        _queue.AddLast(d);
        _maxDurationTimer = _clock.Schedule(_maxDuration, OnMaxDurationElapsed);
    }

    private void OnMaxDurationElapsed()
    {
        lock (_lock)
        {
            if (_disposed) return;
            var recording = FindByPhase(Phase.Recording);
            if (recording == null) return;
            StopRecording(recording);
        }
    }

    private void StopRecording(Dictation d)
    {
        _maxDurationTimer?.Dispose();
        _maxDurationTimer = null;

        d.Wav = _audio.StopAndGetWav();
        // HWND captured at Recording-end, per acceptance criterion.
        d.TargetHwnd = _targeter.CaptureHwndNow();

        if (ReferenceEquals(_queue.First!.Value, d))
        {
            d.Phase = Phase.Transcribing;
            BeginTranscription(d);
        }
        else
        {
            d.Phase = Phase.AwaitingTranscription;
        }
    }

    private void AbortRecording(Dictation d)
    {
        _maxDurationTimer?.Dispose();
        _maxDurationTimer = null;
        _audio.Abort();
        _queue.Remove(d);
        AdvanceFrontIfReady();
    }

    private void BeginTranscription(Dictation d)
    {
        var cts = new CancellationTokenSource();
        d.TranscribeCts = cts;
        Task<string> task;
        try
        {
            task = _backend.TranscribeAsync(d.Wav!, cts.Token);
        }
        catch
        {
            OnTranscriptionComplete(d, null, failed: true);
            return;
        }
        task.ContinueWith(t =>
        {
            if (t.IsFaulted || t.IsCanceled)
                OnTranscriptionComplete(d, null, failed: true);
            else
                OnTranscriptionComplete(d, t.Result, failed: false);
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    private void OnTranscriptionComplete(Dictation d, string? text, bool failed)
    {
        bool fireFlash = false;
        lock (_lock)
        {
            if (_disposed) return;

            if (failed || string.IsNullOrWhiteSpace(text))
            {
                _queue.Remove(d);
                fireFlash = true;
                AdvanceFrontIfReady();
            }
            else
            {
                d.TranscribedText = text;
                if (ReferenceEquals(_queue.First!.Value, d))
                {
                    StartPaste(d);
                }
                else
                {
                    d.Phase = Phase.AwaitingPaste;
                }
            }
        }
        if (fireFlash) ErrorFlashRequested?.Invoke();
    }

    private void StartPaste(Dictation d)
    {
        d.Phase = Phase.Pasting;
        // RestoreFocus is synchronous and safe from any thread (the AttachThreadInput
        // dance in WindowTargeter handles cross-thread cases). Paste is callback-based
        // because the production adapter posts to the UI thread.
        _targeter.RestoreFocus(d.TargetHwnd);
        _paster.Paste(d.TranscribedText!, () => OnPasteDone(d));
    }

    private void OnPasteDone(Dictation d)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _queue.Remove(d);
            AdvanceFrontIfReady();
        }
    }

    private void AdvanceFrontIfReady()
    {
        if (_queue.Count == 0) return;
        var front = _queue.First!.Value;
        switch (front.Phase)
        {
            case Phase.AwaitingTranscription:
                front.Phase = Phase.Transcribing;
                BeginTranscription(front);
                break;
            case Phase.AwaitingPaste:
                StartPaste(front);
                break;
        }
        // Phase.Recording at the front: wait for it to stop normally.
    }

    private Dictation? FindByPhase(Phase phase)
    {
        for (var node = _queue.First; node != null; node = node.Next)
            if (node.Value.Phase == phase)
                return node.Value;
        return null;
    }

    private enum Phase
    {
        Recording,
        AwaitingTranscription,
        Transcribing,
        AwaitingPaste,
        Pasting,
    }

    private sealed class Dictation
    {
        public Phase Phase;
        public byte[]? Wav;
        public IntPtr TargetHwnd;
        public string? TranscribedText;
        public CancellationTokenSource? TranscribeCts;
    }
}
