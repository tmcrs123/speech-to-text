namespace SpeechToText.Tests;

using System.Net;
using System.Net.Http;
using SpeechToText;
using Xunit;

public sealed class GroqApiKeyValidatorTests
{
    [Fact]
    public async Task ValidateAsync_On200_ReturnsOk()
    {
        using var http = HttpClientWith(HttpStatusCode.OK, "{}", captureAuth: out var captured);

        var result = await GroqApiKeyValidator.ValidateAsync("test-key", http);

        Assert.Equal(GroqApiKeyValidator.Outcome.Ok, result.Outcome);
        Assert.True(result.IsOk);
        Assert.Equal("Bearer test-key", captured());
    }

    [Fact]
    public async Task ValidateAsync_On401_ReturnsUnauthorized()
    {
        using var http = HttpClientWith(HttpStatusCode.Unauthorized, "{\"error\":\"invalid\"}");

        var result = await GroqApiKeyValidator.ValidateAsync("bad-key", http);

        Assert.Equal(GroqApiKeyValidator.Outcome.Unauthorized, result.Outcome);
        Assert.Equal(401, result.StatusCode);
    }

    [Fact]
    public async Task ValidateAsync_On403_ReturnsUnauthorized()
    {
        using var http = HttpClientWith(HttpStatusCode.Forbidden, "");

        var result = await GroqApiKeyValidator.ValidateAsync("blocked", http);

        Assert.Equal(GroqApiKeyValidator.Outcome.Unauthorized, result.Outcome);
        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task ValidateAsync_On500_ReturnsUnexpectedStatus()
    {
        using var http = HttpClientWith(HttpStatusCode.InternalServerError, "boom");

        var result = await GroqApiKeyValidator.ValidateAsync("k", http);

        Assert.Equal(GroqApiKeyValidator.Outcome.UnexpectedStatus, result.Outcome);
        Assert.Equal(500, result.StatusCode);
    }

    [Fact]
    public async Task ValidateAsync_OnHttpRequestException_ReturnsNetworkError()
    {
        using var http = new HttpClient(new ThrowingHandler(new HttpRequestException("no route to host")));

        var result = await GroqApiKeyValidator.ValidateAsync("k", http);

        Assert.Equal(GroqApiKeyValidator.Outcome.NetworkError, result.Outcome);
    }

    private static HttpClient HttpClientWith(HttpStatusCode status, string body)
        => HttpClientWith(status, body, out _);

    private static HttpClient HttpClientWith(HttpStatusCode status, string body, out Func<string?> captureAuth)
    {
        var handler = new StubHandler(status, body);
        captureAuth = () => handler.LastAuthorization;
        return new HttpClient(handler);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public string? LastAuthorization { get; private set; }

        public StubHandler(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body) });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        private readonly Exception _ex;
        public ThrowingHandler(Exception ex) => _ex = ex;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw _ex;
    }
}
