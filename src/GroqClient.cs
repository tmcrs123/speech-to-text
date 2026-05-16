using System.Net.Http;
using System.Net.Http.Headers;

namespace SpeechToText;

internal sealed class GroqClient : ITranscriptionBackend
{
    private const string Endpoint = "https://api.groq.com/openai/v1/audio/transcriptions";
    private const string Model = "whisper-large-v3-turbo";

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKeyProvider;

    public GroqClient(string apiKey) : this(() => apiKey) { }

    public GroqClient(Func<string?> apiKeyProvider)
    {
        _apiKeyProvider = apiKeyProvider;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    }

    public async Task<string> TranscribeAsync(byte[] wav, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();

        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "file", "audio.wav");
        form.Add(new StringContent(Model), "model");
        form.Add(new StringContent("text"), "response_format");

        var apiKey = _apiKeyProvider() ?? "";
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Groq {(int)resp.StatusCode}: {body}");

        // response_format=text returns the raw transcript
        return body.Trim();
    }
}
