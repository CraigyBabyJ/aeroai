using Xunit;

namespace AeroAI.Atc;

public class ReadbackNormalizerTests
{
	[Fact]
	public void Normalizes_Icao_AsFiled_FlightLevel()
	{
		var ctx = new FlightContext { CruiseFlightLevel = 350 };
		var input = "Cleared to EG SS then is filed, climb 35,000 squawk 1416";
		var normalized = ReadbackNormalizer.Normalize(input, ctx);
		Assert.Contains("EGSS", normalized);
		Assert.Contains("then as filed", normalized);
		Assert.Contains("FL35", normalized);
		Assert.Contains("squawk 1416", normalized);
	}

	[Fact]
	public void Validator_MissingOnly_Squawk()
	{
		var flightCtx = new FlightContext
		{
			RadioCallsign = "TEST123",
			CruiseFlightLevel = 350
		};
		var atcCtx = new AtcContext
		{
			ClearanceDecision = new ClearanceDecision
			{
				ClearedTo = "EGSS",
				DepRunway = "24",
				Sid = "GOSAC1C",
				InitialAltitudeFt = 5000,
				Squawk = "1416",
				RouteSummary = "as filed",
				ClearanceType = "IFR_CLEARANCE"
			}
		};

		var readback = "Cleared to EGSS via GOSAC1C departure, runway 24, initial climb 5000 expect FL350";
		var eval = ReadbackValidator.Evaluate(readback, atcCtx, flightCtx);
		Assert.False(eval.Accepted);
		Assert.Contains("squawk", eval.Missing);
		Assert.DoesNotContain("runway", eval.Missing);
		Assert.DoesNotContain("initial altitude", eval.Missing);
		Assert.Empty(eval.Mismatched);
	}
}
