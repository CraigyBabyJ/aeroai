using System.Text.RegularExpressions;

namespace AeroAI.Atc;

/// <summary>
/// Cleans common STT quirks in readbacks into aviation-friendly formats before validation/LLM.
/// </summary>
public static class ReadbackNormalizer
{
	private static readonly Regex SplitIcao4 = new("\\b([A-Z])\\s+([A-Z])\\s+([A-Z])\\s+([A-Z])\\b", RegexOptions.Compiled);
	private static readonly Regex AsFiledTypo = new("\\b(then\\s+is\\s+filed|then\\s+its\\s+filed|as\\s+field|is\\s+filed)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex AltitudeThousands = new("\\b(?<num>\\d{2,3})\\s*[ ,]?0{3}\\b", RegexOptions.Compiled);
	private static readonly Regex FlightLevelNumber = new("\\bflight\\s+level\\s+(?<fl>\\d{2,3})\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex SquawkChunk = new("\\bsquawk\\s+(?<code>[A-Za-z0-9\\s-]{3,20})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
	private static readonly Regex RunwaySpoken = new("\\brunway\\s+(?<val>[A-Za-z0-9\\s]{1,8})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	public static string Normalize(string text, FlightContext context)
	{
		if (string.IsNullOrWhiteSpace(text))
			return text;

		var result = text;

		// EG SS -> EGSS, E G S S -> EGSS (generic 4-letter ICAO spaced)
		result = SplitIcao4.Replace(result, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value}{m.Groups[4].Value}");

		// then is filed -> then as filed
		result = AsFiledTypo.Replace(result, "then as filed");

		// flight level 350 -> FL350
		result = FlightLevelNumber.Replace(result, m => $"FL{m.Groups["fl"].Value}");

		// 35,000 / 35 000 / 35000 -> FL350 for altitudes between 10,000 and 45,000
		result = AltitudeThousands.Replace(result, m =>
		{
			if (!int.TryParse(m.Groups["num"].Value, out var thousands))
				return m.Value;

			var altitude = thousands * 1000;
			if (altitude < 10000 || altitude > 45000)
				return m.Value; // leave out of band

			return $"FL{thousands}";
		});

		// Runway spoken -> numeric (limited, conservative)
		result = RunwaySpoken.Replace(result, m =>
		{
			var raw = m.Groups["val"].Value.Trim();
			var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
				return m.Value;

			var digits = new List<char>();
			char? side = null;
			foreach (var p in parts)
			{
				if (p.Length == 1 && char.IsLetter(p[0]) && char.ToUpperInvariant(p[0]) is 'L' or 'R' or 'C')
				{
					side = char.ToUpperInvariant(p[0]);
					continue;
				}

				var d = DigitFromToken(p);
				if (d is char dc)
				{
					digits.Add(dc);
					if (digits.Count == 2)
						break;
				}
			}

			if (digits.Count == 0)
				return m.Value;

			var number = new string(digits.ToArray());
			var suffix = side.HasValue ? side.Value.ToString() : string.Empty;
			return $"runway {number}{suffix}";
		});

		// Squawk 4 digits, tolerate spaced digits/words
		result = SquawkChunk.Replace(result, m =>
		{
			var codeText = m.Groups["code"].Value;
			var tokens = codeText.Split(new[] { ' ', '-', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
			var digits = new List<char>();
			foreach (var t in tokens)
			{
				var d = DigitFromToken(t);
				if (d is char dc)
					digits.Add(dc);
				if (digits.Count == 4)
					break;
			}

			if (digits.Count == 4)
			{
				var code = new string(digits.ToArray());
				return $"squawk {code}";
			}

			return m.Value;
		});

		return result;
	}

	private static char? DigitFromToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
			return null;

		if (token.Length == 1 && char.IsDigit(token[0]))
			return token[0];

		var t = token.Trim().ToLowerInvariant();
		return t switch
		{
			"zero" => '0',
			"oh" or "o" => '0',
			"one" => '1',
			"two" => '2',
			"three" => '3',
			"four" => '4',
			"five" => '5',
			"six" => '6',
			"seven" => '7',
			"eight" => '8',
			"nine" => '9',
			"niner" => '9',
			_ => null
		};
	}
}
