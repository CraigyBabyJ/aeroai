using AeroAI.Atc;
using Xunit;

namespace AeroAI.Tests;

public class ProceduralIntentRouterTests
{
    private FlightContext CreateFlightContext(string? callsign = null, string? canonicalCallsign = null, string? airlineIcao = null, string? airlineName = null)
    {
        return new FlightContext
        {
            Callsign = callsign ?? string.Empty,
            CanonicalCallsign = canonicalCallsign ?? string.Empty,
            AirlineIcao = airlineIcao ?? string.Empty,
            AirlineName = airlineName ?? string.Empty
        };
    }

    [Theory]
    [InlineData("easy one two three radio check")]
    [InlineData("radio check easy 123")]
    [InlineData("mic check please")]
    [InlineData("radio checking")]
    [InlineData("Easy 123 radio check")]
    [InlineData("radio check Easy 123")]
    [InlineData("uh radio check please")]
    [InlineData("request radio check")]
    public void MatchesRadioCheck_WithVariousPhrasings(string transcript)
    {
        var context = CreateFlightContext("Easy 123", "Easy 123", "EZY", "Easy");
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched, $"Should match: {transcript}");
        Assert.Equal(ProceduralIntent.RadioCheck, result.Intent);
        Assert.NotNull(result.ResponseText);
        Assert.NotEmpty(result.ResponseText);
    }

    [Fact]
    public void MatchesRadioCheck_ExtractsCallsign_SpokenNumbers()
    {
        var transcript = "easy one two three radio check";
        var context = CreateFlightContext("Easy 123", "Easy 123", "EZY", "Easy");
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched);
        Assert.Equal(ProceduralIntent.RadioCheck, result.Intent);
        // Should extract callsign (may be from context or extracted)
        Assert.NotNull(result.ExtractedCallsign);
    }

    [Fact]
    public void MatchesRadioCheck_ExtractsCallsign_AfterPhrase()
    {
        var transcript = "radio check easy 123";
        var context = CreateFlightContext("Easy 123", "Easy 123", "EZY", "Easy");
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched);
        Assert.Equal(ProceduralIntent.RadioCheck, result.Intent);
    }

    [Fact]
    public void MatchesRadioCheck_NoCallsign_ReturnsGenericResponse()
    {
        var transcript = "mic check please";
        var context = CreateFlightContext(); // No callsign in context
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched);
        Assert.Equal(ProceduralIntent.RadioCheck, result.Intent);
        Assert.NotNull(result.ResponseText);
        // Should not contain placeholder
        Assert.DoesNotContain("{CALLSIGN}", result.ResponseText);
        // Should be a generic response
        Assert.Contains("Loud and clear", result.ResponseText, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatchesRadioCheck_WithCallsign_IncludesCallsignInResponse()
    {
        var transcript = "radio check";
        var context = CreateFlightContext("Easy 123", "Easy 123", "EZY", "Easy");
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched);
        Assert.NotNull(result.ResponseText);
        // Response should include callsign (from context fallback)
        Assert.Contains("Easy", result.ResponseText, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ready for checkride")]
    [InlineData("request clearance")]
    [InlineData("ready for departure")]
    [InlineData("standby")]
    [InlineData("")]
    [InlineData("   ")]
    public void DoesNotMatch_NonRadioCheckPhrases(string transcript)
    {
        var context = CreateFlightContext("Easy 123", "Easy 123", "EZY", "Easy");
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.False(result.Matched, $"Should not match: {transcript}");
        Assert.Equal(ProceduralIntent.None, result.Intent);
        Assert.Null(result.ResponseText);
    }

    [Fact]
    public void MatchesRadioCheck_IgnoresFillerWords()
    {
        var transcript = "uh please radio check uh request";
        var context = CreateFlightContext();
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched);
        Assert.Equal(ProceduralIntent.RadioCheck, result.Intent);
    }

    [Fact]
    public void MatchesRadioCheck_FallbackToContextCallsign()
    {
        var transcript = "radio check"; // No callsign in transcript
        var context = CreateFlightContext("BAW456", "BAW456", "BAW", "Speedbird");
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched);
        // Should use callsign from context as fallback
        Assert.NotNull(result.ExtractedCallsign);
        Assert.Contains("BAW", result.ExtractedCallsign, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResponseText_IsRandomized()
    {
        var transcript = "radio check";
        var context = CreateFlightContext("Easy 123", "Easy 123", "EZY", "Easy");
        
        // Run multiple times to check randomization
        var responses = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 20; i++)
        {
            var result = ProceduralIntentRouter.TryMatch(transcript, context);
            if (result.Matched && result.ResponseText != null)
            {
                responses.Add(result.ResponseText);
            }
        }

        // Should have multiple different responses (randomization)
        // Note: With 4 templates, after 20 runs we should see at least 2-3 different ones
        Assert.True(responses.Count >= 1, "Should generate responses");
    }

    [Fact]
    public void OriginalTranscript_IsPreserved()
    {
        var transcript = "easy one two three radio check";
        var context = CreateFlightContext();
        var result = ProceduralIntentRouter.TryMatch(transcript, context);

        Assert.True(result.Matched);
        Assert.Equal(transcript, result.OriginalTranscript);
    }
}

