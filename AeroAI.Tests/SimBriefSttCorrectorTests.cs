using System;
using AeroAI.Atc;
using AeroAI.Services;
using Xunit;

namespace AeroAI.Tests;

public class SimBriefSttCorrectorTests
{
    private static FlightContext BuildContext()
    {
        return new FlightContext
        {
            OriginIcao = "EGLL",
            OriginName = "London Heathrow",
            DestinationIcao = "KJFK",
            DestinationName = "John F. Kennedy International"
        };
    }

    [Fact]
    public void CorrectsMisheardDepartureName()
    {
        var context = BuildContext();
        var input = "request pushback at heat throw";
        var output = SimBriefSttCorrector.Apply(input, context);

        Assert.Contains("Heathrow", output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CorrectsMisheardArrivalIata()
    {
        var context = BuildContext();
        var input = "confirm destination jay f k";
        var output = SimBriefSttCorrector.Apply(input, context);

        var hasJfk = output.Contains("JFK", StringComparison.OrdinalIgnoreCase)
                     || output.Contains("John F", StringComparison.OrdinalIgnoreCase);
        Assert.True(hasJfk);
    }

    [Fact]
    public void DoesNotRewriteUnrelatedAirport()
    {
        var context = BuildContext();
        var input = "diverting to gatwick";
        var output = SimBriefSttCorrector.Apply(input, context);

        Assert.Equal(input, output);
    }

    [Fact]
    public void NoSimBriefDataDoesNothing()
    {
        var input = "request pushback at heat throw";
        var output = SimBriefSttCorrector.Apply(input, null);

        Assert.Equal(input, output);
    }
}
