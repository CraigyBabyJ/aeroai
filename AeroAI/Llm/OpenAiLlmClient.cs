#define DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Llm;

public sealed class OpenAiLlmClient : ILlmClient, IDisposable
{
	private sealed class OpenAiResponse
	{
		public OpenAiChoice[]? Choices { get; set; }
	}

	private sealed class OpenAiChoice
	{
		public OpenAiMessage? Message { get; set; }
	}

	private sealed class OpenAiMessage
	{
		public string? Content { get; set; }
	}

	private readonly HttpClient _httpClient;

	private readonly string _apiKey;

	private readonly string _model;

	private readonly JsonSerializerOptions _jsonOptions;

	private bool _disposed;

	public OpenAiLlmClient(string apiKey, string? model = null, string? baseUrl = null)
	{
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			throw new ArgumentException("API key cannot be null or empty.", "apiKey");
		}
		_apiKey = apiKey;
		_model = model ?? "gpt-4o-mini";
		if (!string.IsNullOrWhiteSpace(baseUrl) && !baseUrl.TrimEnd('/').Equals("https://api.openai.com/v1", StringComparison.OrdinalIgnoreCase))
		{
			Debug.WriteLine($"Warning: baseUrl parameter '{baseUrl}' ignored. Using hardcoded '{"https://api.openai.com/v1/"}' to ensure correct URL construction.");
		}
		_jsonOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		};
		_httpClient = new HttpClient
		{
			BaseAddress = new Uri("https://api.openai.com/v1/"),
			Timeout = TimeSpan.FromSeconds(60.0)
		};
		_httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
	}

	public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		try
		{
			return (await _httpClient.GetAsync("models", cancellationToken)).IsSuccessStatusCode;
		}
		catch
		{
			return false;
		}
	}

	public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await GenerateChatCompletionAsync(string.Empty, prompt, cancellationToken);
	}

	public async Task<string> GenerateChatCompletionAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(userPrompt))
		{
			throw new ArgumentException("User prompt cannot be null or empty.", "userPrompt");
		}
		List<object> messages = new List<object>();
		if (!string.IsNullOrWhiteSpace(systemPrompt))
		{
			messages.Add(new
			{
				role = "system",
				content = systemPrompt
			});
		}
		messages.Add(new
		{
			role = "user",
			content = userPrompt
		});
		var requestBody = new
		{
			model = _model,
			messages = messages.ToArray(),
			temperature = 0.3,
			max_tokens = 200
		};
		try
		{
			string fullUrl = $"{_httpClient.BaseAddress}{"chat/completions"}";
			HttpResponseMessage response = await _httpClient.PostAsJsonAsync("chat/completions", requestBody, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				string errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
				string errorMessage;
				if (!string.IsNullOrWhiteSpace(errorBody) && (errorBody.Contains("<html>", StringComparison.OrdinalIgnoreCase) || errorBody.Contains("nginx", StringComparison.OrdinalIgnoreCase) || errorBody.Contains("<center>", StringComparison.OrdinalIgnoreCase)))
				{
					errorMessage = $"Request intercepted by proxy/nginx (not reaching OpenAI servers). Status: {response.StatusCode} ({response.ReasonPhrase}). URL: {fullUrl}. This usually indicates a VPN, proxy, or network configuration issue. Try disabling VPN/proxy or check your network settings.";
				}
				else
				{
					string errorDetails = (string.IsNullOrWhiteSpace(errorBody) ? "No error details provided" : ((errorBody.Length > 500) ? (errorBody.Substring(0, 500) + "...") : errorBody));
					errorMessage = $"OpenAI API returned status {response.StatusCode} ({response.ReasonPhrase}). URL: {fullUrl}. Error details: {errorDetails}";
				}
				throw new HttpRequestException(errorMessage);
			}
			OpenAiResponse? jsonResponse = await response.Content.ReadFromJsonAsync<OpenAiResponse>(_jsonOptions, cancellationToken);
			if (jsonResponse == null)
			{
				throw new InvalidOperationException("Failed to parse OpenAI response: response body was null.");
			}
			if (jsonResponse.Choices == null || jsonResponse.Choices.Length == 0)
			{
				throw new InvalidOperationException("OpenAI response contained no choices.");
			}
			string? content = jsonResponse.Choices[0].Message?.Content;
			if (string.IsNullOrWhiteSpace(content))
			{
				throw new InvalidOperationException("OpenAI response contained no text.");
			}
			return content;
		}
		catch (HttpRequestException ex)
		{
			throw new InvalidOperationException("Failed to communicate with OpenAI API: " + ex.Message, ex);
		}
		catch (TaskCanceledException ex2) when (ex2.InnerException is TimeoutException)
		{
			throw new InvalidOperationException("Request to OpenAI API timed out after 60 seconds.", ex2);
		}
		catch (JsonException ex3)
		{
			throw new InvalidOperationException("Failed to parse JSON response from OpenAI: " + ex3.Message, ex3);
		}
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
