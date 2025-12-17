#if DEBUG
using System.Diagnostics;

namespace AeroAI.Atc;

internal static class ReadbackRunwayGateHarness
{
    public static void Run()
    {
        var atc = new AtcContext
        {
            ClearanceDecision = new ClearanceDecision
            {
                DepRunway = "33",
                Squawk = "4406",
                InitialAltitudeFt = 5000,
                ClearanceType = "IFR_CLEARANCE",
                ClearedTo = "PAJN"
            }
        };

        var flight = new FlightContext { CruiseFlightLevel = 390, DestinationIcao = "PAJN" };

        // ATC did NOT issue runway -> runway should not be required/missing.
        var eval1 = ReadbackValidator.Evaluate("squawk 4406 initial climb 5000", atc, flight, issuedAtcText: "Cleared to Juneau, squawk 4406, initial climb 5000");
        Debug.Assert(!eval1.Missing.Contains("runway"), "Runway should not be required unless issued.");

        // ATC DID issue runway -> runway should be required/missing if absent.
        var eval2 = ReadbackValidator.Evaluate("squawk 4406 initial climb 5000", atc, flight, issuedAtcText: "Departure runway 33, squawk 4406, initial climb 5000");
        Debug.Assert(eval2.Missing.Contains("runway"), "Runway should be required once issued.");
    }
}
#endif
