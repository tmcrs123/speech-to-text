using System.Net.Http;
using System.Net.Http.Headers;

namespace SpeechToText;

// Lightweight live check that a Groq API key is accepted. Hits the OpenAI-
// compatible /models endpoint — a cheap GET that returns 200 for a good key,
// 401/403 for a bad one. Used by the first-run wizard before persisting.
internal static class GroqApiKeyValidator
{
    private const string ModelsEndpoint = "https://api.groq.com/openai/v1/models";

    public enum Outcome
    {
        Ok,
        Unauthorized,
        NetworkError,
        UnexpectedStatus,
    }

    public readonly record struct Result(Outcome Outcome, int StatusCode, string? Detail)
    {
        public bool IsOk => Outcome == Outcome.Ok;
    }

    public static async Task<Result> ValidateAsync(string apiKey, HttpClient http, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ModelsEndpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return new Result(Outcome.NetworkError, 0, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return new Result(Outcome.NetworkError, 0, ex.Message);
        }

        using (resp)
        {
            int status = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode)
                return new Result(Outcome.Ok, status, null);
            if (status is 401 or 403)
                return new Result(Outcome.Unauthorized, status, null);
            string body;
            try { body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false); }
            catch { body = ""; }
            return new Result(Outcome.UnexpectedStatus, status, body);
        }
    }
}
