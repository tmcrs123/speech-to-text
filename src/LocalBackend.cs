using System.IO;
using System.Text;
using Whisper.net;

namespace SpeechToText;

// Whisper.NET (whisper.cpp) Transcription Backend. The factory is created
// once at construction (model load is expensive); each TranscribeAsync builds
// a fresh processor and feeds the in-memory WAV through it. No audio is
// written to disk at any point — the WAV stays a MemoryStream.
internal sealed class LocalBackend : ITranscriptionBackend, IDisposable
{
    private readonly WhisperFactory _factory;
    private bool _disposed;

    public LocalBackend(string modelPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException(
                $"Whisper model file not found at '{modelPath}'. Re-run the first-run wizard to download it.",
                modelPath);

        _factory = WhisperFactory.FromPath(modelPath);
    }

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var processor = _factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        using var stream = new MemoryStream(wav, writable: false);
        var sb = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(stream, ct).ConfigureAwait(false))
        {
            sb.Append(segment.Text);
        }
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _factory.Dispose();
    }
}
