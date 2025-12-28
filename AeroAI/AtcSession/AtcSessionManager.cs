using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Config;
using AeroAI.Data;
using AeroAI.Llm;
using AeroAI.Models;

namespace AeroAI.AtcSession;

public sealed class AtcSessionManager
{
    private readonly AtcPackStore _packs;
    private readonly IntentEngine _intentEngine;
    private readonly AtcFlowEngine _flowEngine;
    private readonly AtcTemplateRenderer _templateRenderer;
    private readonly IAtcResponseGenerator? _responseGenerator;
    private readonly Action<string>? _onDebug;
    private readonly AtcSessionState _state = new();

    public static AtcSessionManager? TryCreate(IAtcResponseGenerator? responseGenerator, Action<string>? onDebug = null)
    {
        var loader = new AtcJsonPackLoader();
        var packs = loader.TryLoadAll(onDebug);
        if (packs == null)
        {
            return null;
        }

        IIntentClassifier? fallback = null;
        try
        {
            EnvironmentConfig.Load();
            var apiKey = EnvironmentConfig.GetOpenAiApiKey();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var model = EnvironmentConfig.GetOpenAiModel();
                var client = new OpenAiLlmClient(apiKey, model, EnvironmentConfig.GetOpenAiBaseUrl(), onDebug);
                fallback = new OpenAiIntentClassifier(client, packs);
            }
        }
        catch
        {
            fallback = null;
        }

