using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Llm;

public sealed class OllamaLlmClient : ILlmClient, IDisposable
{
	private sealed class OllamaResponse
	{
		public string Response { get; set; } = string.Empty;

		public bool Done { get; set; }
	}

	private readonly HttpClient _httpClient;

	private readonly string _baseUrl;

	private readonly string _model;

	private readonly JsonSerializerOptions _jsonOptions;

	private bool _disposed;

	public OllamaLlmClient(string? baseUrl = null, string? model = null)
	{
		_baseUrl = baseUrl?.TrimEnd('/') ?? "http://192.168.1.100:11434";
		_model = model ?? "llama3.1:8b";
		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};
		_httpClient = new HttpClient
		{
			BaseAddress = new Uri(_baseUrl),
			Timeout = TimeSpan.FromSeconds(120.0)
		};
	}

	public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(prompt))
		{
			throw new ArgumentException("Prompt cannot be null or empty.", "prompt");
		}
		var requestBody = new
		{
			model = _model,
			prompt = prompt,
			stream = false
		};
		Exception? lastException = null;
		for (int attempt = 0; attempt <= 2; attempt++)
		{
			try
			{
				if (attempt > 0)
				{
					await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
				}
				HttpResponseMessage response = await _httpClient.PostAsJsonAsync("/api/generate", requestBody, cancellationToken);
				response.EnsureSuccessStatusCode();
				OllamaResponse? jsonResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>(_jsonOptions, cancellationToken);
				if (jsonResponse == null)
				{
					throw new InvalidOperationException("Failed to parse Ollama response: response body was null.");
				}
				if (string.IsNullOrWhiteSpace(jsonResponse.Response))
				{
					throw new InvalidOperationException("Ollama response contained no text.");
				}
				return jsonResponse.Response;
			}
			catch (HttpRequestException ex)
			{
				lastException = ex;
				if (attempt < 2)
				{
					continue;
				}
				throw new InvalidOperationException($"Failed to communicate with Ollama server at {_baseUrl} after {3} attempts: {ex.Message}", ex);
			}
			catch (TaskCanceledException ex2) when (ex2.InnerException is TimeoutException || ex2.CancellationToken.IsCancellationRequested)
			{
				lastException = ex2;
				if (attempt < 2 && !ex2.CancellationToken.IsCancellationRequested)
				{
					continue;
				}
				throw new InvalidOperationException($"Request to Ollama server timed out after 120 seconds (attempt {attempt + 1}/{3}).", ex2);
			}
			catch (JsonException ex3)
			{
				throw new InvalidOperationException("Failed to parse JSON response from Ollama: " + ex3.Message, ex3);
			}
		}
		throw new InvalidOperationException($"Request failed after {3} attempts.", lastException);
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_httpClient?.Dispose();
			_disposed = true;
		}
	}
}
