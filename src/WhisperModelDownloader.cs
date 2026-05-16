using System.IO;
using System.Net.Http;

namespace SpeechToText;

// Downloads a ggml whisper model from Hugging Face into a per-machine
// %APPDATA%\SpeechToText\models\ directory with progress + cancellation, and
// verifies the downloaded size against the server's Content-Length before
// renaming into place. A partial download lives at .part until verified, so
// an interrupted download never leaves a half-file masquerading as ready.
internal static class WhisperModelDownloader
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    public static string ModelsDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SpeechToText", "models");
        }
    }

    public static string ModelPath(string modelSize) =>
        Path.Combine(ModelsDirectory, $"ggml-{modelSize}.bin");

    public static bool IsAlreadyDownloaded(string modelSize) =>
        File.Exists(ModelPath(modelSize));

    public static async Task DownloadAsync(
        string modelSize,
        IProgress<DownloadProgress>? progress,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelsDirectory);
        var finalPath = ModelPath(modelSize);
        var partPath = finalPath + ".part";

        var ownsClient = httpClient == null;
        var http = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            var url = $"{BaseUrl}ggml-{modelSize}.bin";
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new HttpRequestException($"Hugging Face returned HTTP {(int)resp.StatusCode} for {url}.");

            long? expectedSize = resp.Content.Headers.ContentLength;

            using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int n = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                    if (n == 0) break;
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    total += n;
                    progress?.Report(new DownloadProgress(total, expectedSize));
                }
            }

            // Integrity gate: the only thing we can cheaply check is that what
            // we received matches what the server said it would send. A short
            // file means a truncated download; reject it before renaming.
            if (expectedSize is long expected)
            {
                var actual = new FileInfo(partPath).Length;
                if (actual != expected)
                {
                    File.Delete(partPath);
                    throw new InvalidDataException(
                        $"Downloaded model size {actual} bytes does not match expected {expected} bytes. The download was incomplete.");
                }
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(partPath, finalPath);
        }
        catch
        {
            try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
            throw;
        }
        finally
        {
            if (ownsClient) http.Dispose();
        }
    }

    public readonly record struct DownloadProgress(long BytesReceived, long? TotalBytes)
    {
        public double? Fraction => TotalBytes is long t && t > 0 ? (double)BytesReceived / t : null;
    }
}
