namespace SpeechToText;

internal interface ITranscriptionBackend
{
    Task<string> TranscribeAsync(byte[] wav, CancellationToken ct);
}
