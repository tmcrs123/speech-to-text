namespace SpeechToText;

// Wraps the per-cycle AudioCapture instance behind the orchestrator-facing
// Start / StopAndGetWav / Abort interface.
internal sealed class AudioCapturerAdapter : IAudioCapturer
{
    private AudioCapture? _capture;

    public void Start()
    {
        _capture?.Dispose();
        _capture = new AudioCapture();
        _capture.Start();
    }

    public byte[] StopAndGetWav()
    {
        if (_capture == null) throw new InvalidOperationException("not recording");
        try { return _capture.StopAndGetWav(); }
        finally
        {
            _capture.Dispose();
            _capture = null;
        }
    }

    public void Abort()
    {
        _capture?.Dispose();
        _capture = null;
    }
}
