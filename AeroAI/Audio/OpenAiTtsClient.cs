using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AtcNavDataDemo.Config;

namespace AeroAI.Audio;

/// <summary>
/// Minimal OpenAI Text-to-Speech client targeting /v1/audio/speech.
/// Writes audio to a temporary WAV file for playback.
/// </summary>
public sealed class OpenAiTtsClient : IDisposable
{
	private readonly HttpClient _httpClient;
	private readonly VoiceConfig _config;
	private bool _disposed;

	public OpenAiTtsClient(VoiceConfig config)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
		if (string.IsNullOrWhiteSpace(_config.ApiKey))
		{
			throw new ArgumentException("Voice API key is missing.", nameof(config));
		}

		_httpClient = new HttpClient
		{
			BaseAddress = new Uri(_config.ApiBase.TrimEnd('/') + "/"),
			Timeout = TimeSpan.FromSeconds(60)
		};
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
	}

	/// <summary>
	/// Synthesize text to a temporary WAV file. Returns the file path on success, or null on failure.
	/// </summary>
	public async Task<string?> SynthesizeToWavFileAsync(string text, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		var request = new
		{
			model = _config.Model,
			input = text,
			voice = _config.Voice,
			format = "wav"
		};

		HttpResponseMessage response = await _httpClient.PostAsJsonAsync("audio/speech", request, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
			throw new InvalidOperationException($"TTS request failed: {response.StatusCode} {response.ReasonPhrase}. {errorBody}");
		}

		string targetPath = Path.Combine(Path.GetTempPath(), $"aeroai_atc_{Guid.NewGuid():N}.wav");
		await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
		await using FileStream dest = File.Create(targetPath);
		await source.CopyToAsync(dest, cancellationToken);
		return targetPath;
	}

	public void Dispose()
	{
		if (_disposed) return;
		_httpClient.Dispose();
		_disposed = true;
	}
}
