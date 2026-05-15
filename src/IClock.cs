namespace SpeechToText;

internal interface IClock
{
    // Schedule a one-shot callback after `delay`. Dispose the returned handle to cancel
    // before it fires (a no-op if it has already fired).
    IDisposable Schedule(TimeSpan delay, Action callback);
}
