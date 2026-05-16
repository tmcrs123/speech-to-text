namespace SpeechToText;

internal interface ISoundPlayer : IDisposable
{
    // Mute is an in-memory toggle for this slice (#6). Slice #7 will add
    // persistence via ConfigStore.
    bool Muted { get; set; }

    // Short, higher-pitched cue at Recording start.
    void PlayStartPing();

    // Short, lower-pitched cue at Recording end (any cause).
    void PlayStopPong();
}
