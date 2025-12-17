using System;

namespace AeroAI.Atc;

/// <summary>
/// Minimal harness to sanity-check readback normalisation; run manually if needed.
/// </summary>
public static class ReadbackNormalizerHarness
{
	public static void Run()
	{
		var dummyContext = new FlightContext { CruiseFlightLevel = 350 };
		string[] samples =
		{
			"Cleared to EG SS then is filed squawk one four one six",
			"Flight level 350, runway two four",
			"Squawk one four one six",
			"as field via GOSAC one Charlie"
		};

		foreach (var s in samples)
		{
			var n = ReadbackNormalizer.Normalize(s, dummyContext);
			Console.WriteLine($"{s} -> {n}");
		}
	}
}
