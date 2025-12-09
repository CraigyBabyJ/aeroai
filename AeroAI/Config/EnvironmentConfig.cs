using System;
using System.IO;

namespace AeroAI.Config;

public static class EnvironmentConfig
{
	public static void Load()
	{
		string text = ".env";
		if (File.Exists(text))
		{
			LoadFromFile(text);
		}
	}

	public static string GetOpenAiApiKey()
	{
		string? environmentVariable = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
		if (string.IsNullOrWhiteSpace(environmentVariable))
		{
			throw new InvalidOperationException("OPENAI_API_KEY not found. Please set it in .env file or environment variables.");
		}
		if (!environmentVariable.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) && !environmentVariable.StartsWith("sk-proj-", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("Invalid OpenAI API key format. Keys should start with 'sk-' or 'sk-proj-', but found '" + environmentVariable.Substring(0, Math.Min(10, environmentVariable.Length)) + "...'. Please check your .env file.");
		}
		return environmentVariable;
	}

	public static string GetOpenAiModel()
	{
		return Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
	}

	public static string GetOpenAiBaseUrl()
	{
		return Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1";
	}

	public static string GetSystemPromptPath()
	{
		return Environment.GetEnvironmentVariable("AEROAI_SYSTEM_PROMPT_PATH") ?? "prompts/aeroai_system_prompt.txt";
	}

	private static void LoadFromFile(string filePath)
	{
		if (!File.Exists(filePath))
		{
			return;
		}
		string[] array = File.ReadAllLines(filePath);
		foreach (string text in array)
		{
			string text2 = text.Trim();
			if (string.IsNullOrWhiteSpace(text2) || text2.StartsWith('#'))
			{
				continue;
			}
			int num = text2.IndexOf('=');
			if (num > 0)
			{
				string variable = text2.Substring(0, num).Trim();
				string text3 = text2.Substring(num + 1).Trim();
				if (text3.StartsWith('"') && text3.EndsWith('"'))
				{
					text3 = text3.Substring(1, text3.Length - 2);
				}
				else if (text3.StartsWith('\'') && text3.EndsWith('\''))
				{
					text3 = text3.Substring(1, text3.Length - 2);
				}
				if (Environment.GetEnvironmentVariable(variable) == null)
				{
					Environment.SetEnvironmentVariable(variable, text3);
				}
			}
		}
	}
}
