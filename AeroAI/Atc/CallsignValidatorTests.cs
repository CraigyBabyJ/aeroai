using Xunit;

namespace AeroAI.Atc;

public class CallsignValidatorTests
{
    private FlightContext BuildFlight()
    {
        return new FlightContext
        {
            Callsign = "ALASKA 113",
            RawCallsign = "ASA113",
            AirlineIcao = "ASA",
            FlightNumber = "113",
            AirlineName = "ALASKA",
            CanonicalCallsign = "ASA113",
            RadioCallsign = "ALASKA 113"
        };
    }

    [Fact]
    public void Initial_Request_Allows_Callsign_Anywhere()
    {
        var flight = BuildFlight();
        var text = "Good morning clearance, this is Alaska 113 requesting IFR.";

        Assert.True(CallsignValidator.IsPresent(text, flight, allowAnywhere: true));
    }

    [Fact]
    public void Readback_Without_Callsign_Is_Rejected()
    {
        var flight = BuildFlight();
        var text = "Cleared to Juneau, squawk 4406, runway 33.";

        Assert.False(CallsignValidator.IsPresent(text, flight, allowAnywhere: false));
    }
}
