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
		return ctx != null
			&& ctx.ClearanceDecision != null
			&& !string.IsNullOrWhiteSpace(ctx.ClearanceDecision.ClearedTo)
			&& !string.IsNullOrWhiteSpace(ctx.ClearanceDecision.DepRunway)
			&& ctx.ClearanceDecision.InitialAltitudeFt.HasValue
			&& !string.IsNullOrWhiteSpace(ctx.ClearanceDecision.Squawk);
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
