namespace SpeechToText;

internal interface IAudioCapturer
{
    void Start();
    byte[] StopAndGetWav();
    void Abort();
}
