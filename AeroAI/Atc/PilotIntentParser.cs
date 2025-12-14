using System.Linq;
using System.Text.RegularExpressions;

namespace AeroAI.Atc;

public class PilotIntentParser
{
	public PilotIntent ParseIntent(string pilotText, FlightContext context)
	{
		if (string.IsNullOrWhiteSpace(pilotText))
		{
			return new PilotIntent
			{
				Type = IntentType.Unknown,
				RawText = pilotText
			};
		}
		string text = pilotText.Trim().ToUpperInvariant();
		PilotIntent pilotIntent = new PilotIntent
		{
			RawText = pilotText
		};
		if (Regex.IsMatch(text, "(REQUEST.*CLEARAN[CE]+|REQUESTING.*CLEARAN[CE]+|READY TO COPY|READY FOR CLEARAN[CE]+|IFR.*TO|CLEARAN[CE]+.*REQUEST)", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.RequestClearance;
			ExtractIcao(text, pilotIntent);
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "(REQUEST|REQUESTING).*(CLEARAN[CE]+|CLEAREN[CE]+)", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.RequestClearance;
			ExtractIcao(text, pilotIntent);
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(GOOD\\s+(MORNING|EVENING|AFTERNOON)|MORNING|EVENING|AFTERNOON|HELLO|HI)\\b.*CLEARANCE", RegexOptions.IgnoreCase) && !Regex.IsMatch(text, "(REQUEST|REQUESTING|READY)", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.CheckIn;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(CHECK|CHECKING|WITH YOU|RADIO CHECK)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.CheckIn;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(CLEARED TO|VIA|DEPARTURE RUNWAY|SQUAWK|INITIAL CLIMB)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.ReadbackClearance;
			ExtractSquawk(text, pilotIntent);
			ExtractAltitude(text, pilotIntent);
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(PUSH|PUSHBACK|START)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.RequestPush;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(READY TO TAXI|READY FOR TAXI|TAXI)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.ReadyToTaxi;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(READY|READY FOR DEPARTURE|HOLDING SHORT|LINE UP)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.ReadyForDeparture;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(CLEARED FOR TAKEOFF|TAKEOFF|DEPARTURE)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.AcknowledgeTakeoff;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(DEPARTURE|CONTACT|ON FREQUENCY)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.ContactDeparture;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(CLIMB|CLIMBING|FLIGHT LEVEL|MAINTAIN)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.ClimbAcknowledged;
			ExtractAltitude(text, pilotIntent);
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(ARRIVAL|APPROACH|INBOUND|DESCENDING)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.ContactArrival;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(RUNWAY IN SIGHT|VISUAL|HAVE THE RUNWAY)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.RunwayInSight;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(CLEARED TO LAND|LANDING|FINAL)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.AcknowledgeLanding;
			return pilotIntent;
		}
		if (Regex.IsMatch(text, "\\b(SHUTDOWN|SHUT DOWN|PARKED|AT STAND|PARKING)\\b", RegexOptions.IgnoreCase))
		{
			pilotIntent.Type = IntentType.RequestShutdown;
			return pilotIntent;
		}
		pilotIntent.Type = IntentType.Unknown;
		return pilotIntent;
	}

	private void ExtractIcao(string text, PilotIntent intent)
	{
		Match match = Regex.Match(text, "\\b(?:TO|DESTINATION|DEST)\\s+([A-Z]{4})\\b", RegexOptions.IgnoreCase);
		if (match.Success)
		{
			intent.Parameters["destination"] = match.Groups[1].Value.ToUpperInvariant();
			return;
		}
		Match match2 = Regex.Match(text, "\\b([A-Z]{4})\\b");
		if (match2.Success)
		{
			string value = match2.Groups[1].Value;
			if (!new string[7] { "THIS", "THAT", "WITH", "FROM", "CLEAR", "READY", "STAND" }.Contains(value))
			{
				intent.Parameters["destination"] = value;
			}
		}
	}

	private void ExtractSquawk(string text, PilotIntent intent)
	{
		Match match = Regex.Match(text, "SQUAWK\\s+(\\d{4})");
		if (match.Success)
		{
			intent.Parameters["squawk"] = match.Groups[1].Value;
		}
	}

	private void ExtractAltitude(string text, PilotIntent intent)
	{
		Match match = Regex.Match(text, "FL\\s*(\\d{2,3})");
		if (match.Success)
		{
			intent.Parameters["altitude"] = match.Groups[1].Value;
			return;
		}
		Match match2 = Regex.Match(text, "(\\d+)\\s*(FEET|FT)");
		if (match2.Success)
		{
			intent.Parameters["altitude"] = match2.Groups[1].Value;
		}
	}
}
