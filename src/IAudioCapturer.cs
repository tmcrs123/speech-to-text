namespace SpeechToText;

internal interface IAudioCapturer
{
    void Start();
    byte[] StopAndGetWav();
    void Abort();

    // Raised on the audio capture thread after each PCM buffer (~20 Hz with the
    // 50 ms BufferMilliseconds setting). Value is short-window RMS in [0, 1].
    // Implementations that never capture audio (test doubles) may leave it un-fired.
    event Action<float>? LevelChanged;
}