        return new AtcSessionManager(packs, responseGenerator, onDebug, fallback);
    }

    public AtcSessionManager(
        AtcPackStore packs,
        IAtcResponseGenerator? responseGenerator,
        Action<string>? onDebug = null,
        IIntentClassifier? fallbackClassifier = null)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _responseGenerator = responseGenerator;
        _onDebug = onDebug;
        _intentEngine = new IntentEngine(packs, fallbackClassifier);
        _flowEngine = new AtcFlowEngine(packs);
        _templateRenderer = new AtcTemplateRenderer(packs);
    }

    public async Task<AtcSessionResult?> TryHandleAsync(string pilotTransmission, FlightContext flightContext, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pilotTransmission))
        {
            return null;
        }

        InitializeState(flightContext);

        var phaseId = _state.CurrentPhase;
        var expectedNext = _flowEngine.GetExpectedNextIntents(phaseId);
        _state.ExpectedNextIntents = expectedNext.ToArray();

        var allowed = _packs.PhaseById.TryGetValue(phaseId, out var phase)
            ? phase.AllowedIntents
            : new List<string>();

        var intentContext = new AtcIntentContext(phaseId, expectedNext, allowed);
        var intent = await _intentEngine.ClassifyAsync(pilotTransmission, intentContext, ct);

        var decision = _flowEngine.Resolve(_state, intent);
        var isHandoffCheckin = intent.IntentId.Equals("HANDOFF_CHECKIN", StringComparison.OrdinalIgnoreCase);
        var pendingBefore = _state.PendingHandoff;
        string? mentionedRole = null;
        string? mentionedFrequency = null;
        if (isHandoffCheckin)
        {
            if (TryExtractRoleMention(pilotTransmission, out var roleValue))
            {
                mentionedRole = roleValue;
            }
            if (TryExtractFrequencyMention(pilotTransmission, out var frequencyValue))
            {
                mentionedFrequency = frequencyValue;
            }
        }

        var templateData = BuildTemplateData(flightContext, intent, decision, _state);
        var preRole = ResolveActiveControllerRole();

        if (ShouldRejectHandoff(intent, pilotTransmission, pendingBefore))
        {
            var reissueDecision = decision with
            {
                CommitPendingHandoff = false,
                AtcAction = "REISSUE_HANDOFF"
            };
            var reissueRequest = new AtcTemplateRequest(
                _state.CurrentPhase,
                intent.IntentId,
                "REISSUE_HANDOFF",
                "handoff_reissue");
            var reissue = _templateRenderer.TryRender(reissueRequest, templateData);
            var text = reissue?.Text ?? BuildHandoffReissueFallback(templateData);
            ApplyState(reissueDecision, intent, text, flightContext, allowHandoffCommit: false);
            var responseRole = ResolveActiveControllerRole();
            if (isHandoffCheckin)
            {
                LogHandoffCheckinOutcome(
                    pendingBefore,
                    mentionedRole,
                    mentionedFrequency,
                    committed: false,
                    reasonOverride: "role_mismatch");
            }
            return new AtcSessionResult(text, responseRole, false, Array.Empty<string>(), _state, intent);
        }

        var templateRequest = new AtcTemplateRequest(
            phaseId,
            intent.IntentId,
            decision.AtcAction ?? intent.IntentId,
            decision.TemplateId);

        var rendered = _templateRenderer.TryRender(templateRequest, templateData);
        if (rendered != null)
        {
            ApplyState(decision, intent, rendered.Text, flightContext);
            var responseRole = ResolveActiveControllerRole();
            if (isHandoffCheckin)
            {
                var committed = decision.CommitPendingHandoff && pendingBefore != null;
                LogHandoffCheckinOutcome(pendingBefore, mentionedRole, mentionedFrequency, committed);
            }
            return new AtcSessionResult(rendered.Text, responseRole, rendered.RequiresReadback, rendered.ReadbackItems, _state, intent);
        }

        if (_responseGenerator == null)
        {
            if (isHandoffCheckin)
            {
                LogHandoffCheckinOutcome(pendingBefore, mentionedRole, mentionedFrequency, committed: false);
            }
            return null;
        }

        var atcContext = FlightContextToAtcContextMapper.Map(flightContext);
        var promptData = BuildPromptData(intent, decision, templateData, atcContext);
        var request = new AtcRequest
        {
            TranscriptText = pilotTransmission,
            ControllerRole = preRole,
            FlightContext = flightContext,
            AtcContext = atcContext,
            SessionState = promptData
        };

        var response = await _responseGenerator.GenerateAsync(request, ct);
        if (!string.IsNullOrWhiteSpace(response.SpokenText))
        {
            ApplyState(decision, intent, response.SpokenText, flightContext);
            var responseRole = ResolveActiveControllerRole();
            if (isHandoffCheckin)
            {
                var committed = decision.CommitPendingHandoff && pendingBefore != null;
                LogHandoffCheckinOutcome(pendingBefore, mentionedRole, mentionedFrequency, committed);
            }
            return new AtcSessionResult(response.SpokenText, responseRole, false, Array.Empty<string>(), _state, intent);
        }

        if (isHandoffCheckin)
        {
            LogHandoffCheckinOutcome(pendingBefore, mentionedRole, mentionedFrequency, committed: false);
        }
        return null;
    }

    private void InitializeState(FlightContext flightContext)
    {
        if (string.IsNullOrWhiteSpace(_state.SessionId))
        {
            _state.SessionId = !string.IsNullOrWhiteSpace(flightContext.RadioCallsign)
                ? flightContext.RadioCallsign
                : flightContext.Callsign ?? Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(_state.FacilityIcao))
        {
            _state.FacilityIcao = ResolveFacilityIcao(flightContext);
        }

        if (string.IsNullOrWhiteSpace(_state.ActiveControllerRole))
        {
            _state.ActiveControllerRole = ResolveRoleFromContext(flightContext);
        }

        if (string.IsNullOrWhiteSpace(_state.CurrentControllerRole))
        {
            _state.CurrentControllerRole = _state.ActiveControllerRole;
        }

        if (string.IsNullOrWhiteSpace(_state.ActiveFrequencyMhz) &&
            !string.IsNullOrWhiteSpace(flightContext.CurrentFrequency))
        {
            _state.ActiveFrequencyMhz = flightContext.CurrentFrequency;
        }

        if (string.IsNullOrWhiteSpace(_state.CurrentPhase))
        {
            var fallbackPhase = ResolvePhaseId(flightContext);
            _state.CurrentPhase = _flowEngine.GetPhaseForRole(_state.ActiveControllerRole, fallbackPhase);
        }

        if (string.IsNullOrWhiteSpace(_state.CurrentControllerRole))
        {
            _state.CurrentControllerRole = _flowEngine.GetDefaultControllerRole(_state.CurrentPhase);
            _state.ActiveControllerRole = _state.CurrentControllerRole;
        }
    }

    private void ApplyState(AtcFlowDecision decision, AtcIntentResult intent, string responseText, FlightContext flightContext, bool allowHandoffCommit = true)
    {
        _state.LastIntentId = intent.IntentId;
        _state.LastAtcAction = decision.AtcAction;
        _state.LastResponseText = responseText;
        _state.LastUpdatedUtc = DateTime.UtcNow;
        _state.FacilityIcao = ResolveFacilityIcao(flightContext);

        if (decision.SetPendingHandoff && decision.Handoff != null)
        {
            var handoffFrequency = ResolveHandoffFrequency(decision.Handoff, flightContext);
            _state.PendingHandoff = new PendingHandoff
            {
                TargetRole = decision.Handoff.Role,
                TargetFrequencyMhz = handoffFrequency,
                TargetFacilityIcao = _state.FacilityIcao,
                IssuedAtUtc = DateTime.UtcNow
            };
        }

        if (allowHandoffCommit && decision.CommitPendingHandoff && _state.PendingHandoff != null)
        {
            _state.ActiveControllerRole = _state.PendingHandoff.TargetRole;
            _state.ActiveFrequencyMhz = _state.PendingHandoff.TargetFrequencyMhz;
            _state.CurrentPhase = _flowEngine.GetPhaseForRole(_state.ActiveControllerRole, _state.CurrentPhase);
            _state.CurrentControllerRole = _state.ActiveControllerRole;
            if (!string.IsNullOrWhiteSpace(_state.PendingHandoff.TargetFacilityIcao))
            {
                _state.FacilityIcao = _state.PendingHandoff.TargetFacilityIcao!;
            }
            _state.PendingHandoff = null;
        }
        else if (!string.IsNullOrWhiteSpace(decision.NextPhaseId))
        {
            _state.CurrentPhase = decision.NextPhaseId;
            var defaultRole = _flowEngine.GetDefaultControllerRole(_state.CurrentPhase);
            _state.CurrentControllerRole = defaultRole;
            if (string.IsNullOrWhiteSpace(_state.ActiveControllerRole))
            {
                _state.ActiveControllerRole = defaultRole;
            }
        }

        var expectedNext = _flowEngine.GetExpectedNextIntents(_state.CurrentPhase).ToList();
        if (_state.PendingHandoff != null &&
            !expectedNext.Any(id => id.Equals("HANDOFF_CHECKIN", StringComparison.OrdinalIgnoreCase)))
        {
            expectedNext.Add("HANDOFF_CHECKIN");
        }
        _state.ExpectedNextIntents = expectedNext.ToArray();

        if (!string.IsNullOrWhiteSpace(_state.ActiveControllerRole))
        {
            _state.CurrentControllerRole = _state.ActiveControllerRole;
        }

        UpdateFlags(decision.AtcAction);
        ApplyFlightContextState(flightContext);
    }

    private void UpdateFlags(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return;
        }

        switch (action.ToUpperInvariant())
        {
            case "ISSUE_CLEARANCE":
                _state.ClearanceIssued = true;
                break;
            case "ISSUE_TAXI":
                _state.TaxiIssued = true;
                break;
            case "TAKEOFF_CLEARANCE":
                _state.TakeoffCleared = true;
                break;
        }
    }

    private void ApplyFlightContextState(FlightContext flightContext)
    {
        if (!TryMapPhase(_state.CurrentPhase, out var unit, out var phase))
        {
            return;
        }

        flightContext.CurrentAtcUnit = unit;
        flightContext.CurrentPhase = phase;

        if (!string.IsNullOrWhiteSpace(_state.ActiveFrequencyMhz))
        {
            flightContext.CurrentFrequency = _state.ActiveFrequencyMhz;
            return;
        }

        var freq = ClearanceUnitResolver.ResolveFrequency(flightContext.OriginIcao, unit);
        if (freq.HasValue)
        {
            flightContext.CurrentFrequency = freq.Value.ToString("F3");
        }
    }

    private AtcPromptData BuildPromptData(AtcIntentResult intent, AtcFlowDecision decision, IReadOnlyDictionary<string, string> templateData, AtcContext atcContext)
    {
        var templateInfo = new
        {
            phase = _state.CurrentPhase,
            intent = intent.IntentId,
            atc_action = decision.AtcAction,
            template_id = decision.TemplateId,
            data = templateData
        };

        var simBrief = new
        {
            dep_icao = atcContext.FlightInfo?.DepIcao,
            arr_icao = atcContext.FlightInfo?.ArrIcao,
            sid = atcContext.ClearanceDecision?.Sid,
            star = atcContext.ClearanceDecision?.Star,
            cruise = atcContext.FlightInfo?.CruiseLevel
        };

        return new AtcPromptData
        {
            SessionId = _state.SessionId,
            Phase = _state.CurrentPhase,
            ControllerRole = ResolveActiveControllerRole(),
            ActiveControllerRole = _state.ActiveControllerRole,
            ActiveFrequencyMhz = _state.ActiveFrequencyMhz,
            LastAtcAction = _state.LastAtcAction,
            PendingHandoff = _state.PendingHandoff,
            ExpectedNextIntents = _state.ExpectedNextIntents,
            ExtractedSlots = intent.ExtractedSlots,
            SimBrief = simBrief,
            AllowedActions = _flowEngine.GetAllowedActions(_state.CurrentPhase),
            Template = templateInfo
        };
    }

    private static IReadOnlyDictionary<string, string> BuildTemplateData(
        FlightContext flightContext,
        AtcIntentResult intent,
        AtcFlowDecision decision,
        AtcSessionState state)
    {
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var callsign = !string.IsNullOrWhiteSpace(flightContext.RadioCallsign)
            ? flightContext.RadioCallsign
            : flightContext.Callsign ?? "Aircraft";

        data["callsign"] = callsign;
        data["origin_icao"] = flightContext.OriginIcao ?? string.Empty;
        data["destination_icao"] = flightContext.DestinationIcao ?? string.Empty;
        data["destination_name"] = AirportNameResolver.ResolveAirportName(flightContext.DestinationIcao, flightContext);

        var runway = flightContext.DepartureRunway?.RunwayIdentifier
                     ?? flightContext.SelectedDepartureRunway
                     ?? "runway";
        data["runway"] = runway;
        data["departure_runway"] = runway;
        data["sid"] = flightContext.SelectedSID ?? "as filed";

        var initialClimb = flightContext.ClearedAltitude ?? (flightContext.CruiseFlightLevel > 300 ? 5000 : 3000);
        data["initial_climb"] = $"{initialClimb} feet";

        var cruise = flightContext.CruiseFlightLevel > 0 ? $"FL{flightContext.CruiseFlightLevel}" : "cruise level";
        data["cruise_level"] = cruise;

        var squawk = flightContext.SquawkCode ?? "XXXX";
        data["squawk"] = squawk;

        if (!string.IsNullOrWhiteSpace(flightContext.DepartureAtisLetter))
        {
            data["atis_letter"] = flightContext.DepartureAtisLetter!;
        }

        if (!string.IsNullOrWhiteSpace(flightContext.Aircraft?.IcaoType))
        {
            data["aircraft_type"] = flightContext.Aircraft!.IcaoType!;
        }

        data["taxi_route"] = "as directed";
        data["facility"] = !string.IsNullOrWhiteSpace(state.FacilityIcao)
            ? state.FacilityIcao
            : (flightContext.OriginIcao ?? string.Empty);

        if (decision.CommitPendingHandoff && state.PendingHandoff != null)
        {
            data["role"] = state.PendingHandoff.TargetRole;
        }
        else
        {
            data["role"] = !string.IsNullOrWhiteSpace(state.ActiveControllerRole)
                ? state.ActiveControllerRole
                : state.CurrentControllerRole;
        }

        foreach (var slot in intent.ExtractedSlots)
        {
            data[slot.Key] = slot.Value;
        }

        if (decision.Handoff != null)
        {
            data["handoff_role"] = decision.Handoff.Role;
            data["next_role"] = decision.Handoff.Role;
            var freq = decision.Handoff.FrequencyMhz;
            if (!freq.HasValue)
            {
                var unit = MapRoleToAtcUnit(decision.Handoff.Role);
                freq = ClearanceUnitResolver.ResolveFrequency(flightContext.OriginIcao, unit);
            }
            if (freq.HasValue)
            {
                var freqText = freq.Value.ToString("F3");
                data["handoff_frequency"] = freqText;
                data["next_freq"] = freqText;
            }
        }
        else if (state.PendingHandoff != null)
        {
            data["handoff_role"] = state.PendingHandoff.TargetRole;
            data["next_role"] = state.PendingHandoff.TargetRole;
            if (!string.IsNullOrWhiteSpace(state.PendingHandoff.TargetFrequencyMhz))
            {
                data["handoff_frequency"] = state.PendingHandoff.TargetFrequencyMhz!;
                data["next_freq"] = state.PendingHandoff.TargetFrequencyMhz!;
            }
            else
            {
                var unit = MapRoleToAtcUnit(state.PendingHandoff.TargetRole);
                var freq = ClearanceUnitResolver.ResolveFrequency(flightContext.OriginIcao, unit);
                if (freq.HasValue)
                {
                    var freqText = freq.Value.ToString("F3");
                    data["handoff_frequency"] = freqText;
                    data["next_freq"] = freqText;
                }
            }
        }

        return data;
    }

    private string ResolveActiveControllerRole()
    {
        if (!string.IsNullOrWhiteSpace(_state.ActiveControllerRole))
        {
            return _state.ActiveControllerRole;
        }

        if (!string.IsNullOrWhiteSpace(_state.CurrentControllerRole))
        {
            return _state.CurrentControllerRole;
        }

        return _flowEngine.GetDefaultControllerRole(_state.CurrentPhase);
    }

    private static string ResolveRoleFromContext(FlightContext flightContext)
    {
        return flightContext.CurrentAtcUnit switch
        {
            AtcUnit.ClearanceDelivery => "delivery",
            AtcUnit.Ground => "ground",
            AtcUnit.Tower => "tower",
            AtcUnit.Departure => "departure",
            AtcUnit.Center => "center",
            AtcUnit.Arrival => "approach",
            AtcUnit.Approach => "approach",
            _ => string.Empty
        };
    }

    private static bool ShouldRejectHandoff(AtcIntentResult intent, string pilotTransmission, PendingHandoff? pending)
    {
        if (pending == null)
        {
            return false;
        }

        if (!intent.IntentId.Equals("HANDOFF_CHECKIN", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryExtractRoleMention(pilotTransmission, out var mentionedRole))
        {
            return false;
        }

        var pendingRole = pending.TargetRole?.Trim() ?? string.Empty;
        var resolvedMention = mentionedRole.Trim();
        if (string.IsNullOrWhiteSpace(pendingRole))
        {
            return false;
        }

        return !resolvedMention.Equals(pendingRole, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractRoleMention(string pilotTransmission, out string role)
    {
        role = string.Empty;
        if (string.IsNullOrWhiteSpace(pilotTransmission))
        {
            return false;
        }

        var match = Regex.Match(
            pilotTransmission,
            @"\b(ground|tower|departure|center|approach|delivery|clearance)\b",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        role = match.Groups[1].Value.ToLowerInvariant();
        if (role == "clearance")
        {
            role = "delivery";
        }

        return true;
    }

    private static bool TryExtractFrequencyMention(string pilotTransmission, out string frequency)
    {
        frequency = string.Empty;
        if (string.IsNullOrWhiteSpace(pilotTransmission))
        {
            return false;
        }

        var match = Regex.Match(pilotTransmission, @"\b\d{3}\.\d{3}\b", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        frequency = match.Value;
        return true;
    }

    private void LogHandoffCheckinOutcome(
        PendingHandoff? pending,
        string? mentionedRole,
        string? mentionedFrequency,
        bool committed,
        string? reasonOverride = null)
    {
        if (_onDebug == null)
        {
            return;
        }

        var reason = reasonOverride ?? ResolveHandoffCheckinReason(pending, mentionedRole, mentionedFrequency, committed);
        var pendingRole = !string.IsNullOrWhiteSpace(pending?.TargetRole) ? pending!.TargetRole : "none";
        var pendingFrequency = !string.IsNullOrWhiteSpace(pending?.TargetFrequencyMhz) ? pending!.TargetFrequencyMhz : "none";
        var roleText = !string.IsNullOrWhiteSpace(mentionedRole) ? mentionedRole : "none";
        var freqText = !string.IsNullOrWhiteSpace(mentionedFrequency) ? mentionedFrequency : "none";
        var hasPending = pending != null ? "yes" : "no";

        _onDebug($"[ATC] HANDOFF_CHECKIN reason={reason} pending={hasPending} pending_role={pendingRole} pending_freq={pendingFrequency} mentioned_role={roleText} mentioned_freq={freqText}");
    }

    private static string ResolveHandoffCheckinReason(
        PendingHandoff? pending,
        string? mentionedRole,
        string? mentionedFrequency,
        bool committed)
    {
        if (committed)
        {
            return "ok";
        }

        if (pending == null)
        {
            return "no_pending";
        }

        if (!string.IsNullOrWhiteSpace(mentionedRole) &&
            !string.IsNullOrWhiteSpace(pending.TargetRole) &&
            !mentionedRole.Equals(pending.TargetRole, StringComparison.OrdinalIgnoreCase))
        {
            return "role_mismatch";
        }

        if (string.IsNullOrWhiteSpace(mentionedRole))
        {
            return "role_missing";
        }

        if (!string.IsNullOrWhiteSpace(mentionedFrequency) &&
            !string.IsNullOrWhiteSpace(pending.TargetFrequencyMhz) &&
            !string.Equals(mentionedFrequency, pending.TargetFrequencyMhz, StringComparison.OrdinalIgnoreCase))
        {
            return "freq_mismatch";
        }

        return "role_missing";
    }

    private static string BuildHandoffReissueFallback(IReadOnlyDictionary<string, string> templateData)
    {
        var callsign = templateData.TryGetValue("callsign", out var cs) ? cs : "Aircraft";
        var role = templateData.TryGetValue("next_role", out var roleValue) ? roleValue : "ground";
        if (!templateData.TryGetValue("next_freq", out var freq) || string.IsNullOrWhiteSpace(freq))
        {
            return $"{callsign}, contact {role}.";
        }

        return $"{callsign}, contact {role} on {freq}.";
    }

    private static string? ResolveHandoffFrequency(AtcHandoffSpec handoff, FlightContext flightContext)
    {
        if (handoff.FrequencyMhz.HasValue)
        {
            return handoff.FrequencyMhz.Value.ToString("F3");
        }

        var unit = MapRoleToAtcUnit(handoff.Role);
        var freq = ClearanceUnitResolver.ResolveFrequency(flightContext.OriginIcao, unit);
        return freq.HasValue ? freq.Value.ToString("F3") : null;
    }

    private static AtcUnit MapRoleToAtcUnit(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "delivery" => AtcUnit.ClearanceDelivery,
            "ground" => AtcUnit.Ground,
            "tower" => AtcUnit.Tower,
            "departure" => AtcUnit.Departure,
            "center" => AtcUnit.Center,
            "approach" => AtcUnit.Approach,
            _ => AtcUnit.ClearanceDelivery
        };
    }

    private static string ResolveFacilityIcao(FlightContext flightContext)
    {
        if (!string.IsNullOrWhiteSpace(flightContext.OriginIcao))
        {
            return flightContext.OriginIcao;
        }
        if (!string.IsNullOrWhiteSpace(flightContext.DestinationIcao))
        {
            return flightContext.DestinationIcao;
        }
        return string.Empty;
    }

    private static string ResolvePhaseId(FlightContext flightContext)
    {
        return flightContext.CurrentAtcUnit switch
        {
            AtcUnit.ClearanceDelivery => "clearance",
            AtcUnit.Ground => "ground",
            AtcUnit.Tower => "tower",
            AtcUnit.Departure => "departure",
            AtcUnit.Center => "center",
            AtcUnit.Arrival => "approach",
            AtcUnit.Approach => "approach",
            _ => "clearance"
        };
    }

    private static bool TryMapPhase(string phaseId, out AtcUnit unit, out FlightPhase phase)
    {
        switch (phaseId.ToLowerInvariant())
        {
            case "clearance":
                unit = AtcUnit.ClearanceDelivery;
                phase = FlightPhase.Preflight_Clearance;
                return true;
            case "ground":
                unit = AtcUnit.Ground;
                phase = FlightPhase.Taxi_Out;
                return true;
            case "tower":
                unit = AtcUnit.Tower;
                phase = FlightPhase.Lineup_Takeoff;
                return true;
            case "departure":
                unit = AtcUnit.Departure;
                phase = FlightPhase.Climb_Departure;
                return true;
            case "approach":
                unit = AtcUnit.Approach;
                phase = FlightPhase.Approach;
                return true;
            case "center":
                unit = AtcUnit.Center;
                phase = FlightPhase.Enroute;
                return true;
            default:
                unit = AtcUnit.ClearanceDelivery;
                phase = FlightPhase.Preflight_Clearance;
                return false;
        }
    }
}

public sealed record AtcSessionResult(
    string SpokenText,
    string ControllerRole,
    bool RequiresReadback,
    IReadOnlyList<string> ReadbackItems,
    AtcSessionState State,
    AtcIntentResult Intent);
