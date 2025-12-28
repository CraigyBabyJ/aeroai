using AeroAI.Atc;
using Xunit;

namespace AeroAI.Tests;

public class TranscriptUsabilityCheckerTests
{
    [Theory]
    [InlineData("easy one two three radio check", true)]
    [InlineData("request clearance", true)]
    [InlineData("ready for departure", true)]
    [InlineData("Easy 123", true)] // Single callsign token
    [InlineData("BAW456", true)] // Single callsign token
    public void IsUsable_ValidTranscripts_ReturnsTrue(string transcript, bool expected)
    {
        var result = TranscriptUsabilityChecker.IsUsable(transcript);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")] // Too short
    [InlineData("uh")]
    [InlineData("um")]
    [InlineData("uh um")]
    [InlineData("test")]
    [InlineData("hello")]
    [InlineData("mic")]
    public void IsUsable_UnusableTranscripts_ReturnsFalse(string transcript)
    {
        var result = TranscriptUsabilityChecker.IsUsable(transcript);
        Assert.False(result);
    }

    [Fact]
    public void IsUsable_WithLowConfidence_ReturnsFalse()
    {
        var result = TranscriptUsabilityChecker.IsUsable("request clearance", sttConfidence: 0.3, minConfidence: 0.5);
        Assert.False(result);
    }

    [Fact]
    public void IsUsable_WithHighConfidence_ReturnsTrue()
    {
        var result = TranscriptUsabilityChecker.IsUsable("request clearance", sttConfidence: 0.9, minConfidence: 0.5);
        Assert.True(result);
    }

    [Fact]
    public void GetUnusableReason_Empty_ReturnsReason()
    {
        var reason = TranscriptUsabilityChecker.GetUnusableReason("");
        Assert.NotNull(reason);
        Assert.Contains("Empty", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUnusableReason_TooShort_ReturnsReason()
    {
        var reason = TranscriptUsabilityChecker.GetUnusableReason("ab");
        Assert.NotNull(reason);
        Assert.Contains("short", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUnusableReason_AllFiller_ReturnsReason()
    {
        var reason = TranscriptUsabilityChecker.GetUnusableReason("uh um");
        Assert.NotNull(reason);
        Assert.Contains("filler", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetUnusableReason_Usable_ReturnsNull()
    {
        var reason = TranscriptUsabilityChecker.GetUnusableReason("request clearance");
        Assert.Null(reason);
    }
}

