using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Models;
using Xunit;

namespace AeroAI.Tests;

/// <summary>
/// Tests for routing decision logic and LLM fallback behavior.
/// </summary>
public class RoutingDecisionTests
{
    [Fact]
    public async Task UsableNonProcedural_ShouldRouteToLlm()
    {
        // Arrange
        var spyGenerator = new SpyResponseGenerator();
        var context = new FlightContext
        {
            Callsign = "Easy 123",
            CanonicalCallsign = "Easy 123"
        };
        var session = new AeroAiLlmSession(spyGenerator, context);

        // Act: Send a usable, non-procedural transmission
        var response = await session.HandlePilotTransmissionAsync("request clearance", CancellationToken.None);

        // Assert: LLM should have been called
        Assert.True(spyGenerator.WasCalled, "LLM should be called for usable non-procedural transcripts");
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task UnusableTranscript_ShouldReturnSayAgain_WithoutCallingLlm()
    {
        // Arrange
        var spyGenerator = new SpyResponseGenerator();
        var context = new FlightContext();
        var session = new AeroAiLlmSession(spyGenerator, context);

        // Act: Send an unusable transcript (too short)
        var response = await session.HandlePilotTransmissionAsync("uh", CancellationToken.None);

        // Assert: LLM should NOT have been called, should return say again
        Assert.False(spyGenerator.WasCalled, "LLM should NOT be called for unusable transcripts");
        Assert.NotNull(response);
        Assert.Contains("say again", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyTranscript_ShouldReturnSayAgain_WithoutCallingLlm()
    {
        // Arrange
        var spyGenerator = new SpyResponseGenerator();
        var context = new FlightContext();
        var session = new AeroAiLlmSession(spyGenerator, context);

        // Act: Send empty transcript
        var response = await session.HandlePilotTransmissionAsync("", CancellationToken.None);

        // Assert: LLM should NOT have been called
        Assert.False(spyGenerator.WasCalled, "LLM should NOT be called for empty transcripts");
        Assert.NotNull(response);
        Assert.Contains("Say again", response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RadioCheck_ShouldBypassLlm()
    {
        // Arrange
        var spyGenerator = new SpyResponseGenerator();
        var context = new FlightContext
        {
            Callsign = "Easy 123",
            CanonicalCallsign = "Easy 123"
        };
        var session = new AeroAiLlmSession(spyGenerator, context);

        // Act: Send radio check
        var response = await session.HandlePilotTransmissionAsync("easy one two three radio check", CancellationToken.None);

        // Assert: LLM should NOT have been called
        Assert.False(spyGenerator.WasCalled, "LLM should be bypassed for radio checks");
        Assert.NotNull(response);
        Assert.Contains("loud and clear", response, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Spy implementation of IAtcResponseGenerator that tracks if GenerateAsync was called.
    /// </summary>
    private sealed class SpyResponseGenerator : IAtcResponseGenerator
    {
        public bool WasCalled { get; private set; }
        public int CallCount { get; private set; }

        public Task<AtcResponse> GenerateAsync(AtcRequest request, CancellationToken ct = default)
        {
            WasCalled = true;
            CallCount++;
            // Return a dummy response
            return Task.FromResult(new AtcResponse { SpokenText = "LLM response" });
        }
    }
}

