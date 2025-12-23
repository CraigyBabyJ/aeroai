using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.AtcSession;
using AeroAI.Models;
using Xunit;

namespace AeroAI.Tests;

public class AtcSessionEngineTests
{
    [Fact]
    public async Task IntentEngine_MatchesClearanceRequest()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var engine = new IntentEngine(packs!);
        var context = new AtcIntentContext("clearance", new List<string>(), new List<string>());
        var result = await engine.ClassifyAsync("Requesting IFR clearance runway 27", context);

        Assert.Equal("REQUEST_IFR_CLEARANCE", result.IntentId);
        Assert.True(result.Confidence >= 0.6);
    }

    [Fact]
    public async Task HandoffFlow_DeliveryToGround_CheckinCommitsPhase()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var manager = new AtcSessionManager(packs!, responseGenerator: null);
        var flight = new FlightContext
        {
            Callsign = "TEST 123",
            OriginIcao = "EGLL",
            DestinationIcao = "EGKK",
            CurrentAtcUnit = AtcUnit.ClearanceDelivery,
            CurrentPhase = FlightPhase.Preflight_Clearance,
            CruiseFlightLevel = 350
        };

        var first = await manager.TryHandleAsync("Cleared to EGKK as filed", flight);
        Assert.NotNull(first);
        Assert.NotNull(first!.State.PendingHandoff);
        Assert.Equal("ground", first.State.PendingHandoff!.TargetRole);
        Assert.Equal("clearance", first.State.CurrentPhase);
        Assert.Equal("delivery", first.State.ActiveControllerRole);

        var second = await manager.TryHandleAsync("Ground, TEST 123 with you", flight);
        Assert.NotNull(second);
        Assert.Equal("HANDOFF_CHECKIN", second!.Intent.IntentId);
        Assert.Null(second!.State.PendingHandoff);
        Assert.Equal("ground", second.State.CurrentPhase);
        Assert.Equal("ground", second.ControllerRole);
        Assert.Equal("ground", second.State.ActiveControllerRole);
    }

    [Fact]
    public async Task HandoffFlow_GroundToTower_CheckinCommitsPhase()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var manager = new AtcSessionManager(packs!, responseGenerator: null);
        var flight = new FlightContext
        {
            Callsign = "TEST 123",
            OriginIcao = "EGLL",
            DestinationIcao = "EGKK",
            CurrentAtcUnit = AtcUnit.Ground,
            CurrentPhase = FlightPhase.Taxi_Out,
            CruiseFlightLevel = 350
        };

        var first = await manager.TryHandleAsync("Ready for departure runway 27", flight);
        Assert.NotNull(first);
        Assert.NotNull(first!.State.PendingHandoff);
        Assert.Equal("tower", first.State.PendingHandoff!.TargetRole);
        Assert.Equal("ground", first.State.CurrentPhase);

        var second = await manager.TryHandleAsync("Tower, TEST 123 with you", flight);
        Assert.NotNull(second);
        Assert.Equal("HANDOFF_CHECKIN", second!.Intent.IntentId);
        Assert.Null(second!.State.PendingHandoff);
        Assert.Equal("tower", second.State.CurrentPhase);
        Assert.Equal("tower", second.State.ActiveControllerRole);
    }

    [Fact]
    public async Task HandoffFlow_DoesNotCommitOnRoleMismatch()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var manager = new AtcSessionManager(packs!, responseGenerator: null);
        var flight = new FlightContext
        {
            Callsign = "TEST 123",
            OriginIcao = "EGLL",
            DestinationIcao = "EGKK",
            CurrentAtcUnit = AtcUnit.Ground,
            CurrentPhase = FlightPhase.Taxi_Out,
            CruiseFlightLevel = 350
        };

        var first = await manager.TryHandleAsync("Ready for departure runway 27", flight);
        Assert.NotNull(first);
        Assert.NotNull(first!.State.PendingHandoff);
        Assert.Equal("tower", first.State.PendingHandoff!.TargetRole);

        var second = await manager.TryHandleAsync("Ground with you", flight);
        Assert.NotNull(second);
        Assert.Equal("HANDOFF_CHECKIN", second!.Intent.IntentId);
        Assert.NotNull(second!.State.PendingHandoff);
        Assert.Equal("ground", second.State.ActiveControllerRole);
        Assert.Equal("ground", second.State.CurrentPhase);
    }

    [Fact]
    public async Task HandoffFlow_PendingHandoffDoesNotChangeActiveUntilCheckin()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var manager = new AtcSessionManager(packs!, responseGenerator: null);
        var flight = new FlightContext
        {
            Callsign = "TEST 123",
            OriginIcao = "EGLL",
            DestinationIcao = "EGKK",
            CurrentAtcUnit = AtcUnit.ClearanceDelivery,
            CurrentPhase = FlightPhase.Preflight_Clearance,
            CurrentFrequency = "121.975",
            CruiseFlightLevel = 350
        };

        var first = await manager.TryHandleAsync("Cleared to EGKK as filed", flight);
        Assert.NotNull(first);
        Assert.NotNull(first!.State.PendingHandoff);
        Assert.Equal("delivery", first.State.ActiveControllerRole);
        Assert.Equal("clearance", first.State.CurrentPhase);
        Assert.Equal("121.975", first.State.ActiveFrequencyMhz);

        var pendingFrequency = first.State.PendingHandoff!.TargetFrequencyMhz;
        var second = await manager.TryHandleAsync("Ground, TEST 123 with you", flight);
        Assert.NotNull(second);
        Assert.Null(second!.State.PendingHandoff);
        Assert.Equal("ground", second.State.ActiveControllerRole);
        Assert.Equal("ground", second.State.CurrentPhase);
        if (!string.IsNullOrWhiteSpace(pendingFrequency))
        {
            Assert.Equal(pendingFrequency, second.State.ActiveFrequencyMhz);
        }
    }

    [Fact]
    public async Task HandoffFlow_CheckinWrongRole_DoesNotCommitPendingHandoff()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var manager = new AtcSessionManager(packs!, responseGenerator: null);
        var flight = new FlightContext
        {
            Callsign = "TEST 123",
            OriginIcao = "EGLL",
            DestinationIcao = "EGKK",
            CurrentAtcUnit = AtcUnit.Ground,
            CurrentPhase = FlightPhase.Taxi_Out,
            CurrentFrequency = "121.700",
            CruiseFlightLevel = 350
        };

        var first = await manager.TryHandleAsync("Ready for departure runway 27", flight);
        Assert.NotNull(first);
        Assert.NotNull(first!.State.PendingHandoff);
        Assert.Equal("tower", first.State.PendingHandoff!.TargetRole);
        Assert.Equal("ground", first.State.ActiveControllerRole);
        Assert.Equal("ground", first.State.CurrentPhase);
        Assert.Equal("121.700", first.State.ActiveFrequencyMhz);

        var second = await manager.TryHandleAsync("Ground, TEST 123 with you", flight);
        Assert.NotNull(second);
        Assert.NotNull(second!.State.PendingHandoff);
        Assert.Equal("ground", second.State.ActiveControllerRole);
        Assert.Equal("ground", second.State.CurrentPhase);
        Assert.Equal("121.700", second.State.ActiveFrequencyMhz);
    }

    [Fact]
    public void TemplateRenderer_AllowsMissingOptionalSlots()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var renderer = new AtcTemplateRenderer(packs!);
        var request = new AtcTemplateRequest("clearance", "HANDOFF_CHECKIN", "ACK_HANDOFF", null);
        var data = new Dictionary<string, string> { ["callsign"] = "TEST 123" };

        var result = renderer.TryRender(request, data);
        Assert.NotNull(result);
        Assert.DoesNotContain("{", result!.Text);
    }

    [Fact]
    public async Task OpenAiFallback_OnlyWhenBelowThreshold()
    {
        var packs = new AtcJsonPackLoader().TryLoadAll();
        Assert.NotNull(packs);

        var fallback = new StubIntentClassifier();
        var engine = new IntentEngine(packs!, fallback);
        var context = new AtcIntentContext("clearance", new List<string>(), new List<string>());
        var result = await engine.ClassifyAsync("Requesting IFR clearance runway 27", context);

        Assert.Equal("REQUEST_IFR_CLEARANCE", result.IntentId);
        Assert.False(fallback.WasCalled);
    }

    private sealed class StubIntentClassifier : IIntentClassifier
    {
        public bool WasCalled { get; private set; }

        public Task<AtcIntentResult?> ClassifyAsync(string transcript, AtcIntentContext context, CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult<AtcIntentResult?>(null);
        }
    }
}
