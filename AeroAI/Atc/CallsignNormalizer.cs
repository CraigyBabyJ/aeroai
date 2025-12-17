using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

public static class CallsignNormalizer
{
	private const string DigitToken = "(?:\\d+|zero|one|two|three|four|five|six|seven|eight|nine|niner|oh|o)";

	public static string Normalize(string text, FlightContext context)
	{
		if (string.IsNullOrWhiteSpace(text) || context == null)
			return text;

		var airlineIcao = context.AirlineIcao?.Trim();
		var radioName = context.AirlineName?.Trim();

		if (string.IsNullOrWhiteSpace(radioName) && !string.IsNullOrWhiteSpace(context.Callsign))
		{
			// Heuristic: take the first non-numeric token from the radio callsign if airline name is missing.
			var first = context.Callsign.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
			if (!string.IsNullOrWhiteSpace(first) && !Regex.IsMatch(first, "^\\d+$"))
				radioName = first;
		}

		if (string.IsNullOrWhiteSpace(airlineIcao) && string.IsNullOrWhiteSpace(radioName))
			return text;

		var prefixVariants = new List<string>();
		if (!string.IsNullOrWhiteSpace(airlineIcao))
			prefixVariants.Add(Regex.Escape(airlineIcao.ToUpperInvariant()));
		if (!string.IsNullOrWhiteSpace(radioName))
			prefixVariants.Add(Regex.Escape(radioName.ToUpperInvariant()).Replace("\\ ", "\\s*"));

		if (prefixVariants.Count == 0)
			return text;

		var pattern = $"\\b(?:{string.Join("|", prefixVariants)})\\s+(?<num>{DigitToken})(?:\\s+(?<num>{DigitToken})){0,3}\\b";
		var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

		return regex.Replace(text, match =>
		{
			var nums = match.Groups["num"].Captures.Select(c => c.Value).ToList();
			if (nums.Count == 0)
				return match.Value;

			var digitStr = string.Concat(nums.Select(ToDigitChar));
			if (string.IsNullOrWhiteSpace(digitStr))
				return match.Value;

			var prefix = !string.IsNullOrWhiteSpace(radioName) ? radioName : airlineIcao;
			if (string.IsNullOrWhiteSpace(prefix))
				return match.Value;

			return $"{prefix} {digitStr}";
		});
	}

	private static string ToDigitChar(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
			return string.Empty;

		var t = token.Trim().ToLowerInvariant();
		return t switch
		{
			"zero" => "0",
			"oh" or "o" => "0",
			"one" => "1",
			"two" => "2",
			"three" => "3",
			"four" => "4",
			"five" => "5",
			"six" => "6",
			"seven" => "7",
			"eight" => "8",
			"nine" => "9",
			"niner" => "9",
			_ => int.TryParse(t, out _) ? t : string.Empty
		};
	}
}
