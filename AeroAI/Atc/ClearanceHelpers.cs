using System;
using System.Linq;

namespace AeroAI.Atc;

public static class ClearanceHelpers
{
	public static bool IsNonOperationalAck(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return true;
		}

		string t = text.ToLowerInvariant().Trim();
		string[] source = new string[]
		{
			"standby",
			"standby cj",
			"standby xj",
			"roger",
			"wilco",
			"copy",
			"copy that",
			"affirm",
			"affirmative",
			"ok",
			"okay"
		};

		return source.Any(p => t == p || t.Contains(p));
	}

	public static bool ClearanceDataComplete(AtcContext ctx)
	{
		// Check if we have enough information to issue a clearance.
		// Note: DepRunway is assigned by ATC in the clearance, not required from pilot beforehand.
		return ctx != null
			&& ctx.ClearanceDecision != null
			&& !string.IsNullOrWhiteSpace(ctx.ClearanceDecision.ClearedTo)
			&& ctx.ClearanceDecision.InitialAltitudeFt.HasValue
			&& !string.IsNullOrWhiteSpace(ctx.ClearanceDecision.Squawk);
		// DepRunway will be assigned by ATC when issuing the clearance based on weather/airport conditions.
	}

	private static void DebugLogClearanceCheck(string message)
	{
		string? env = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
		if (!string.IsNullOrWhiteSpace(env) &&
		    (env.Equals("1", StringComparison.OrdinalIgnoreCase) ||
		     env.Equals("true", StringComparison.OrdinalIgnoreCase) ||
		     env.Equals("yes", StringComparison.OrdinalIgnoreCase)))
		{
			Console.WriteLine("[DEBUG ClearanceCheck] " + message);
		}
	}

	public static bool IsIfrRequest(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string text2 = text.ToLowerInvariant();
		if (text2.Contains("request ifr") || text2.Contains("requesting ifr"))
		{
			return true;
		}

		bool hasRequest = text2.Contains("request") || text2.Contains("requesting");
		bool hasClearance = text2.Contains("clearance") || text2.Contains("clearence") || text2.Contains("clearan");
		if (hasRequest && hasClearance)
		{
			return true;
		}

		return false;
	}

	public static bool IsIfrClearanceRequest(string text) => IsIfrRequest(text);

	public static bool IsReadbackOnly(string text, AtcContext ctx)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		string text2 = text.ToLowerInvariant();
		string? clearedTo = ctx?.ClearanceDecision?.ClearedTo;
		if (!string.IsNullOrWhiteSpace(clearedTo))
		{
			string value = clearedTo.ToLowerInvariant();
			if (text2.Contains("cleared to") && text2.Contains(value))
			{
				return true;
			}
		}

		return false;
	}
}
