namespace SpeechToText.Tests;

using SpeechToText;

internal sealed class FakeHotkeyListener : IHotkeyListener
{
    public event Action? HotkeyPressed;
    public Func<bool>? EscPressed { get; set; }

    public void RaiseHotkey() => HotkeyPressed?.Invoke();
    public bool RaiseEsc() => EscPressed?.Invoke() ?? false;
}

internal sealed class FakeAudioCapturer : IAudioCapturer
{
    public int StartCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int AbortCalls { get; private set; }
    public bool IsRecording { get; private set; }
    public byte[] WavToReturn { get; set; } = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"

    public void Start()
    {
        if (IsRecording) throw new InvalidOperationException("FakeAudioCapturer: already recording");
        IsRecording = true;
        StartCalls++;
    }

    public byte[] StopAndGetWav()
    {
        if (!IsRecording) throw new InvalidOperationException("FakeAudioCapturer: not recording");
        IsRecording = false;
        StopCalls++;
        return WavToReturn;
    }

    public void Abort()
    {
        IsRecording = false;
        AbortCalls++;
    }
}

internal sealed class FakeTranscriptionBackend : ITranscriptionBackend
{
    private readonly Queue<TaskCompletionSource<string>> _pending = new();
    public List<byte[]> Calls { get; } = new();

    public Task<string> TranscribeAsync(byte[] wav, CancellationToken ct)
    {
        Calls.Add(wav);
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Use synchronous continuations so test-driven SetResult fires the orchestrator
        // continuation on the test thread immediately. (RunContinuationsAsynchronously
        // would push it to the threadpool — bad for determinism.)
        tcs = new TaskCompletionSource<string>();
        _pending.Enqueue(tcs);
        return tcs.Task;
    }

    public void CompleteNext(string text)
    {
        if (_pending.Count == 0) throw new InvalidOperationException("no pending transcription");
        _pending.Dequeue().SetResult(text);
    }

    public void FailNext(Exception ex)
    {
        if (_pending.Count == 0) throw new InvalidOperationException("no pending transcription");
        _pending.Dequeue().SetException(ex);
    }

    public int PendingCount => _pending.Count;
}

internal sealed class FakeWindowTargeter : IWindowTargeter
{
    public IntPtr ForegroundHwnd { get; set; } = new IntPtr(0x1000);
    public List<IntPtr> Captured { get; } = new();
    public List<IntPtr> Restored { get; } = new();

    public IntPtr CaptureHwndNow()
    {
        Captured.Add(ForegroundHwnd);
        return ForegroundHwnd;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        Restored.Add(hwnd);
        return true;
    }
}

internal sealed class FakeClipboardPaster : IClipboardPaster
{
    private readonly Queue<(string text, Action onPasted)> _pending = new();
    public List<string> Pasted { get; } = new();

    public void Paste(string text, Action onPasted)
    {
        Pasted.Add(text);
        _pending.Enqueue((text, onPasted));
    }

    public void CompleteNext()
    {
        if (_pending.Count == 0) throw new InvalidOperationException("no pending paste");
        _pending.Dequeue().onPasted();
    }

    public int PendingCount => _pending.Count;
}

internal sealed class FakeClock : IClock
{
    private readonly List<Scheduled> _scheduled = new();
    private TimeSpan _now = TimeSpan.Zero;

    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var s = new Scheduled(_now + delay, callback);
        _scheduled.Add(s);
        return s;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        // Snapshot to allow callbacks to schedule new items without mutating during iteration.
        var fired = _scheduled
            .Where(s => !s.Cancelled && s.DueAt <= _now)
            .OrderBy(s => s.DueAt)
            .ToList();
        foreach (var s in fired)
        {
            _scheduled.Remove(s);
            s.Cancelled = true;
            s.Callback();
        }
    }

    private sealed class Scheduled : IDisposable
    {
        public TimeSpan DueAt;
        public Action Callback;
        public bool Cancelled;
        public Scheduled(TimeSpan dueAt, Action callback) { DueAt = dueAt; Callback = callback; }
        public void Dispose() { Cancelled = true; }
    }
}

internal sealed class Harness
{
    public FakeHotkeyListener Hotkey { get; } = new();
    public FakeAudioCapturer Audio { get; } = new();
    public FakeTranscriptionBackend Backend { get; } = new();
    public FakeWindowTargeter Targeter { get; } = new();
    public FakeClipboardPaster Paster { get; } = new();
    public FakeClock Clock { get; } = new();
    public DictationOrchestrator Orchestrator { get; }
    public List<int> ErrorFlashes { get; } = new();
    public List<DictationState> StateTransitions { get; } = new();

    public Harness(TimeSpan? maxDuration = null)
    {
        Orchestrator = new DictationOrchestrator(
            Hotkey, Audio, Backend, Targeter, Paster, Clock, maxDuration);
        Orchestrator.ErrorFlashRequested += () => ErrorFlashes.Add(ErrorFlashes.Count + 1);
        Orchestrator.StateChanged += s => StateTransitions.Add(s);
    }
}
