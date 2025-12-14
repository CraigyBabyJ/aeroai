using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;

namespace AeroAI.Llm;

public sealed class AeroAiPhraseEngine : IDisposable
{
	private readonly OpenAiLlmClient _llmClient;

	private readonly string _systemPrompt;

	private bool _disposed;

	public AeroAiPhraseEngine(string apiKey, string? model = null, string? baseUrl = null, string? systemPromptPath = null)
	{
		_llmClient = new OpenAiLlmClient(apiKey, model, baseUrl);
		_systemPrompt = LoadSystemPrompt(systemPromptPath);
	}

	public async Task<string> GenerateAtcTransmissionAsync(AtcContext context, string pilotTransmission, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
		{
			return "Say again?";
		}
		string userPrompt = BuildUserPrompt(context, pilotTransmission);
		LogDebugPrompt(_systemPrompt, userPrompt);
		if (ShouldLogApiRequests())
		{
			LogApiRequest(_systemPrompt, userPrompt);
		}
		string response = await _llmClient.GenerateChatCompletionAsync(_systemPrompt, userPrompt, cancellationToken);
		LogDebugResponse(response);
		if (ShouldLogApiRequests())
		{
			LogApiResponse(response);
		}
		return response.Trim();
	}

	private static bool ShouldLogApiRequests()
	{
		string? environmentVariable = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
		return !string.IsNullOrWhiteSpace(environmentVariable) && (environmentVariable.Equals("1", StringComparison.OrdinalIgnoreCase) || environmentVariable.Equals("true", StringComparison.OrdinalIgnoreCase) || environmentVariable.Equals("yes", StringComparison.OrdinalIgnoreCase));
	}

	private static void LogApiRequest(string systemPrompt, string userPrompt)
	{
		string value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("═══════════════════════════════════════════════════════════════");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
		handler.AppendLiteral("API REQUEST [");
		handler.AppendFormatted(value);
		handler.AppendLiteral("]");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine("═══════════════════════════════════════════════════════════════");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("SYSTEM PROMPT:");
		stringBuilder.AppendLine("───────────────────────────────────────────────────────────────");
		stringBuilder.AppendLine(systemPrompt);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("USER PROMPT:");
		stringBuilder.AppendLine("───────────────────────────────────────────────────────────────");
		stringBuilder.AppendLine(userPrompt);
		stringBuilder.AppendLine("═══════════════════════════════════════════════════════════════");
		stringBuilder.AppendLine();
		string text = stringBuilder.ToString();
		Console.Write(text);
		WriteToLogFile(text);
	}

	private static void LogApiResponse(string response)
	{
		string value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
		StringBuilder stringBuilder = new StringBuilder();
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
		handler.AppendLiteral("API RESPONSE [");
		handler.AppendFormatted(value);
		handler.AppendLiteral("]:");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine("───────────────────────────────────────────────────────────────");
		stringBuilder.AppendLine(response);
		stringBuilder.AppendLine("═══════════════════════════════════════════════════════════════");
		stringBuilder.AppendLine();
		string text = stringBuilder.ToString();
		Console.Write(text);
		WriteToLogFile(text);
	}

	private static void WriteToLogFile(string logText)
	{
		try
		{
			string? environmentVariable = Environment.GetEnvironmentVariable("AEROAI_LOG_FILE");
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				File.AppendAllText(environmentVariable, logText);
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine("[Log file write error: " + ex.Message + "]");
		}
	}

	private string BuildUserPrompt(AtcContext context, string pilotTransmission)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("CONTEXT_JSON:");
		stringBuilder.AppendLine("```json");
		stringBuilder.AppendLine(context.ToJson());
		stringBuilder.AppendLine("```");
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("PILOT_TRANSMISSION:");
		StringBuilder stringBuilder2 = stringBuilder;
		StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder2);
		handler.AppendLiteral("\"");
		handler.AppendFormatted(pilotTransmission);
		handler.AppendLiteral("\"");
		stringBuilder2.AppendLine(ref handler);
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("Using ONLY this information and obeying your role and permissions, respond with a single ICAO-style ATC transmission.");
		return stringBuilder.ToString();
	}

	private static string LoadSystemPrompt(string? path)
	{
		string text = path ?? "prompts/aeroai_system_prompt.txt";
		if (!File.Exists(text))
		{
			throw new FileNotFoundException("System prompt file not found: " + text + ". Please ensure the file exists or provide a valid path via AEROAI_SYSTEM_PROMPT_PATH.");
		}
		return File.ReadAllText(text);
	}

	private static void LogDebugPrompt(string systemPrompt, string userPrompt)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("=== ATC DEBUG PROMPT ===");
		sb.AppendLine("SYSTEM PROMPT:");
		sb.AppendLine(systemPrompt);
		sb.AppendLine("USER PROMPT:");
		sb.AppendLine(userPrompt);
		sb.AppendLine("=== END ATC DEBUG PROMPT ===");
		string log = sb.ToString();
		Console.Write(log);
		WriteToLogFile(log);
	}

	private static void LogDebugResponse(string response)
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("=== ATC DEBUG RESPONSE ===");
		sb.AppendLine(response);
		sb.AppendLine("=== END ATC DEBUG RESPONSE ===");
		string log = sb.ToString();
		Console.Write(log);
		WriteToLogFile(log);
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_llmClient?.Dispose();
			_disposed = true;
		}
	}
}
