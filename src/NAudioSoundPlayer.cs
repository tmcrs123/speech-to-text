using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SpeechToText;

// Synthesises ping/pong tones in-memory and plays them through WaveOutEvent.
// Tones are short enough that a fresh WaveOutEvent per play is cheap, and the
// stateless approach avoids overlap/disposal races.
internal sealed class NAudioSoundPlayer : ISoundPlayer
{
    private const int SampleRate = 44100;
    private const float Gain = 0.18f;
    private static readonly TimeSpan PingDuration = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan PongDuration = TimeSpan.FromMilliseconds(160);

    private readonly object _lock = new();
    private readonly List<WaveOutEvent> _active = new();
    private bool _disposed;

    public bool Muted { get; set; }

    public void PlayStartPing() => Play(frequency: 1320f, duration: PingDuration);

    public void PlayStopPong() => Play(frequency: 660f, duration: PongDuration);

    private void Play(float frequency, TimeSpan duration)
    {
        if (Muted) return;

        WaveOutEvent? output = null;
        try
        {
            var tone = new SignalGenerator(SampleRate, 1)
            {
                Gain = Gain,
                Frequency = frequency,
                Type = SignalGeneratorType.Sin,
            }.Take(duration);

            // Fade in/out a few ms to avoid the click that bare sine starts/stops make.
            var faded = new FadeInOutSampleProvider(tone, initiallySilent: true);
            faded.BeginFadeIn(15);
            // Schedule the fade-out so it ends right when the tone does.
            var sw = new System.Threading.Timer(_ =>
            {
                try { faded.BeginFadeOut(20); } catch { /* output already closed */ }
            }, null, Math.Max(0, (int)duration.TotalMilliseconds - 25), System.Threading.Timeout.Infinite);

            output = new WaveOutEvent();
            output.Init(faded.ToWaveProvider());
            output.PlaybackStopped += (_, _) =>
            {
                sw.Dispose();
                lock (_lock) _active.Remove(output!);
                output!.Dispose();
            };

            lock (_lock)
            {
                if (_disposed) { output.Dispose(); return; }
                _active.Add(output);
            }

            output.Play();
        }
        catch
        {
            // Audio device gone or no output device. Sounds are a non-critical
            // accessory; failure here must not break the dictation pipeline.
            output?.Dispose();
        }
    }

    public void Dispose()
    {
        WaveOutEvent[] snapshot;
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            snapshot = _active.ToArray();
            _active.Clear();
        }
        foreach (var o in snapshot)
        {
            try { o.Stop(); } catch { }
            try { o.Dispose(); } catch { }
        }
    }
}
