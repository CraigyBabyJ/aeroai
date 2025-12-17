using System.Collections.Generic;
using Xunit;

namespace AeroAI.Atc;

public class ReadbackValidatorTargetedTests
{
    private static AtcContext CreateContext()
    {
        return new AtcContext
        {
            ClearanceDecision = new ClearanceDecision
            {
                ClearedTo = "PAJN",
                Sid = "NOME",
                DepRunway = "33",
                InitialAltitudeFt = 5000,
                Squawk = "4406",
                ClearanceType = "IFR_CLEARANCE"
            },
            FlightInfo = new FlightInfo
            {
                CruiseLevel = "FL390"
            }
        };
    }

    private static FlightContext CreateFlight()
    {
        return new FlightContext
        {
            DestinationIcao = "PAJN",
            DestinationName = "Juneau International",
            CruiseFlightLevel = 390
        };
    }

    [Fact]
    public void Accepts_When_Pending_Destination_Only()
    {
        var ctx = CreateContext();
        var flight = CreateFlight();

        var eval = ReadbackValidator.Evaluate("Yes, destination Juneau International", ctx, flight, new[] { "destination" });

        Assert.True(eval.Accepted);
        Assert.Empty(eval.Missing);
        Assert.Empty(eval.Mismatched);
    }

    [Fact]
    public void Requests_Only_Pending_Slots()
    {
        var ctx = CreateContext();
        var flight = CreateFlight();
        var pending = new[] { "destination", "SID" };

        var eval = ReadbackValidator.Evaluate("Destination Juneau International", ctx, flight, pending);

        Assert.False(eval.Accepted);
        Assert.Contains("SID", eval.Missing);
        Assert.DoesNotContain("destination", eval.Missing); // satisfied
        Assert.True(eval.Missing.Count == 1);
    }

    [Fact]
    public void Full_Readback_Required_When_Multiple_Critical_Missing()
    {
        var ctx = CreateContext();
        var flight = CreateFlight();

        var eval = ReadbackValidator.Evaluate("Runway three three", ctx, flight);

        Assert.False(eval.Accepted);
        Assert.Contains("initial altitude", eval.Missing);
        Assert.Contains("squawk", eval.Missing);
        Assert.True(eval.Missing.Count >= 2);
    }
}
