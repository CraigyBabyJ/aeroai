using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.UI.Services;

internal sealed class WhisperFastSttService : ISttService, IDisposable
{
    private readonly WhisperFastHost _host;
    private readonly HttpClient _httpClient = new();
    private readonly Action<string>? _log;
    private readonly Func<string?>? _initialPromptProvider;
    public bool IsAvailable { get; }

    public WhisperFastSttService(WhisperFastHost host, Action<string>? log = null, bool available = true, Func<string?>? initialPromptProvider = null)
    {
        _host = host;
        _log = log;
        IsAvailable = available;
        _initialPromptProvider = initialPromptProvider;
    }

    public async Task<string?> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        var started = await _host.StartAsync(cancellationToken).ConfigureAwait(false);
        if (!started)
            throw new InvalidOperationException("whisper-fast not available");

        var initialPrompt = _initialPromptProvider?.Invoke();
        object request = string.IsNullOrWhiteSpace(initialPrompt)
            ? new { wavPath }
            : new { wavPath, initialPrompt };
        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _httpClient.PostAsync($"http://127.0.0.1:{_host.Port}/transcribe", content, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"whisper-fast HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");
        }

        var respJson = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(respJson);
        if (doc.RootElement.TryGetProperty("ok", out var okElem) && okElem.ValueKind == JsonValueKind.False)
        {
            var err = doc.RootElement.TryGetProperty("error", out var errElem) ? errElem.GetString() : "unknown";
            throw new InvalidOperationException(err ?? "whisper-fast error");
        }

        if (doc.RootElement.TryGetProperty("text", out var textElem))
        {
            var text = textElem.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        throw new InvalidOperationException("whisper-fast response missing text");
    }

    public void Dispose()
    {
        _host.Dispose();
        _httpClient.Dispose();
    }
}
