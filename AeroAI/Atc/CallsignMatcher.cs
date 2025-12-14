using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

public static class CallsignMatcher
{
	private static readonly Regex NonAlphanumeric = new("[^A-Z0-9]", RegexOptions.Compiled);

	public static bool IsRecognized(string pilotTransmission, FlightContext context)
	{
		var details = CallsignDetails.FromContext(context);
		if (!details.IsValid)
		{
			// No canonical SimBrief callsign to compare against; allow.
			return true;
		}

		var normalizedPilot = Normalize(pilotTransmission);
		if (string.IsNullOrWhiteSpace(normalizedPilot))
		{
			return true;
		}

		foreach (var variant in BuildVariants(details))
		{
			var normalizedVariant = Normalize(variant);
			if (!string.IsNullOrWhiteSpace(normalizedVariant) && normalizedPilot.Contains(normalizedVariant))
			{
				return true;
			}
		}

		return false;
	}

	public static IReadOnlyList<string> BuildVariants(CallsignDetails details)
	{
		var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (details == null)
		{
			return Array.Empty<string>();
		}

		AddIfAny(variants, details.Raw);

		if (!string.IsNullOrWhiteSpace(details.AirlineIcao) && !string.IsNullOrWhiteSpace(details.FlightNumber))
		{
			var canonical = details.AirlineIcao + details.FlightNumber;
			AddIfAny(variants, canonical);
			AddIfAny(variants, $"{details.AirlineIcao} {details.FlightNumber}");
		}

		var baseNames = GetAirlineNameVariants(details);
		foreach (var name in baseNames)
		{
			AddIfAny(variants, $"{name} {details.FlightNumber}");
			AddIfAny(variants, $"{name} {ToSpelledDigits(details.FlightNumber)}");
		}

		if (!string.IsNullOrWhiteSpace(details.RadioCallsign))
		{
			AddIfAny(variants, details.RadioCallsign);
		}

		return variants.ToList();
	}

	public static CallsignContextInfo BuildContextInfo(FlightContext context)
	{
		var details = CallsignDetails.FromContext(context);
		var canonical = string.IsNullOrWhiteSpace(details.AirlineIcao) || string.IsNullOrWhiteSpace(details.FlightNumber)
			? details.RadioCallsign
			: $"{details.AirlineIcao}{details.FlightNumber}";

		return new CallsignContextInfo
		{
			Canonical = canonical,
			Raw = details.Raw,
			AirlineIcao = details.AirlineIcao,
			FlightNumber = details.FlightNumber,
			AirlineRadioName = details.AirlineName,
			AirlineFullName = details.AirlineFullName,
			ExpectedVariants = BuildVariants(details)
		};
	}

	private static IEnumerable<string> GetAirlineNameVariants(CallsignDetails details)
	{
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		AddIfAny(names, details.AirlineName);
		AddIfAny(names, details.AirlineFullName);

		if (!string.IsNullOrWhiteSpace(details.AirlineFullName))
		{
			var words = details.AirlineFullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (words.Length > 1 && words[0].Equals("AIR", StringComparison.OrdinalIgnoreCase))
			{
				AddIfAny(names, string.Join(' ', words.Skip(1))); // Air New Zealand -> New Zealand
			}

			AddIfAny(names, words.FirstOrDefault()); // Cathay Pacific -> Cathay
		}

		return names;
	}

	private static string Normalize(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		var upper = value.ToUpperInvariant();
		return NonAlphanumeric.Replace(upper, string.Empty);
	}

	private static string ToSpelledDigits(string? digits)
	{
		if (string.IsNullOrWhiteSpace(digits))
		{
			return string.Empty;
		}

		var parts = new List<string>();
		foreach (var c in digits)
		{
			parts.Add(c switch
			{
				'0' => "ZERO",
				'1' => "ONE",
				'2' => "TWO",
				'3' => "THREE",
				'4' => "FOUR",
				'5' => "FIVE",
				'6' => "SIX",
				'7' => "SEVEN",
				'8' => "EIGHT",
				'9' => "NINE",
				_ => c.ToString()
			});
		}
		return string.Join(' ', parts);
	}

	private static void AddIfAny(ISet<string> set, string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			set.Add(value.Trim());
		}
	}
}
