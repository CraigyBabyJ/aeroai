using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AeroAI.Examples;

public static class SimpleOpenAiTest
{
	public static async Task RunAsync()
	{
		string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
		if (string.IsNullOrWhiteSpace(apiKey))
		{
			Console.WriteLine("ERROR: OPENAI_API_KEY not set in environment variables.");
			Console.WriteLine("Please set it in your .env file or environment variables.");
			return;
		}
		Console.WriteLine("Testing OpenAI API connection...");
		Console.WriteLine("API Key: " + apiKey.Substring(0, Math.Min(10, apiKey.Length)) + "...");
		Console.WriteLine();
		using HttpClient client = new HttpClient
		{
			BaseAddress = new Uri("https://api.openai.com/v1/")
		};
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
		var payload = new
		{
			model = "gpt-4o-mini",
			messages = new[]
			{
				new
				{
					role = "system",
					content = "You are an ATC controller. Reply with one short transmission."
				},
				new
				{
					role = "user",
					content = "PILOT: Good evening clearance, this is CJ requesting IFR clearance to Casablanca as filed."
				}
			}
		};
		string json = JsonSerializer.Serialize(payload);
		StringContent content = new StringContent(json, Encoding.UTF8, "application/json");
		Console.WriteLine($"Request URL: {client.BaseAddress}chat/completions");
		Console.WriteLine("Payload model: " + payload.model);
		Console.WriteLine();
		try
		{
			HttpResponseMessage resp = await client.PostAsync("chat/completions", content);
			Console.WriteLine($"Status: {resp.StatusCode} {resp.StatusCode}");
			string body = await resp.Content.ReadAsStringAsync();
			if (resp.IsSuccessStatusCode)
			{
				Console.WriteLine("✓ SUCCESS! API connection is working.");
				Console.WriteLine();
				Console.WriteLine("Response body:");
				Console.WriteLine(body);
				return;
			}
			Console.WriteLine("✗ FAILED");
			Console.WriteLine();
			Console.WriteLine("Response body:");
			Console.WriteLine(body);
			Console.WriteLine();
			if (resp.StatusCode == HttpStatusCode.NotFound)
			{
				Console.WriteLine("DIAGNOSIS: 404 Not Found");
				Console.WriteLine("  - Check that URL is exactly: https://api.openai.com/v1/chat/completions");
				Console.WriteLine("  - Verify model name is correct: gpt-4o-mini");
				Console.WriteLine("  - Check for proxy/VPN interference");
			}
			else if (resp.StatusCode == HttpStatusCode.Unauthorized)
			{
				Console.WriteLine("DIAGNOSIS: 401 Unauthorized");
				Console.WriteLine("  - API key is invalid or missing");
				Console.WriteLine("  - Verify your API key at https://platform.openai.com/api-keys");
			}
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			Console.WriteLine("ERROR: " + ex2.GetType().Name + ": " + ex2.Message);
			if (ex2.InnerException != null)
			{
				Console.WriteLine("  Inner: " + ex2.InnerException.Message);
			}
		}
	}
}
