using Xunit;

namespace AeroAI.Atc;

public class DestinationResolverTests
{
    private FlightContext BuildFlight()
    {
        return new FlightContext
        {
            DestinationIcao = "PAJN",
            DestinationName = "Juneau International"
        };
    }

    [Theory]
    [InlineData("Affirm")]
    [InlineData("yes")]
    [InlineData("confirmed")]
    public void Accepts_Affirmations(string input)
    {
        Assert.True(DestinationResolver.Matches(input, BuildFlight()));
    }

    [Theory]
    [InlineData("Confirmed destination Juneau")]
    [InlineData("Destination June International")]
    [InlineData("Yes, destination PAJN")]
    public void Matches_Fuzzy_Destination(string input)
    {
        Assert.True(DestinationResolver.Matches(input, BuildFlight()));
    }

    [Fact]
    public void Rejects_Wrong_Destination()
    {
        Assert.False(DestinationResolver.Matches("Confirm destination Anchorage", BuildFlight()));
    }
}
