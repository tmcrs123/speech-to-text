namespace SpeechToText;

internal sealed class SystemClock : IClock
{
    public IDisposable Schedule(TimeSpan delay, Action callback)
    {
        var cts = new CancellationTokenSource(delay);
        cts.Token.Register(callback);
        return cts;
    }
}
