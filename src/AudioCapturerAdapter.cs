namespace SpeechToText;

// Wraps the per-cycle AudioCapture instance behind the orchestrator-facing
// Start / StopAndGetWav / Abort interface. Resolves the configured input
// device id on each Start so settings changes apply to the next Dictation
// without restart.
internal sealed class AudioCapturerAdapter : IAudioCapturer
{
    private readonly ConfigStore _config;
    private AudioCapture? _capture;

    public AudioCapturerAdapter(ConfigStore config)
    {
        _config = config;
    }

    public event Action<float>? LevelChanged;

    public void Start()
    {
        _capture?.Dispose();
        var deviceNumber = AudioDevices.ResolveWaveInDeviceNumber(_config.GetInputDeviceId());
        _capture = new AudioCapture();
        _capture.LevelChanged += level => LevelChanged?.Invoke(level);
        _capture.Start(deviceNumber);
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
