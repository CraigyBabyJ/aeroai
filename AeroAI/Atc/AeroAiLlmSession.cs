using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using AeroAI.Config;
using AeroAI.Models;
using AeroAI.Data;
using AeroAI.AtcSession;

namespace AeroAI.Atc;

public class AeroAiLlmSession : IDisposable
{
	private readonly IAtcResponseGenerator _responseGenerator;

	private readonly FlightContext _context;

	private readonly PilotIntentParser _intentParser;
	private readonly AtcSession.AtcSessionManager? _sessionManager;
	private readonly AtcPackStore? _packs;
	private readonly AtcTemplateRenderer? _templateRenderer;

	private AtcState _state = AtcState.Idle;

	private bool _ifrClearanceIssued = false;

	private string? _lastAtcResponse;

	private AtcContext? _lastContext;
	private PendingReadbackRequest? _pendingReadback;
	private PendingConfirmation? _pendingConfirmation;
	private bool _atisConfirmed = false;
	private ClearanceRequestInfo _clearanceRequestInfo = new ClearanceRequestInfo();

	internal bool HasPendingConfirmation => _pendingConfirmation != null;

	private bool _disposed = false;

	private ClearanceRoutingResult ResolveClearanceRouting()
	{
		var routing = ClearanceUnitResolver.ResolveForClearance(_context.OriginIcao);
		_context.CurrentAtcUnit = routing.Unit;
		_context.CurrentFrequency = routing.FrequencyMhz?.ToString("F3");
		_context.NoAtcAvailable = !routing.HasAtc;
		return routing;
	}

	private void LogDebug(string message)
	{
		var logApi = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
		if (!string.IsNullOrWhiteSpace(logApi) &&
		    (logApi.Equals("1", StringComparison.OrdinalIgnoreCase) ||
		     logApi.Equals("true", StringComparison.OrdinalIgnoreCase) ||
		     logApi.Equals("yes", StringComparison.OrdinalIgnoreCase)))
		{
			Console.WriteLine("[DEBUG] " + message);
		}
	}

        private readonly Action<string>? _onDebug;
        private static readonly RoutingMetrics _routingMetrics = new RoutingMetrics();

        public AeroAiLlmSession(IAtcResponseGenerator responseGenerator, FlightContext context, Action<string>? onDebug = null)
        {
                _context = context ?? throw new ArgumentNullException("context");
                _responseGenerator = responseGenerator ?? throw new ArgumentNullException(nameof(responseGenerator));
                _intentParser = new PilotIntentParser();
                _sessionManager = AtcSession.AtcSessionManager.TryCreate(_responseGenerator, onDebug);
		var packLoader = new AtcJsonPackLoader();
		_packs = packLoader.TryLoadAll(onDebug);
		if (_packs != null)
		{
			_templateRenderer = new AtcTemplateRenderer(_packs);
		}
                _onDebug = onDebug;
        }

        /// <summary>
        /// Gets the current routing metrics snapshot.
        /// </summary>
        public static RoutingMetricsSnapshot GetRoutingMetrics() => _routingMetrics.GetSnapshot();

	public async Task<string?> HandlePilotTransmissionAsync(string pilotTransmission, CancellationToken cancellationToken = default(CancellationToken))
	{
		var rawTranscript = pilotTransmission;
		double? sttConfidence = null; // TODO: Extract from STT service if available

		// Update metrics
		_routingMetrics.IncrementTotalTranscripts();

		// Early exits for non-operational acks
		if (ClearanceHelpers.IsNonOperationalAck(pilotTransmission))
		{
			return null;
		}

		// Check usability BEFORE normalization (for logging purposes)
		var isUsableBeforeNorm = TranscriptUsabilityChecker.IsUsable(pilotTransmission, sttConfidence);
		var unusableReasonBeforeNorm = TranscriptUsabilityChecker.GetUnusableReason(pilotTransmission, sttConfidence);

		if (string.IsNullOrWhiteSpace(pilotTransmission))
		{
			_routingMetrics.IncrementUnusableTranscripts();
			_routingMetrics.IncrementSayAgain();
			var emptyResolvedContext = ResolvedContextBuilder.Build(_context);
			LogRoutingDecision(rawTranscript, pilotTransmission, sttConfidence, null, "SayAgain", 
				"Empty or whitespace only", null, false, "Empty or whitespace only", emptyResolvedContext);
			return "Say again?";
		}

		// CRITICAL: Extract and persist ATIS acknowledgement BEFORE normalization
		// This ensures ATIS state is captured even if normalization changes the text
		ExtractAndPersistAtisAcknowledgement(pilotTransmission);

		// Extract and persist stand/gate BEFORE normalization
		ExtractAndPersistStandGate(pilotTransmission);

		// Apply centralized normalization pipeline (STT corrections, deterministic fixes, callsign/readback normalization)
		// This must happen BEFORE any routing logic to ensure consistent processing
		var normalizedTranscript = PilotTransmissionNormalizer.Normalize(pilotTransmission, _context, enableDebugLogging: true);

		// Re-check ATIS after normalization (in case normalization improved the text)
		ExtractAndPersistAtisAcknowledgement(normalizedTranscript);

		// Re-check stand/gate after normalization
                ExtractAndPersistStandGate(normalizedTranscript);

		// Check usability AFTER normalization
		var isUsable = TranscriptUsabilityChecker.IsUsable(normalizedTranscript, sttConfidence);
		var unusableReason = TranscriptUsabilityChecker.GetUnusableReason(normalizedTranscript, sttConfidence);

		// Build resolved context from FlightContext (authoritative SimBrief data)
		var resolvedContext = ResolvedContextBuilder.Build(_context);

		// PROCEDURAL INTENT ROUTING: Check for procedural intents (e.g., radio checks) BEFORE any LLM routing
		// These must bypass the LLM entirely and use hard-coded responses
		var proceduralResult = ProceduralIntentRouter.TryMatch(normalizedTranscript, _context, _onDebug, resolvedContext);
		if (proceduralResult.Matched && !string.IsNullOrWhiteSpace(proceduralResult.ResponseText))
		{
			_routingMetrics.IncrementProceduralHits();
			// Apply output guardrails to procedural responses as well (in case callsign needs scrubbing)
			var scrubbedResponse = OutputGuard.ScrubOutput(proceduralResult.ResponseText, resolvedContext, _onDebug);
			_lastAtcResponse = scrubbedResponse;
			LogRoutingDecision(rawTranscript, normalizedTranscript, sttConfidence, proceduralResult.Intent, 
				"Procedural", "Matched procedural intent", proceduralResult.ExtractedCallsign, isUsable, null, resolvedContext);
			return scrubbedResponse;
		}

		if (_sessionManager != null)
		{
			var sessionResult = await _sessionManager.TryHandleAsync(normalizedTranscript, _context, cancellationToken);
			if (sessionResult != null && !string.IsNullOrWhiteSpace(sessionResult.SpokenText))
			{
				_routingMetrics.IncrementLlmCalls(); // Session manager may use LLM internally
				// Apply output guardrails to session manager response
				var scrubbed = OutputGuard.ScrubOutput(sessionResult.SpokenText, resolvedContext, _onDebug);
				_lastAtcResponse = scrubbed;
				LogRoutingDecision(rawTranscript, normalizedTranscript, sttConfidence, null, "SessionManager", 
					"Session manager handled", null, isUsable, null, resolvedContext);
				return scrubbed;
			}
		}

		// STRICT ROUTING PRIORITY (in order):
		// 1. Pending readback -> handle readback ONLY (no callsign gate, no other routing)
		if (_pendingReadback != null)
		{
			// Readback handling is done in RouteToPhaseHandlerAsync, but we skip callsign checks here
			var readbackIntent = _intentParser.ParseIntent(normalizedTranscript, _context);
			var readbackContext = _lastContext ?? FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, readbackIntent);
			var readbackResolvedContext = ResolvedContextBuilder.Build(_context);
			var readbackResult = await RouteToPhaseHandlerAsync(readbackContext, normalizedTranscript, readbackIntent, cancellationToken, readbackResolvedContext);
			if (!string.IsNullOrWhiteSpace(readbackResult))
			{
				// Apply output guardrails
				var scrubbed = OutputGuard.ScrubOutput(readbackResult, readbackResolvedContext, _onDebug);
				return scrubbed;
			}
			// If readback handling returned null/empty, fall through to LLM fallback below
		}

		// 2. Pending destination confirmation -> handle destination confirmation ONLY (no callsign gate, no radio check override)
		if (_pendingConfirmation?.Slot == ConfirmationSlot.Destination)
		{
			var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
			var plannedName = AirportNameResolver.ResolveAirportName(_context.DestinationIcao, _context);

			if (DestinationResolver.Matches(pilotTransmission, _context))
			{
				var pending = _pendingConfirmation;
				_pendingConfirmation = null;

				// If this confirmation was blocking a clearance request, continue with clearance issuance.
				if (pending?.OriginalIntent?.Type == IntentType.RequestClearance || pending?.OriginalIntent?.Type == IntentType.CheckIn)
				{
					var intent = pending.OriginalIntent ?? _intentParser.ParseIntent(pending?.OriginalText ?? pilotTransmission, _context);
					var clearanceContext = FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, intent, hideDestination: false);
					var res = await HandleClearanceAsync(clearanceContext, pending?.OriginalText ?? pilotTransmission, cancellationToken, pilotIntent: intent);
					if (!string.IsNullOrWhiteSpace(res))
						return res;
				}

				// If we were waiting on readback, acknowledge and include next instruction/tail.
				if (_pendingReadback != null)
				{
					// Remove the confirmed slot from pending list.
					if (_pendingReadback.Slots != null)
					{
						_pendingReadback.Slots.RemoveWhere(s => s.Equals("destination", StringComparison.OrdinalIgnoreCase));
					}

					if (_pendingReadback.Slots != null && _pendingReadback.Slots.Count > 0)
					{
						var remaining = string.Join(", ", _pendingReadback.Slots);
						return $"{cs}, confirm {remaining}.";
					}

					var tail = BuildReadbackAcknowledgementTail(_pendingReadback.Context);
					_pendingReadback = null;
					if (!string.IsNullOrWhiteSpace(tail))
						return $"{cs}, readback correct. {tail}";
					return $"{cs}, readback correct.";
				}

				// If destination was the only pending slot, treat clearance as complete.
				if (_state == AtcState.ClearanceIssued || _context.CurrentAtcState == AtcState.ClearanceIssued)
				{
					var intent = pending?.OriginalIntent ?? _intentParser.ParseIntent(pending?.OriginalText ?? pilotTransmission, _context);
					var ctx = _lastContext ?? FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, intent, hideDestination: false);
					var tail = BuildReadbackAcknowledgementTail(ctx);
					if (!string.IsNullOrWhiteSpace(tail))
						return $"{cs}, readback correct. {tail}";
					return $"{cs}, readback correct.";
				}

				return $"{cs}, destination confirmed.";
			}

			// Pilot did not match the filed destination: check if they mentioned a different destination
			var mentionedDest = ExtractDestinationMention(pilotTransmission, null);
			if (mentionedDest.HasDestination && !DestinationMatchesFlightPlan(mentionedDest))
			{
				// Pilot insists on different destination: refuse deterministically
				return $"Unable. Your flight plan shows {plannedName}. Update flight plan and advise.";
			}

			// Pilot reply is unrelated (radio check, random words, etc.): keep pending and ask again
			var lower = pilotTransmission.ToLowerInvariant();
			if (IsRadioCheckRequest(lower) || !IsRelevantToDestinationConfirmation(pilotTransmission))
			{
				return $"Unable. Confirm destination.";
			}

			// Still pending, ask again
			return $"{cs}, confirm destination {plannedName}.";
		}

		// 3. Pending ATIS confirmation -> handle ATIS confirmation ONLY (no callsign gate, no radio check override)
		if (_pendingConfirmation?.Slot == ConfirmationSlot.Atis)
		{
			var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
			if (IsAtisConfirmation(pilotTransmission) || HasAtisInTransmission(pilotTransmission))
			{
				var pending = _pendingConfirmation;
				_pendingConfirmation = null;
				_atisConfirmed = true;
				_clearanceRequestInfo.AtisAcknowledged = true;
				TryUpdateDepartureAtisLetter(pilotTransmission);

				// If strict training mode, check for remaining slots before issuing clearance
				if (TrainingConfig.StrictClearanceData)
				{
					var nextSlot = GetNextMissingSlot();
					if (nextSlot.HasValue)
					{
						_state = AtcState.ClearanceCollectingTrainingData;
						return BuildTrainingSlotPrompt(nextSlot.Value);
					}
				}

				// If this confirmation was blocking a clearance request, continue with clearance issuance.
				if (pending?.OriginalIntent?.Type == IntentType.RequestClearance || pending?.OriginalIntent?.Type == IntentType.CheckIn)
				{
					var intent = pending.OriginalIntent ?? _intentParser.ParseIntent(pending?.OriginalText ?? pilotTransmission, _context);
					var clearanceContext = FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, intent, hideDestination: false);
					var res = await HandleClearanceAsync(clearanceContext, pending?.OriginalText ?? pilotTransmission, cancellationToken, pilotIntent: intent);
					if (!string.IsNullOrWhiteSpace(res))
						return res;
				}

				return $"{cs}, information confirmed.";
			}

			// Pilot did not confirm ATIS: keep pending.
			// Radio check or unrelated responses during ATIS confirmation should re-prompt
			var lowerAtis = pilotTransmission.ToLowerInvariant();
			if (IsRadioCheckRequest(lowerAtis) || !IsRelevantToAtisConfirmation(pilotTransmission))
			{
				return $"Unable. Confirm you have the latest information.";
			}
			return $"{cs}, confirm you have the latest information.";
		}

		// 4. Radio check handling (only if no pending states) - REMOVED: handled by ProceduralIntentRouter above
		// This section is now redundant since ProceduralIntentRouter handles radio checks

		// 5. Callsign gating (ONLY if callsign is NOT already known - session-sticky)
		// Once callsign is set, NEVER ask again in this session
		// This check uses FlightContext (session memory), NOT "present in this single transmission"
		var callsignKnown = !string.IsNullOrWhiteSpace(_context.Callsign) || !string.IsNullOrWhiteSpace(_context.CanonicalCallsign);
		
		if (callsignKnown)
		{
			// Callsign is already known - mark as known and continue (no validation needed)
			_clearanceRequestInfo.CallsignKnown = true;
			LogDebug($"[CALLSIGN] Session-sticky: callsign already known as '{_context.CanonicalCallsign ?? _context.Callsign}'");
		}
		else
		{
			// Callsign is unknown - check if pilot provided it in this transmission
			var callsignFromTransmission = CallsignMatcher.ExtractCallsign(normalizedTranscript, _context);
			if (string.IsNullOrWhiteSpace(callsignFromTransmission))
			{
				LogDebug("[CALLSIGN] Unknown and not provided in transmission");
				// Check if transcript is usable before deciding what to do
				if (!isUsable)
				{
					// Unusable transcript - return say again (will not route to LLM)
					_routingMetrics.IncrementUnusableTranscripts();
					_routingMetrics.IncrementSayAgain();
					LogRoutingDecision(rawTranscript, normalizedTranscript, sttConfidence, null, "SayAgain", 
						$"Unusable transcript: {unusableReason}", null, false, unusableReason, resolvedContext);
					var cs = "Aircraft";
					return $"{cs}, say again.";
				}
				// Usable transcript but no callsign extracted - allow it to fall through to LLM
				// The LLM can handle requests like "radio check" even without explicit callsign
				// We'll use the resolved context callsign (from SimBrief) if available
				LogDebug("[CALLSIGN] Usable transcript without extracted callsign - allowing LLM fallback (will use SimBrief callsign if available)");
			}
			else
			{
				// Callsign found - update context (will be persisted by intent parser or elsewhere)
				_clearanceRequestInfo.CallsignKnown = true;
				LogDebug($"[CALLSIGN] Extracted from transmission: '{callsignFromTransmission}'");
			}
		}

		PilotIntent pilotIntent = _intentParser.ParseIntent(normalizedTranscript, _context);

		// Top-down routing for clearance delivery: Delivery -> Ground -> Tower -> Approach -> Center -> UNICOM.
		if (pilotIntent.Type == IntentType.RequestClearance)
		{
			var routing = ResolveClearanceRouting();
			if (routing.IsUnicom)
			{
				return "No ATC is online. Use UNICOM one two two decimal eight and self-announce your intentions.";
			}
		}

		// Cross-check pilot-stated destination against flight plan (accept either ICAO or spoken airport name).
		var destinationMention = ExtractDestinationMention(normalizedTranscript, pilotIntent);
		
		// If destination was mentioned and FlightContext doesn't have one, try to resolve and set it
		// (This handles cases where flight plan might not be loaded yet)
		if (destinationMention.HasDestination && string.IsNullOrWhiteSpace(_context.DestinationIcao))
		{
			string? resolvedIcao = null;
			string? resolvedName = null;
			
			// If we have an ICAO code, use it
			if (!string.IsNullOrWhiteSpace(destinationMention.Icao))
			{
				resolvedIcao = destinationMention.Icao.ToUpperInvariant();
				resolvedName = AirportNameResolver.ResolveAirportName(resolvedIcao, _context);
			}
			// If we have a name, try to resolve it to ICAO
			else if (!string.IsNullOrWhiteSpace(destinationMention.Name))
			{
				resolvedIcao = AirportNameResolver.ResolveIcaoFromName(destinationMention.Name, _context);
				resolvedName = destinationMention.Name;
			}
			
			// If we successfully resolved a destination, set it
			if (!string.IsNullOrWhiteSpace(resolvedIcao))
			{
				_context.DestinationIcao = resolvedIcao;
				if (!string.IsNullOrWhiteSpace(resolvedName))
					_context.DestinationName = resolvedName;
				LogDebug($"[Destination] Resolved '{destinationMention.Name ?? destinationMention.Icao}' to ICAO '{resolvedIcao}', name '{resolvedName}'");
			}
		}
		
		if (destinationMention.HasDestination && !DestinationMatchesFlightPlan(destinationMention))
		{
			var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
			var plannedName = !string.IsNullOrWhiteSpace(_context.DestinationName) ? _context.DestinationName : _context.DestinationIcao;
			// For initial clearance requests, collect the mismatch and continue so we can prompt for all missing slots together.
			if (pilotIntent.Type == IntentType.RequestClearance)
			{
				pilotIntent.Parameters["__dest_mismatch"] = "true";
			}
			else
			{
				_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Destination, pilotTransmission, pilotIntent);
				if (_pendingReadback == null)
				{
					var ctx = FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, pilotIntent, hideDestination: false);
					_pendingReadback = new PendingReadbackRequest(ctx, new HashSet<string>(new[] { "destination" }, StringComparer.OrdinalIgnoreCase), _lastAtcResponse);
				}
				else if (_pendingReadback.Slots == null)
				{
					_pendingReadback = new PendingReadbackRequest(_pendingReadback.Context, new HashSet<string>(new[] { "destination" }, StringComparer.OrdinalIgnoreCase), _pendingReadback.IssuedAtcText ?? _lastAtcResponse);
				}
				else
				{
					_pendingReadback.Slots.Add("destination");
				}
				LogDebug($"[DestMismatch] Pilot said ICAO='{destinationMention.Icao ?? "none"}', name='{destinationMention.Name ?? "none"}'; flight plan has '{_context.DestinationIcao}' / '{_context.DestinationName}'.");
				return $"{cs}, flight plan shows destination {plannedName}, confirm destination.";
			}
		}

		UpdateStateFromPilotTransmission(normalizedTranscript);
		bool maskDestination = ShouldMaskDestination(normalizedTranscript);
		AtcContext atcContext = FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, pilotIntent, maskDestination);
		PhaseDefaults.ApplyPhaseDefaults(_context.CurrentPhase, atcContext);

		// If a readback is expected, handle it before other routing.
			if (_pendingReadback != null)
			{
				var eval = ReadbackValidator.Evaluate(normalizedTranscript, _pendingReadback.Context, _context, _pendingReadback.Slots, issuedAtcText: _pendingReadback.IssuedAtcText);
				LogDebug($"[Readback] Pending slots: {( _pendingReadback.Slots?.Count > 0 ? string.Join(", ", _pendingReadback.Slots) : "all")} ; accepted={eval.Accepted}, missing=[{string.Join(", ", eval.Missing)}], mismatched=[{string.Join(", ", eval.Mismatched)}]");
				if (eval.Accepted)
				{
					_pendingReadback = null;
					var cs = eval.Callsign ?? (!string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft"));
					var tail = BuildReadbackAcknowledgementTail(atcContext);
					return string.IsNullOrWhiteSpace(tail)
						? $"{cs}, readback correct."
						: $"{cs}, readback correct. {tail}";
				}
				if (eval.Mismatched.Count > 0 || eval.Missing.Count > 0)
				{
					var cs = eval.Callsign ?? (!string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft"));
					var items = eval.Mismatched.Concat(eval.Missing).Distinct().ToList();
					if (items.Count > 0)
					{
						_pendingReadback = new PendingReadbackRequest(_pendingReadback.Context, new HashSet<string>(items, StringComparer.OrdinalIgnoreCase), _pendingReadback.IssuedAtcText);
						return $"{cs}, confirm {string.Join(", ", items)}.";
					}
				}
			// If not accepted and no specific items, fall through to normal flow.
		}

		// Tolerant readback validation for issued clearances to avoid unnecessary LLM churn.
		if (pilotIntent.Type == IntentType.ReadbackClearance
			&& (_state == AtcState.ClearanceIssued || _ifrClearanceIssued || atcContext.ClearanceDecision.ClearanceType == "IFR_CLEARANCE"))
		{
			var eval = ReadbackValidator.Evaluate(normalizedTranscript, atcContext, _context, issuedAtcText: _lastAtcResponse);
			var callsign = !string.IsNullOrWhiteSpace(_context.RadioCallsign)
				? _context.RadioCallsign
				: (_context.Callsign ?? "Aircraft");

			if (eval.Accepted)
			{
				var tail = BuildReadbackAcknowledgementTail(atcContext);
				if (string.IsNullOrWhiteSpace(tail))
				{
					return $"{callsign}, readback correct.";
				}
				return $"{callsign}, readback correct. {tail}";
			}
			else if (eval.Mismatched.Count > 0 || eval.Missing.Count > 0)
			{
				var items = eval.Mismatched.Concat(eval.Missing).Distinct().ToList();
				if (items.Count > 0)
				{
					_pendingReadback = new PendingReadbackRequest(atcContext, new HashSet<string>(items, StringComparer.OrdinalIgnoreCase), _lastAtcResponse);
					return $"{callsign}, confirm {string.Join(", ", items)}.";
				}
			}
		}

		var result = await RouteToPhaseHandlerAsync(atcContext, normalizedTranscript, pilotIntent, cancellationToken, resolvedContext);
		
		// If RouteToPhaseHandlerAsync returned null or empty, OR if it returned "say again", check if we should route to LLM
		var isSayAgain = !string.IsNullOrWhiteSpace(result) && 
			(result.Contains("say again", StringComparison.OrdinalIgnoreCase) || 
			 result.Contains("Say again", StringComparison.OrdinalIgnoreCase));
		
		if (string.IsNullOrWhiteSpace(result) || isSayAgain)
		{
			// Check if transcript is usable - if so, route to LLM as fallback
			// CRITICAL: This ensures usable transcripts always get a chance with the LLM
			if (isUsable)
			{
				LogDebug($"[Routing] RouteToPhaseHandlerAsync returned {(string.IsNullOrWhiteSpace(result) ? "null/empty" : "'say again'")} for usable transcript - routing to LLM: '{normalizedTranscript}'");
				_routingMetrics.IncrementLlmCalls();
				try
				{
					var llmResult = await CallLlmAsync(atcContext, normalizedTranscript, cancellationToken, resolvedContext);
					LogRoutingDecision(rawTranscript, normalizedTranscript, sttConfidence, null, "LLM", 
						isSayAgain ? "Route returned 'say again' → LLM fallback" : "No match → LLM fallback", 
						null, isUsable, null, resolvedContext);
					return llmResult;
				}
				catch (Exception ex)
				{
					_routingMetrics.IncrementLlmFailures();
					RoutingDecisionLogger.LogLlmFailure(normalizedTranscript, $"LLM call failed: {ex.Message}", ex, _onDebug);
					_routingMetrics.IncrementSayAgain();
					LogRoutingDecision(rawTranscript, normalizedTranscript, sttConfidence, null, "SayAgain", 
						$"LLM failed: {ex.GetType().Name}", null, isUsable, null, resolvedContext);
					var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
					
                    // Proactive Error Reporting: Include the error in the response so the user knows why it failed.
                    return $"{cs}, say again. (System Error: {ex.Message})";
				}
			}
			else
			{
				// Unusable transcript - return say again
				LogDebug($"[Routing] RouteToPhaseHandlerAsync returned {(string.IsNullOrWhiteSpace(result) ? "null/empty" : "'say again'")} for unusable transcript - returning 'say again': '{normalizedTranscript}', reason: {unusableReason}");
				_routingMetrics.IncrementUnusableTranscripts();
				_routingMetrics.IncrementSayAgain();
				LogRoutingDecision(rawTranscript, normalizedTranscript, sttConfidence, null, "SayAgain", 
					$"Unusable transcript: {unusableReason}", null, false, unusableReason, resolvedContext);
				var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
				return $"{cs}, say again.";
			}
		}

		// Result was generated - log as LLM route (since RouteToPhaseHandlerAsync calls CallLlmAsync internally)
		// Apply output guardrails to result
		var scrubbedResult = OutputGuard.ScrubOutput(result, resolvedContext, _onDebug);
		LogRoutingDecision(rawTranscript, normalizedTranscript, sttConfidence, null, "LLM", 
			"RouteToPhaseHandlerAsync → LLM", null, isUsable, null, resolvedContext);
		return scrubbedResult;
	}

	private void LogRoutingDecision(string rawTranscript, string normalizedTranscript, double? sttConfidence, 
		ProceduralIntent? matchedIntent, string routeTaken, string reason, string? extractedCallsign, 
		bool isUsable, string? unusableReason, ResolvedContext? resolvedContext = null)
	{
		var decision = new RoutingDecision
		{
			RawTranscript = rawTranscript,
			NormalizedTranscript = normalizedTranscript,
			SttConfidence = sttConfidence,
			MatchedIntent = matchedIntent,
			RouteTaken = routeTaken,
			Reason = reason,
			ExtractedCallsign = extractedCallsign,
			IsUsable = isUsable,
			UnusableReason = unusableReason,
			SimbriefCallsign = resolvedContext?.CallsignRaw,
			SpokenCallsign = resolvedContext?.CallsignSpoken,
			DepIcao = resolvedContext?.DepartureIcao,
			ArrIcao = resolvedContext?.ArrivalIcao,
			DepSpoken = resolvedContext?.DepartureSpoken,
			ArrSpoken = resolvedContext?.ArrivalSpoken,
			DepSource = resolvedContext?.DepartureSource,
			ArrSource = resolvedContext?.ArrivalSource
		};

		RoutingDecisionLogger.LogDecision(decision, _onDebug, _routingMetrics);
	}

	/// <summary>
	/// Extract and persist ATIS acknowledgement from pilot transmission.
	/// This runs BEFORE normalization to ensure ATIS phrases are captured even if normalization changes them.
	/// Also runs AFTER normalization as a safety check.
	/// </summary>
	private void ExtractAndPersistAtisAcknowledgement(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return;

		// If already confirmed, don't re-process (persistence guarantee)
		if (_atisConfirmed)
			return;

		var lower = pilotTransmission.ToLowerInvariant();

		// Pattern 1: "information <letter>" or "with information <letter>"
		// Accepts: "information alpha", "information alfa", "with information bravo", etc.
		var infoMatch = Regex.Match(
			pilotTransmission,
			@"\b(?:WITH\s+)?INFORMATION\s+([A-Z]|ALFA|ALPHA|BRAVO|CHARLIE|DELTA|ECHO|FOXTROT|GOLF|HOTEL|INDIA|JULIETT|JULIET|KILO|LIMA|MIKE|NOVEMBER|OSCAR|PAPA|QUEBEC|ROMEO|SIERRA|TANGO|UNIFORM|VICTOR|WHISKEY|WHISKY|X[-\s]?RAY|YANKEE|ZULU)\b",
			RegexOptions.IgnoreCase);
		if (infoMatch.Success)
		{
			var token = infoMatch.Groups[1].Value;
			var letter = TryResolveAtisLetterToken(token);
			if (letter != null)
			{
				_context.DepartureAtisLetter = letter;
				AtisMetarCache.SetAtisLetter(_context.OriginIcao, letter);
				_atisConfirmed = true;
				_clearanceRequestInfo.AtisAcknowledged = true;
				LogDebug($"[ATIS] Extracted letter '{letter}' from transmission");
				return;
			}
		}

		// Pattern 2: "ATIS <letter>" (e.g., "ATIS alpha", "ATIS bravo")
		var atisMatch = Regex.Match(
			pilotTransmission,
			@"\bATIS\s+([A-Z]|ALFA|ALPHA|BRAVO|CHARLIE|DELTA|ECHO|FOXTROT|GOLF|HOTEL|INDIA|JULIETT|JULIET|KILO|LIMA|MIKE|NOVEMBER|OSCAR|PAPA|QUEBEC|ROMEO|SIERRA|TANGO|UNIFORM|VICTOR|WHISKEY|WHISKY|X[-\s]?RAY|YANKEE|ZULU)\b",
			RegexOptions.IgnoreCase);
		if (atisMatch.Success)
		{
			var token = atisMatch.Groups[1].Value;
			var letter = TryResolveAtisLetterToken(token);
			if (letter != null)
			{
				_context.DepartureAtisLetter = letter;
				AtisMetarCache.SetAtisLetter(_context.OriginIcao, letter);
				_atisConfirmed = true;
				_clearanceRequestInfo.AtisAcknowledged = true;
				LogDebug($"[ATIS] Extracted letter '{letter}' from ATIS pattern");
				return;
			}
		}

		// Pattern 3: "we have (the) latest information" or "we have information" (no letter required)
		if (lower.Contains("we have") && (lower.Contains("latest") || lower.Contains("information") || lower.Contains("atis")))
		{
			_atisConfirmed = true;
			_clearanceRequestInfo.AtisAcknowledged = true;
			LogDebug("[ATIS] Confirmed via 'we have latest information' pattern");
			return;
		}

		// Pattern 4: "information received" (no letter required)
		if (lower.Contains("information received") || lower.Contains("info received"))
		{
			_atisConfirmed = true;
			_clearanceRequestInfo.AtisAcknowledged = true;
			LogDebug("[ATIS] Confirmed via 'information received' pattern");
			return;
		}

		// Pattern 5: "have information" or "have ATIS" (standalone)
		if ((lower.Contains("have information") || lower.Contains("have atis")) && 
		    !lower.Contains("we have")) // Avoid double-matching "we have information"
		{
			_atisConfirmed = true;
			_clearanceRequestInfo.AtisAcknowledged = true;
			LogDebug("[ATIS] Confirmed via 'have information' pattern");
			return;
		}
	}

	/// <summary>
	/// Legacy method - now calls ExtractAndPersistAtisAcknowledgement for backward compatibility.
	/// </summary>
	private void TryUpdateDepartureAtisLetter(string pilotTransmission)
	{
		ExtractAndPersistAtisAcknowledgement(pilotTransmission);
	}

	/// <summary>
	/// Legacy method - now calls ExtractAndPersistAtisAcknowledgement for backward compatibility.
	/// </summary>
	private void CheckAtisConfirmation(string pilotTransmission)
	{
		ExtractAndPersistAtisAcknowledgement(pilotTransmission);
	}

	/// <summary>
	/// Extract and persist stand/gate from pilot transmission.
	/// Runs before normalization and after to ensure capture.
	/// </summary>
	private void ExtractAndPersistStandGate(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return;

		// If already collected, don't re-process (persistence guarantee)
		if (_clearanceRequestInfo.StandGateCollected && !string.IsNullOrWhiteSpace(_context.Stand))
			return;

		var (found, value) = ExtractStandGate(pilotTransmission);
		if (found && !string.IsNullOrWhiteSpace(value))
		{
			_context.Stand = value;
			_clearanceRequestInfo.StandGateCollected = true;
			_clearanceRequestInfo.StandGateValue = value;
			LogDebug($"[STAND] Extracted stand/gate '{value}' from transmission");
		}
	}

	private static bool IsAtisConfirmation(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		var lower = pilotTransmission.ToLowerInvariant();
		// Accept various forms of ATIS confirmation.
		return lower.Contains("we have") && (lower.Contains("latest") || lower.Contains("information") || lower.Contains("atis"))
			|| lower.Contains("have information")
			|| lower.Contains("have atis")
			|| lower.Contains("information received")
			|| lower.Contains("with information")
			|| Regex.IsMatch(lower, @"\binformation\s+[a-z]\b", RegexOptions.IgnoreCase); // "information A", "information B", etc.
	}

	/// <summary>
	/// Check if pilot transmission contains any ATIS-related content (letter or acknowledgement).
	/// </summary>
	private static bool HasAtisInTransmission(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		// Check for ATIS letter pattern: "with information X" or "information X"
		if (Regex.IsMatch(pilotTransmission, @"\b(?:WITH\s+)?INFORMATION\s+([A-Z]|ALFA|ALPHA|BRAVO|CHARLIE|DELTA|ECHO|FOXTROT|GOLF|HOTEL|INDIA|JULIETT|JULIET|KILO|LIMA|MIKE|NOVEMBER|OSCAR|PAPA|QUEBEC|ROMEO|SIERRA|TANGO|UNIFORM|VICTOR|WHISKEY|WHISKY|X[-\s]?RAY|YANKEE|ZULU)\b", RegexOptions.IgnoreCase))
			return true;

		// Check for ATIS acknowledgement phrases
		return IsAtisConfirmation(pilotTransmission);
	}

	private static string ToAtisPhonetic(string atisLetter)
	{
		if (string.IsNullOrWhiteSpace(atisLetter))
			return "---";

		var c = char.ToUpperInvariant(atisLetter.Trim()[0]);
		return c switch
		{
			'A' => "Alfa",
			'B' => "Bravo",
			'C' => "Charlie",
			'D' => "Delta",
			'E' => "Echo",
			'F' => "Foxtrot",
			'G' => "Golf",
			'H' => "Hotel",
			'I' => "India",
			'J' => "Juliett",
			'K' => "Kilo",
			'L' => "Lima",
			'M' => "Mike",
			'N' => "November",
			'O' => "Oscar",
			'P' => "Papa",
			'Q' => "Quebec",
			'R' => "Romeo",
			'S' => "Sierra",
			'T' => "Tango",
			'U' => "Uniform",
			'V' => "Victor",
			'W' => "Whiskey",
			'X' => "X-ray",
			'Y' => "Yankee",
			'Z' => "Zulu",
			_ => atisLetter
		};
	}

	private static string? TryResolveAtisLetterToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
			return null;

		token = token.Trim();
		if (token.Length == 1 && token[0] is >= 'A' and <= 'Z' || token[0] is >= 'a' and <= 'z')
			return token.ToUpperInvariant();

		var upper = token.ToUpperInvariant().Replace(" ", "").Replace("-", "");
		return upper switch
		{
			"ALFA" or "ALPHA" => "A",
			"BRAVO" => "B",
			"CHARLIE" => "C",
			"DELTA" => "D",
			"ECHO" => "E",
			"FOXTROT" => "F",
			"GOLF" => "G",
			"HOTEL" => "H",
			"INDIA" => "I",
			"JULIETT" or "JULIET" => "J",
			"KILO" => "K",
			"LIMA" => "L",
			"MIKE" => "M",
			"NOVEMBER" => "N",
			"OSCAR" => "O",
			"PAPA" => "P",
			"QUEBEC" => "Q",
			"ROMEO" => "R",
			"SIERRA" => "S",
			"TANGO" => "T",
			"UNIFORM" => "U",
			"VICTOR" => "V",
			"WHISKEY" or "WHISKY" => "W",
			"XRAY" => "X",
			"YANKEE" => "Y",
			"ZULU" => "Z",
			_ => null
		};
	}

	private async Task<string?> RouteToPhaseHandlerAsync(AtcContext atcContext, string pilotTransmission, PilotIntent pilotIntent, CancellationToken cancellationToken, ResolvedContext? resolvedContext = null)
	{
		switch (_context.CurrentPhase)
		{
		case FlightPhase.Preflight_Clearance:
			// Check ATIS before allowing clearance to proceed from pending data state.
			if (_state == AtcState.ClearancePendingData)
			{
				if (!_atisConfirmed)
				{
					var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
					_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Atis, pilotTransmission, pilotIntent);
					return $"{cs}, confirm you have the latest ATIS or information.";
				}
				if (ClearanceHelpers.ClearanceDataComplete(atcContext))
				{
					var result = await HandleClearanceAsync(atcContext, "Pilot is waiting for IFR clearance.", cancellationToken, pilotIntent: pilotIntent, resolvedContext: resolvedContext);
					if (result != null)
						return OutputGuard.ScrubOutput(result, resolvedContext, _onDebug);
					// If HandleClearanceAsync returned null, fall through to LLM
				}
			}
			var clearanceResult = await HandleClearanceAsync(atcContext, pilotTransmission, cancellationToken, maskDestination: ShouldMaskDestination(pilotTransmission), pilotIntent: pilotIntent, resolvedContext: resolvedContext);
			if (clearanceResult != null)
				return OutputGuard.ScrubOutput(clearanceResult, resolvedContext, _onDebug);
			// If HandleClearanceAsync returned null (validation failed, no missing info), return null to trigger LLM fallback
			return null;
		case FlightPhase.Taxi_Out:
                        var taxiOutResult = await PhaseHandlers.HandleTaxiOutPhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(taxiOutResult ?? string.Empty, resolvedContext, _onDebug);
		case FlightPhase.Lineup_Takeoff:
                        var lineupResult = await PhaseHandlers.HandleLineupTakeoffPhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(lineupResult ?? string.Empty, resolvedContext, _onDebug);
		case FlightPhase.Climb_Departure:
                        var climbResult = await PhaseHandlers.HandleDepartureClimbPhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(climbResult ?? string.Empty, resolvedContext, _onDebug);
		case FlightPhase.Enroute:
			var enrouteResult = await PhaseHandlers.HandleEnroutePhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(enrouteResult ?? string.Empty, resolvedContext, _onDebug);
		case FlightPhase.Descent_Arrival:
                        var descentResult = await PhaseHandlers.HandleArrivalPhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(descentResult ?? string.Empty, resolvedContext, _onDebug);
		case FlightPhase.Approach:
                        var approachResult = await PhaseHandlers.HandleApproachPhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(approachResult ?? string.Empty, resolvedContext, _onDebug);
		case FlightPhase.Landing:
                        var landingResult = await PhaseHandlers.HandleLandingPhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(landingResult ?? string.Empty, resolvedContext, _onDebug);
		case FlightPhase.Taxi_In:
                        var taxiInResult = await PhaseHandlers.HandleTaxiInPhase(pilotTransmission, atcContext, _context, _responseGenerator, cancellationToken);
			return OutputGuard.ScrubOutput(taxiInResult ?? string.Empty, resolvedContext, _onDebug);
		default:
			if (!HasContextChanged(atcContext))
			{
				return _lastAtcResponse;
			}
			return await CallLlmAsync(atcContext, pilotTransmission, cancellationToken, resolvedContext);
		}
	}

	public async Task<string?> HandleClearanceAsync(AtcContext context, string pilotTransmission, CancellationToken ct = default(CancellationToken), bool maskDestination = false, PilotIntent? pilotIntent = null, ResolvedContext? resolvedContext = null)
	{
		if (ClearanceHelpers.IsNonOperationalAck(pilotTransmission))
		{
			return null;
		}
		pilotIntent ??= _intentParser.ParseIntent(pilotTransmission, _context);
		bool isIfrRequest = ClearanceHelpers.IsIfrRequest(pilotTransmission);
		switch (_state)
		{
		case AtcState.Idle:
		{
			if (!isIfrRequest)
			{
				string lower = pilotTransmission.ToLowerInvariant();
				var resolvedCtxIdle = resolvedContext ?? ResolvedContextBuilder.Build(_context);
				if (lower.Contains("clearance") || lower.Contains("clearence") || lower.Contains("clearan"))
				{
					context.Permissions.AllowIfrClearance = false;
					context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
					return await CallLlmAsync(context, pilotTransmission, ct, resolvedCtxIdle);
				}
				context.Permissions.AllowIfrClearance = false;
				context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
				return await CallLlmAsync(context, pilotTransmission, ct, resolvedCtxIdle);
			}
			_state = AtcState.IfrRequested;
			
			// CRITICAL: If pilot explicitly said "requesting IFR clearance" (or equivalent),
			// mark it as explicit immediately - no confirmation prompt needed.
			if (HasExplicitIfrRequest(pilotTransmission))
			{
				_clearanceRequestInfo.IfrRequestExplicit = true;
				LogDebug($"[IFR] Explicit IFR clearance request detected in: '{pilotTransmission}'");
			}
			
			// Try to resolve aircraft type from pilot speech before mapping context.
			var resolvedTypeFromPilot = AircraftTypeResolver.ResolveSimple(pilotTransmission);
			if (!string.IsNullOrWhiteSpace(resolvedTypeFromPilot))
				_context.Aircraft = WithIcaoType(_context.Aircraft, resolvedTypeFromPilot);
			string? logApi = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
			if (!string.IsNullOrWhiteSpace(logApi) && (logApi.Equals("1", StringComparison.OrdinalIgnoreCase) || logApi.Equals("true", StringComparison.OrdinalIgnoreCase) || logApi.Equals("yes", StringComparison.OrdinalIgnoreCase)))
			{
				Console.WriteLine("[DEBUG] FlightContext before mapping:");
				Console.WriteLine("  DestinationIcao: '" + _context.DestinationIcao + "'");
				Console.WriteLine("  SquawkCode: '" + _context.SquawkCode + "'");
				Console.WriteLine($"  ClearedAltitude: {_context.ClearedAltitude}");
				Console.WriteLine($"  CruiseFlightLevel: {_context.CruiseFlightLevel}");
			}
			context = FlightContextToAtcContextMapper.Map(pilotIntent: pilotIntent, flightContext: _context, ifrClearanceIssued: _ifrClearanceIssued, hideDestination: maskDestination);
			if (!string.IsNullOrWhiteSpace(logApi) && (logApi.Equals("1", StringComparison.OrdinalIgnoreCase) || logApi.Equals("true", StringComparison.OrdinalIgnoreCase) || logApi.Equals("yes", StringComparison.OrdinalIgnoreCase)))
			{
				Console.WriteLine("[DEBUG] AtcContext after mapping:");
				Console.WriteLine("  ClearedTo: '" + context.ClearanceDecision.ClearedTo + "'");
				Console.WriteLine($"  InitialAltitudeFt: {context.ClearanceDecision.InitialAltitudeFt}");
				Console.WriteLine("  Squawk: '" + context.ClearanceDecision.Squawk + "'");
			}
			// Check destination mismatch FIRST - this is a hard stop, no clearance should be issued.
			var destMismatch = pilotIntent.Parameters.TryGetValue("__dest_mismatch", out var dm) && dm == "true";
			if (destMismatch)
			{
				var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
				var plannedName = !string.IsNullOrWhiteSpace(_context.DestinationName) ? _context.DestinationName : AirportNameResolver.ResolveAirportName(_context.DestinationIcao, _context);
				_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Destination, pilotTransmission, pilotIntent);
				_clearanceRequestInfo.DestinationConfirmed = false;
				if (TrainingConfig.StrictClearanceData)
				{
					_state = AtcState.ClearanceCollectingTrainingData;
				}
				else
				{
					_state = AtcState.ClearancePendingData;
				}
				return $"{cs}, flight plan shows destination {plannedName}, confirm destination.";
			}
			else
			{
				// Destination matches or not mentioned - mark as confirmed if it matches flight plan
				var destMention = ExtractDestinationMention(pilotTransmission, pilotIntent);
				if (!destMention.HasDestination || DestinationMatchesFlightPlan(destMention))
				{
					_clearanceRequestInfo.DestinationConfirmed = true;
				}
			}

			// Check ATIS confirmation BEFORE issuing clearance (training/VATSIM-style requirement).
			// NOTE: ATIS extraction already ran in HandlePilotTransmissionAsync (before this method),
			// so _atisConfirmed should already be set if ATIS was detected.
			// This check is a final safeguard to ensure ATIS is confirmed before clearance.
			if (TrainingConfig.StrictAtisForClearance && !_atisConfirmed && _context.CurrentPhase == FlightPhase.Preflight_Clearance)
			{
				// Re-check ATIS in this transmission (in case extraction missed it)
				ExtractAndPersistAtisAcknowledgement(pilotTransmission);
				
				// If still not confirmed, prompt for ATIS
				if (!_atisConfirmed)
				{
					var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
					_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Atis, pilotTransmission, pilotIntent);
					_state = TrainingConfig.StrictClearanceData ? AtcState.ClearanceCollectingTrainingData : AtcState.ClearancePendingData;
					
					// Get current ATIS letter from cache to tell pilot what they should have.
					var currentAtis = AtisMetarCache.Get(_context.OriginIcao);
					if (!string.IsNullOrWhiteSpace(currentAtis.AtisLetter))
					{
						var phonetic = ToAtisPhonetic(currentAtis.AtisLetter);
						return $"{cs}, confirm you have information {phonetic}.";
					}
					return $"{cs}, confirm you have the latest information.";
				}
			}

			// Check if this is a clearance request context (Delivery or Ground during Preflight_Clearance phase)
			bool isClearanceContext = _context.CurrentPhase == FlightPhase.Preflight_Clearance &&
				(_context.CurrentAtcUnit == AtcUnit.ClearanceDelivery || _context.CurrentAtcUnit == AtcUnit.Ground);

			// Validate that the request is meaningful (not nonsense).
			// If validation fails but we're in a clearance context, still check for missing info instead of rejecting
			// CRITICAL: If validation fails and we can't prompt for missing info, return null to allow LLM fallback
			if (!IsValidClearanceRequest(pilotTransmission, pilotIntent))
			{
				// If we're in clearance context, check for missing info first before rejecting
				if (isClearanceContext)
				{
					EnsureSquawk(context);
					var missingPrompt = BuildMissingInfoPrompt(context, pilotTransmission, destinationMismatch: false);
					if (!string.IsNullOrWhiteSpace(missingPrompt))
					{
						_state = AtcState.ClearancePendingData;
						return missingPrompt;
					}
				}
				
				// Validation failed and no missing info to prompt for
				// Return null to allow LLM fallback instead of hard-coded "say again"
				// The LLM can handle misheard or partially understood clearance requests
				LogDebug($"[Clearance] Validation failed but allowing LLM fallback for: '{pilotTransmission}'");
				return null; // This will trigger LLM fallback in RouteToPhaseHandlerAsync
			}

			// STRICT TRAINING MODE: Check if all required training slots are collected
			if (TrainingConfig.StrictClearanceData)
			{
				var nextSlot = GetNextMissingSlot();
				if (nextSlot.HasValue)
				{
					_state = AtcState.ClearanceCollectingTrainingData;
					return BuildTrainingSlotPrompt(nextSlot.Value);
				}
			}

			// If we already have full clearance data, respond immediately with a deterministic clearance.
			EnsureSquawk(context);
			if (ClearanceHelpers.ClearanceDataComplete(context))
			{
				string directClearance = BuildDeterministicClearance(context);
				_state = AtcState.ClearanceIssued;
				_ifrClearanceIssued = true;
				context.StateFlags.IfrClearanceIssued = true;
				PersistClearanceData(context);
				if (HasCriticalItems(context))
					_pendingReadback = new PendingReadbackRequest(context, null, directClearance);
				LogDebug($"[Readback] Deterministic clearance issued; expect readback? {HasCriticalItems(context)}");
				return directClearance;
			}

			// Missing critical IFR fields – prompt for just the missing items.
			// CRITICAL: Always check for missing info when in clearance context (Delivery/Ground)
			// Note: destMismatch was already checked above, so this should be false here.
			var missingInfoCheck = BuildMissingInfoPrompt(context, pilotTransmission, destinationMismatch: false);
			if (!string.IsNullOrWhiteSpace(missingInfoCheck))
			{
				_state = AtcState.ClearancePendingData;
				return missingInfoCheck;
			}
			
			// Double-check ATIS even if BuildMissingInfoPrompt returned null (strict training mode).
			if (TrainingConfig.StrictAtisForClearance && !_atisConfirmed && _context.CurrentPhase == FlightPhase.Preflight_Clearance)
			{
				// Check if pilot transmission contains ATIS acknowledgement
				if (!IsAtisConfirmation(pilotTransmission) && !HasAtisInTransmission(pilotTransmission))
				{
					var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
					_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Atis, pilotTransmission, pilotIntent);
					_state = TrainingConfig.StrictClearanceData ? AtcState.ClearanceCollectingTrainingData : AtcState.ClearancePendingData;
					return $"{cs}, confirm you have the latest information.";
				}
				// If ATIS was mentioned, mark as confirmed
				if (HasAtisInTransmission(pilotTransmission))
				{
					_atisConfirmed = true;
					_clearanceRequestInfo.AtisAcknowledged = true;
					TryUpdateDepartureAtisLetter(pilotTransmission);
				}
			}

			// Final check: all training slots must be collected (strict training mode)
			if (TrainingConfig.StrictClearanceData)
			{
				var nextSlot = GetNextMissingSlot();
				if (nextSlot.HasValue)
				{
					_state = AtcState.ClearanceCollectingTrainingData;
					return BuildTrainingSlotPrompt(nextSlot.Value);
				}
			}

			// All required data present and ATIS confirmed: issue clearance.
			context.Permissions.AllowIfrClearance = true;
			context.ClearanceDecision.ClearanceType = "IFR_CLEARANCE";
			EnsureSquawk(context);
			var resolvedCtx = resolvedContext ?? ResolvedContextBuilder.Build(_context);
			string atc3 = await CallLlmAsync(context, pilotTransmission, ct, resolvedCtx);
			_state = AtcState.ClearanceIssued;
			_ifrClearanceIssued = true;
			context.StateFlags.IfrClearanceIssued = true;
			PersistClearanceData(context);
			return atc3;
		}
		case AtcState.ClearancePendingData:
			pilotIntent = _intentParser.ParseIntent(pilotTransmission, _context);
			context = FlightContextToAtcContextMapper.Map(pilotIntent: pilotIntent, flightContext: _context, ifrClearanceIssued: _ifrClearanceIssued, hideDestination: maskDestination);
			
			// Check if we're in clearance context (Delivery or Ground during Preflight_Clearance)
			bool isClearanceContextPending = _context.CurrentPhase == FlightPhase.Preflight_Clearance &&
				(_context.CurrentAtcUnit == AtcUnit.ClearanceDelivery || _context.CurrentAtcUnit == AtcUnit.Ground);
			
			// If strict training mode, transition to collecting training data
			if (TrainingConfig.StrictClearanceData)
			{
				var nextSlot = GetNextMissingSlot();
				if (nextSlot.HasValue)
				{
					_state = AtcState.ClearanceCollectingTrainingData;
					// Try to collect from this transmission
					var collected = CollectTrainingSlot(pilotTransmission, nextSlot.Value);
					if (collected)
					{
						var next = GetNextMissingSlot();
						if (!next.HasValue)
						{
							_state = AtcState.ClearanceReady;
							goto case AtcState.ClearanceReady;
						}
						return BuildTrainingSlotPrompt(next.Value);
					}
					return BuildTrainingSlotPrompt(nextSlot.Value);
				}
			}
			
			// Check ATIS confirmation if still pending (non-strict mode fallback).
			if (TrainingConfig.StrictAtisForClearance && !_atisConfirmed && _context.CurrentPhase == FlightPhase.Preflight_Clearance)
			{
				// Check if pilot transmission contains ATIS acknowledgement
				if (!IsAtisConfirmation(pilotTransmission) && !HasAtisInTransmission(pilotTransmission))
				{
					var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
					_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Atis, pilotTransmission, pilotIntent);
					if (TrainingConfig.StrictClearanceData)
					{
						_state = AtcState.ClearanceCollectingTrainingData;
					}
					else
					{
						_state = AtcState.ClearancePendingData;
					}
					return $"{cs}, confirm you have the latest information.";
				}
				// If ATIS was mentioned, mark as confirmed
				if (HasAtisInTransmission(pilotTransmission))
				{
					_atisConfirmed = true;
					_clearanceRequestInfo.AtisAcknowledged = true;
					TryUpdateDepartureAtisLetter(pilotTransmission);
				}
			}

			// CRITICAL: Always check for missing info when in clearance context
			// This ensures iterative prompting until all required data is collected
			EnsureSquawk(context);
			
			// Check for missing information first
			var missingInfoPrompt = BuildMissingInfoPrompt(context, pilotTransmission, destinationMismatch: false);
			if (!string.IsNullOrWhiteSpace(missingInfoPrompt))
			{
				// Stay in ClearancePendingData state to continue collecting
				_state = AtcState.ClearancePendingData;
				return missingInfoPrompt;
			}
			
			// Only check if complete if no missing info was found
			if (ClearanceHelpers.ClearanceDataComplete(context))
			{
				// Check strict training mode before transitioning to ready
				if (TrainingConfig.StrictClearanceData)
				{
					var nextSlot = GetNextMissingSlot();
					if (nextSlot.HasValue)
					{
						_state = AtcState.ClearanceCollectingTrainingData;
						return BuildTrainingSlotPrompt(nextSlot.Value);
					}
				}
				_state = AtcState.ClearanceReady;
				goto case AtcState.ClearanceReady;
			}
			
			// If data is still incomplete but BuildMissingInfoPrompt returned nothing,
			// check again more thoroughly (this should rarely happen, but ensures we don't miss anything)
			if (isClearanceContextPending)
			{
				var thoroughCheck = BuildMissingInfoPrompt(context, pilotTransmission, destinationMismatch: false);
				if (!string.IsNullOrWhiteSpace(thoroughCheck))
				{
					_state = AtcState.ClearancePendingData;
					return thoroughCheck;
				}
			}
			if (isIfrRequest && !ClearanceHelpers.IsNonOperationalAck(pilotTransmission))
			{
				// Check if we're in clearance context (Delivery or Ground during Preflight_Clearance)
				bool isClearanceContext = _context.CurrentPhase == FlightPhase.Preflight_Clearance &&
					(_context.CurrentAtcUnit == AtcUnit.ClearanceDelivery || _context.CurrentAtcUnit == AtcUnit.Ground);

				// Check destination mismatch first - hard stop, no clearance.
				var destMismatch = pilotIntent.Parameters.TryGetValue("__dest_mismatch", out var dm) && dm == "true";
				if (destMismatch)
				{
					var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
					var plannedName = !string.IsNullOrWhiteSpace(_context.DestinationName) ? _context.DestinationName : AirportNameResolver.ResolveAirportName(_context.DestinationIcao, _context);
					_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Destination, pilotTransmission, pilotIntent);
					_clearanceRequestInfo.DestinationConfirmed = false;
					if (TrainingConfig.StrictClearanceData)
					{
						_state = AtcState.ClearanceCollectingTrainingData;
					}
					else
					{
						_state = AtcState.ClearancePendingData;
					}
					return $"{cs}, flight plan shows destination {plannedName}, confirm destination.";
				}

				// CRITICAL: Always check for missing info when in clearance context
				// This ensures ATC iteratively asks for missing information until complete
				EnsureSquawk(context);
				var missingPrompt = BuildMissingInfoPrompt(context, pilotTransmission, destinationMismatch: false);
				if (!string.IsNullOrWhiteSpace(missingPrompt))
				{
					if (TrainingConfig.StrictClearanceData)
					{
						_state = AtcState.ClearanceCollectingTrainingData;
					}
					else
					{
						_state = AtcState.ClearancePendingData;
					}
					return missingPrompt;
				}

				// If no missing info but we're in clearance context and data is incomplete, 
				// still check completeness one more time before allowing LLM
				if (isClearanceContext && !ClearanceHelpers.ClearanceDataComplete(context))
				{
					// Re-check with more thorough validation
					var thoroughMissingPrompt = BuildMissingInfoPrompt(context, pilotTransmission, destinationMismatch: false);
					if (!string.IsNullOrWhiteSpace(thoroughMissingPrompt))
					{
						_state = AtcState.ClearancePendingData;
						return thoroughMissingPrompt;
					}
				}

				// All data present but ATIS not confirmed: still block (strict training mode).
				if (TrainingConfig.StrictAtisForClearance && !_atisConfirmed && _context.CurrentPhase == FlightPhase.Preflight_Clearance)
				{
					// Check if pilot transmission contains ATIS acknowledgement
					if (!IsAtisConfirmation(pilotTransmission) && !HasAtisInTransmission(pilotTransmission))
					{
						var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
						_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Atis, pilotTransmission, pilotIntent);
						if (TrainingConfig.StrictClearanceData)
						{
							_state = AtcState.ClearanceCollectingTrainingData;
						}
						return $"{cs}, confirm you have the latest information.";
					}
					// If ATIS was mentioned, mark as confirmed
					if (HasAtisInTransmission(pilotTransmission))
					{
						_atisConfirmed = true;
						_clearanceRequestInfo.AtisAcknowledged = true;
						TryUpdateDepartureAtisLetter(pilotTransmission);
					}
				}

				// Check strict training mode slots before allowing clearance
				if (TrainingConfig.StrictClearanceData)
				{
					var nextSlot = GetNextMissingSlot();
					if (nextSlot.HasValue)
					{
						_state = AtcState.ClearanceCollectingTrainingData;
						return BuildTrainingSlotPrompt(nextSlot.Value);
					}
				}

				context.Permissions.AllowIfrClearance = false;
				context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
				return await CallLlmAsync(context, pilotTransmission, ct);
			}
			return null;
		case AtcState.ClearanceCollectingTrainingData:
		{
			// Deterministic slot collection - no LLM calls
			var nextSlot = GetNextMissingSlot();
			if (!nextSlot.HasValue)
			{
				// All slots collected - transition to ready state
				_state = AtcState.ClearanceReady;
				goto case AtcState.ClearanceReady;
			}

			// Try to collect the next missing slot
			var collected = CollectTrainingSlot(pilotTransmission, nextSlot.Value);
			if (collected)
			{
				// Slot collected - check for next missing slot
				var next = GetNextMissingSlot();
				if (!next.HasValue)
				{
					// All slots collected
					_state = AtcState.ClearanceReady;
					goto case AtcState.ClearanceReady;
				}
				// Ask for next slot
				return BuildTrainingSlotPrompt(next.Value);
			}

			// Slot not collected - check if response is unrelated
			var lower = pilotTransmission.ToLowerInvariant();
			if (IsRadioCheckRequest(lower) || !IsRelevantToTrainingSlot(pilotTransmission, nextSlot.Value))
			{
				var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
				return $"Unable. {BuildTrainingSlotPrompt(nextSlot.Value)}";
			}

			// Re-prompt for same slot
			return BuildTrainingSlotPrompt(nextSlot.Value);
		}
		case AtcState.ClearanceReady:
		{
			// Final check: all training slots must be collected (strict training mode)
			if (TrainingConfig.StrictClearanceData)
			{
				var nextSlot = GetNextMissingSlot();
				if (nextSlot.HasValue)
				{
					_state = AtcState.ClearanceCollectingTrainingData;
					return BuildTrainingSlotPrompt(nextSlot.Value);
				}
			}

			// Final ATIS check before issuing clearance (strict training mode).
			if (TrainingConfig.StrictAtisForClearance && !_atisConfirmed && _context.CurrentPhase == FlightPhase.Preflight_Clearance)
			{
				// Check if pilot transmission contains ATIS acknowledgement
				if (!IsAtisConfirmation(pilotTransmission) && !HasAtisInTransmission(pilotTransmission))
				{
					var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
					_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Atis, pilotTransmission, pilotIntent);
					_state = TrainingConfig.StrictClearanceData ? AtcState.ClearanceCollectingTrainingData : AtcState.ClearancePendingData;
					return $"{cs}, confirm you have the latest information.";
				}
				// If ATIS was mentioned, mark as confirmed
				if (HasAtisInTransmission(pilotTransmission))
				{
					_atisConfirmed = true;
					_clearanceRequestInfo.AtisAcknowledged = true;
					TryUpdateDepartureAtisLetter(pilotTransmission);
				}
			}

			context.Permissions.AllowIfrClearance = true;
			context.ClearanceDecision.ClearanceType = "IFR_CLEARANCE";
			EnsureSquawk(context);
			string effectivePilotText = (string.IsNullOrWhiteSpace(pilotTransmission) ? "Pilot is waiting for IFR clearance." : pilotTransmission);
			var resolvedCtx2 = resolvedContext ?? ResolvedContextBuilder.Build(_context);
			string atc = await CallLlmAsync(context, effectivePilotText, ct, resolvedCtx2);
			_state = AtcState.ClearanceIssued;
			_ifrClearanceIssued = true;
			context.StateFlags.IfrClearanceIssued = true;
			PersistClearanceData(context);
			if (HasCriticalItems(context))
				_pendingReadback = new PendingReadbackRequest(context, null, atc);
			return atc;
		}
		case AtcState.ClearanceIssued:
			// Previously we swallowed readbacks here, which meant no reply.
			// Route the readback to the LLM so it can confirm/correct it.
			var resolvedCtx3 = resolvedContext ?? ResolvedContextBuilder.Build(_context);
			return await CallLlmAsync(context, pilotTransmission, ct, resolvedCtx3);
		default:
			return null;
		}
	}

	private async Task<string> CallLlmAsync(AtcContext context, string pilotTransmission, CancellationToken cancellationToken, ResolvedContext? resolvedContext = null)
	{
		try
		{
			_routingMetrics.IncrementLlmCalls();
                        var request = new AtcRequest
                        {
                                TranscriptText = pilotTransmission,
                                ControllerRole = context.ControllerRole,
                                FlightContext = _context,
                                SessionState = _state,
                                AtcContext = context
                        };
                        var rawResponse = (await _responseGenerator.GenerateAsync(request, cancellationToken)).SpokenText.Trim();
			
			// Apply output guardrails to scrub ICAO codes and raw callsigns
			var scrubbedResponse = OutputGuard.ScrubOutput(rawResponse, resolvedContext, _onDebug);
			
			_lastAtcResponse = scrubbedResponse;
			_lastContext = context;
			if (HasCriticalItems(context))
			{
				_pendingReadback = new PendingReadbackRequest(context, null, _lastAtcResponse);
				LogDebug($"[Readback] Expecting readback for controller_role={context.ControllerRole}, cleared_to={context.ClearanceDecision.ClearedTo}, runway={context.ClearanceDecision.DepRunway}, squawk={context.ClearanceDecision.Squawk}, alt={context.ClearanceDecision.InitialAltitudeFt}, sid={context.ClearanceDecision.Sid}, type={context.ClearanceDecision.ClearanceType}");
			}
			return scrubbedResponse;
		}
		catch (OperationCanceledException)
		{
			_routingMetrics.IncrementLlmFailures();
			RoutingDecisionLogger.LogLlmFailure(pilotTransmission, "LLM timeout/cancelled", null, _onDebug);
			return "Standby, processing your request.";
		}
		catch (InvalidOperationException ex2)
		{
			_routingMetrics.IncrementLlmFailures();
			InvalidOperationException ex3 = ex2;
			Console.Error.WriteLine("ERROR: " + ex3.Message);
			if (ex3.InnerException != null)
			{
				Console.Error.WriteLine("  Inner: " + ex3.InnerException.Message);
			}
			RoutingDecisionLogger.LogLlmFailure(pilotTransmission, $"InvalidOperation: {ex3.Message}", ex3, _onDebug);
			return "Standby, experiencing technical difficulties. Please check your .env file and API key.";
		}
		catch (Exception ex4)
		{
			_routingMetrics.IncrementLlmFailures();
			Exception ex5 = ex4;
			Console.Error.WriteLine("ERROR: " + ex5.GetType().Name + ": " + ex5.Message);
			RoutingDecisionLogger.LogLlmFailure(pilotTransmission, $"{ex5.GetType().Name}: {ex5.Message}", ex5, _onDebug);
			return "Standby, experiencing technical difficulties. (" + ex5.GetType().Name + ")";
		}
	}

	private void BeginLoadingSimbriefNavWeatherAsync(AtcContext context)
	{
		Task.Run(async delegate
		{
			await Task.Delay(100);
			if (await CheckAndAutoIssueClearanceAsync() == null)
			{
			}
		});
	}

	private bool HasContextChanged(AtcContext newContext)
	{
		if (_lastContext == null)
		{
			return true;
		}
		return _lastContext.ClearanceDecision.ClearanceType != newContext.ClearanceDecision.ClearanceType || _lastContext.Permissions.AllowIfrClearance != newContext.Permissions.AllowIfrClearance || _lastContext.StateFlags.IfrClearanceIssued != newContext.StateFlags.IfrClearanceIssued || _lastContext.Phase != newContext.Phase || _lastContext.ControllerRole != newContext.ControllerRole || _lastContext.ClearanceDecision.ClearedTo != newContext.ClearanceDecision.ClearedTo || _lastContext.ClearanceDecision.DepRunway != newContext.ClearanceDecision.DepRunway || _lastContext.ClearanceDecision.Squawk != newContext.ClearanceDecision.Squawk;
	}

	public FlightContext GetContext()
	{
		return _context;
	}

	public AtcState GetState()
	{
		return _state;
	}

	public void ResetForNewFlight()
	{
		_state = AtcState.Idle;
		_ifrClearanceIssued = false;
		_lastAtcResponse = null;
		_lastContext = null;
		_pendingReadback = null;
		_pendingConfirmation = null;
		_atisConfirmed = false;
		_clearanceRequestInfo.Reset();
		// Note: DepartureAtisLetter is preserved in FlightContext.ResetForNewFlight() if needed
		_context.ResetForNewFlight();
	}

	public async Task<string?> CheckAndAutoIssueClearanceAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (_state == AtcState.ClearancePendingData)
		{
			AtcContext atcContext = FlightContextToAtcContextMapper.Map(pilotIntent: new PilotIntent
			{
				Type = IntentType.RequestClearance,
				RawText = "Pilot is waiting for IFR clearance."
			}, flightContext: _context, ifrClearanceIssued: _ifrClearanceIssued, hideDestination: false);
			EnsureSquawk(atcContext);
			if (ClearanceHelpers.ClearanceDataComplete(atcContext))
			{
				_state = AtcState.ClearanceReady;
				return await HandleClearanceAsync(atcContext, "Pilot is waiting for IFR clearance.", cancellationToken, pilotIntent: new PilotIntent { Type = IntentType.RequestClearance, RawText = "Pilot is waiting for IFR clearance." });
			}
		}
		return null;
	}

	private void UpdateStateFromPilotTransmission(string pilotTransmission)
	{
		string text = pilotTransmission.ToLowerInvariant();
		if (text.Contains("clearance") && text.Contains("request"))
		{
			_context.CurrentPhase = FlightPhase.Preflight_Clearance;

			// Only switch to Clearance Delivery if the airport actually has one; otherwise stay on the current unit (e.g., Ground).
			if (AirportFrequencies.TryGetFrequencies(_context.OriginIcao, out var freqs) && freqs.Clearance.HasValue)
			{
				_context.CurrentAtcUnit = AtcUnit.ClearanceDelivery;
			}
		}
	}

	private static string? TryExtractCallsignToken(string pilotTransmission)
	{
		var match = Regex.Match(pilotTransmission.ToUpperInvariant(), "\\b([A-Z]{3}\\s?\\d{1,4})\\b");
		if (match.Success)
		{
			return match.Groups[1].Value;
		}
		return null;
	}

	private static string NormalizeCallsign(string value)
	{
		return value.Replace(" ", string.Empty).ToUpperInvariant();
	}

	public void UpdatePhaseFromSimState(SimState sim)
	{
		if (sim == null)
		{
			return;
		}
		switch (_context.CurrentPhase)
		{
		case FlightPhase.Preflight_Clearance:
			break;
		case FlightPhase.Taxi_Out:
			if (sim.OnRunway && _context.DepartureRunway != null)
			{
				_context.CurrentPhase = FlightPhase.Lineup_Takeoff;
				_context.CurrentAtcUnit = AtcUnit.Tower;
			}
			break;
		case FlightPhase.Lineup_Takeoff:
			if (!sim.OnGround && sim.AltitudeFeet > 100)
			{
				_context.CurrentPhase = FlightPhase.Climb_Departure;
				_context.CurrentAtcUnit = AtcUnit.Departure;
			}
			break;
		case FlightPhase.Climb_Departure:
			if (sim.AltitudeFeet >= _context.CruiseFlightLevel * 100 - 1000)
			{
				_context.CurrentPhase = FlightPhase.Enroute;
				_context.CurrentAtcUnit = AtcUnit.Center;
			}
			break;
		case FlightPhase.Enroute:
			if (sim.AltitudeFeet < _context.CruiseFlightLevel * 100 - 5000)
			{
				_context.CurrentPhase = FlightPhase.Descent_Arrival;
				_context.CurrentAtcUnit = AtcUnit.Arrival;
			}
			break;
		case FlightPhase.Descent_Arrival:
			if (sim.AltitudeFeet < 10000 && sim.OnApproachCourse)
			{
				_context.CurrentPhase = FlightPhase.Approach;
				_context.CurrentAtcUnit = AtcUnit.Approach;
			}
			break;
		case FlightPhase.Approach:
			if (sim.OnFinal && sim.AltitudeFeet < 1000)
			{
				_context.CurrentPhase = FlightPhase.Landing;
				_context.CurrentAtcUnit = AtcUnit.Tower;
			}
			break;
		case FlightPhase.Landing:
			if (sim.OnGround && sim.GroundSpeedKts < 30 && !sim.OnRunway)
			{
				_context.CurrentPhase = FlightPhase.Taxi_In;
				_context.CurrentAtcUnit = AtcUnit.Ground;
			}
			break;
		}
	}

	public void Dispose()
	{
		if (!_disposed)
		{
                        if (_responseGenerator is IDisposable disposable)
                        {
                                disposable.Dispose();
                        }
			_disposed = true;
		}
	}

	private string NormalizePilotTransmission(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
		{
			return pilotTransmission;
		}

		var normalized = CallsignNormalizer.Normalize(pilotTransmission, _context);

		// Fix common STT slips for radio checks so we respond correctly and avoid confusing transcripts.
		normalized = Regex.Replace(normalized, @"\bradio\s+chat\b", "radio check", RegexOptions.IgnoreCase);
		normalized = Regex.Replace(normalized, @"\bradio\s+chek\b", "radio check", RegexOptions.IgnoreCase);
		normalized = Regex.Replace(normalized, @"\bradio\s+chek\b", "radio check", RegexOptions.IgnoreCase);
		normalized = Regex.Replace(normalized, @"\bradio\s+ch[eai]t\b", "radio check", RegexOptions.IgnoreCase);

		normalized = ReadbackNormalizer.Normalize(normalized, _context);
		return normalized;
	}

	private string BuildDeterministicClearance(AtcContext context)
	{
		// Build a concise, runway/SID-inclusive clearance without calling the LLM.
		var callsign = !string.IsNullOrWhiteSpace(_context.RadioCallsign)
			? _context.RadioCallsign
			: (_context.Callsign ?? "Aircraft");

		var resolvedDestination = AirportNameResolver.ResolveAirportName(_context.DestinationIcao, _context);
		string clearedTo = !string.IsNullOrWhiteSpace(resolvedDestination)
			? resolvedDestination
			: (context.ClearanceDecision.ClearedTo ?? _context.DestinationName ?? "destination");

		string depRunway = context.ClearanceDecision.DepRunway ?? _context.DepartureRunway?.RunwayIdentifier ?? "runway";
		string sid = !string.IsNullOrWhiteSpace(context.ClearanceDecision.Sid)
			? context.ClearanceDecision.Sid
			: (!string.IsNullOrWhiteSpace(_context.SelectedSid?.SelectedSid?.ProcedureIdentifier)
				? _context.SelectedSid.SelectedSid.ProcedureIdentifier
				: "radar vectors");
		string initialClimb = context.ClearanceDecision.InitialAltitudeFt.HasValue
			? $"{context.ClearanceDecision.InitialAltitudeFt.Value} feet"
			: "initial altitude";
		string squawk = context.ClearanceDecision.Squawk ?? _context.SquawkCode ?? "XXXX";
		string expectFl = !string.IsNullOrWhiteSpace(context.FlightInfo?.CruiseLevel)
			? context.FlightInfo.CruiseLevel
			: $"FL{_context.CruiseFlightLevel}";

		var sidPhrase = sid.Equals("radar vectors", StringComparison.OrdinalIgnoreCase)
			? "via radar vectors"
			: $"via {sid} departure";

		return $"{callsign}, cleared to {clearedTo}, {sidPhrase}. Initial climb {initialClimb}, squawk {squawk}, expect {expectFl} after departure.";
	}

	private string? BuildReadbackAcknowledgementTail(AtcContext context)
	{
		var role = ResolveTemplateRole();
		var phase = ResolveTemplatePhase(role);
		var data = BuildRendererTemplateData(context);

		var tail = _templateRenderer?.RenderReadbackAcknowledgementTail(phase, role, data);
		if (!string.IsNullOrWhiteSpace(tail))
		{
			return tail;
		}

		// Fallback to legacy deterministic text if templates are unavailable.
		return _context.CurrentAtcUnit switch
		{
			AtcUnit.ClearanceDelivery => "Call ready for push and start.",
			AtcUnit.Ground => "Advise ready for push and start. When ready for taxi, call ground.",
			_ => null
		};
	}

	private void PersistClearanceData(AtcContext context)
	{
		if (!string.IsNullOrWhiteSpace(context.ClearanceDecision.Squawk))
		{
			_context.SquawkCode = context.ClearanceDecision.Squawk;
		}
		if (context.ClearanceDecision.InitialAltitudeFt.HasValue)
		{
			_context.ClearedAltitude = context.ClearanceDecision.InitialAltitudeFt.Value;
		}
		if (!string.IsNullOrWhiteSpace(context.ClearanceDecision.DepRunway))
		{
			var depRunwayId = context.ClearanceDecision.DepRunway;

			if (_context.DepartureRunway == null)
			{
				_context.DepartureRunway = new NavRunwaySummary
				{
					AirportIcao = _context.OriginIcao ?? string.Empty,
					RunwayIdentifier = depRunwayId
				};
			}
			else
			{
				var existing = _context.DepartureRunway;
				_context.DepartureRunway = new NavRunwaySummary
				{
					AirportIcao = existing.AirportIcao,
					RunwayIdentifier = depRunwayId,
					TrueHeadingDegrees = existing.TrueHeadingDegrees,
					LengthFeet = existing.LengthFeet,
					HasIlsOrLocalizer = existing.HasIlsOrLocalizer,
					HasRnavApproach = existing.HasRnavApproach,
					IsPreferredDeparture = existing.IsPreferredDeparture,
					IsPreferredArrival = existing.IsPreferredArrival
				};
			}
		}

		_context.CurrentAtcState = AtcState.ClearanceIssued;
	}

	private void EnsureSquawk(AtcContext context)
	{
		if (string.IsNullOrWhiteSpace(context.ClearanceDecision.Squawk))
		{
			var code = string.IsNullOrWhiteSpace(_context.SquawkCode) ? GenerateSquawk() : _context.SquawkCode;
			context.ClearanceDecision.Squawk = code;
		}
	}

	private string? BuildMissingInfoPrompt(AtcContext ctx, string pilotTransmission, bool destinationMismatch = false)
	{
		var missing = new List<string>();

		// ATIS confirmation must be checked FIRST (strict training mode requirement).
		if (TrainingConfig.StrictAtisForClearance && !_atisConfirmed && _context.CurrentPhase == FlightPhase.Preflight_Clearance)
		{
			if (!IsAtisConfirmation(pilotTransmission) && !HasAtisInTransmission(pilotTransmission))
			{
				missing.Add("atis");
			}
			else
			{
				_atisConfirmed = true;
				_clearanceRequestInfo.AtisAcknowledged = true;
				TryUpdateDepartureAtisLetter(pilotTransmission);
			}
		}

		// Note: destinationMismatch should be handled BEFORE calling this method, but keep it for safety.
		if (destinationMismatch)
		{
			missing.Add("destination");
		}
		else if (string.IsNullOrWhiteSpace(ctx.ClearanceDecision.ClearedTo))
		{
			missing.Add("destination");
		}

		if (!ctx.ClearanceDecision.InitialAltitudeFt.HasValue)
			missing.Add("initial_altitude");

		// Validate aircraft type using resolver; if invalid/unknown, prompt for ICAO type.
		var flightType = _context.Aircraft?.IcaoType;
		var resolvedFromPilot = AircraftTypeResolver.ResolveSimple(pilotTransmission);
		var mentionedType = MentionsAircraftType(pilotTransmission);
		if (!string.IsNullOrWhiteSpace(resolvedFromPilot))
		{
			_context.Aircraft = WithIcaoType(_context.Aircraft, resolvedFromPilot);
			flightType = resolvedFromPilot;
		}

		if (string.IsNullOrWhiteSpace(flightType))
		{
			missing.Add("aircraft_type");
		}
		else if (mentionedType && string.IsNullOrWhiteSpace(resolvedFromPilot))
		{
			missing.Add("aircraft_type");
		}

		if (missing.Count == 0)
			return null;

		var orderedSlots = OrderMissingSlots(missing);
		var slot = orderedSlots.FirstOrDefault();
		if (slot == null)
			return null;

		if (string.Equals(slot, "atis", StringComparison.OrdinalIgnoreCase))
		{
			_pendingConfirmation = new PendingConfirmation(ConfirmationSlot.Atis, pilotTransmission, null);
		}

		var role = ResolveTemplateRole();
		var phase = ResolveTemplatePhase(role);
		var data = BuildRendererTemplateData(ctx);

		var rendered = _templateRenderer?.RenderMissingInfoPrompt(slot, phase, role, data);
		if (!string.IsNullOrWhiteSpace(rendered))
		{
			return rendered;
		}

		// Fallback to deterministic phrasing if templates are unavailable.
		var callsign = data.TryGetValue("callsign", out var cs) ? cs : (_context.Callsign ?? "Aircraft");
		return slot.ToLowerInvariant() switch
		{
			"atis" => $"{callsign}, confirm you have the latest information.",
			"destination" => $"{callsign}, say destination.",
			"aircraft_type" => $"{callsign}, confirm aircraft type.",
			"initial_altitude" => $"{callsign}, say initial altitude.",
			_ => $"{callsign}, provide missing information."
		};
	}

	private IEnumerable<string> OrderMissingSlots(IEnumerable<string> missing)
	{
		if (_packs?.MissingInfo.SlotPriorities == null || _packs.MissingInfo.SlotPriorities.Count == 0)
		{
			return missing;
		}

		var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < _packs.MissingInfo.SlotPriorities.Count; i++)
		{
			var slot = _packs.MissingInfo.SlotPriorities[i];
			if (!priorities.ContainsKey(slot))
			{
				priorities[slot] = i;
			}
		}

		return missing.OrderBy(s => priorities.ContainsKey(s) ? priorities[s] : int.MaxValue)
			.ThenBy(s => s, StringComparer.OrdinalIgnoreCase);
	}

	private Dictionary<string, string> BuildRendererTemplateData(AtcContext ctx)
	{
		var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		var callsign = !string.IsNullOrWhiteSpace(_context.RadioCallsign)
			? _context.RadioCallsign
			: (_context.Callsign ?? "Aircraft");
		data["callsign"] = callsign;

		var destName = !string.IsNullOrWhiteSpace(_context.DestinationName)
			? _context.DestinationName
			: AirportNameResolver.ResolveAirportName(_context.DestinationIcao, _context);
		data["destination_name"] = destName ?? string.Empty;

		if (!string.IsNullOrWhiteSpace(_context.Aircraft?.IcaoType))
		{
			data["aircraft_type"] = _context.Aircraft!.IcaoType!;
		}

		if (ctx.ClearanceDecision.InitialAltitudeFt.HasValue)
		{
			data["initial_altitude"] = $"{ctx.ClearanceDecision.InitialAltitudeFt.Value}";
		}

		var atis = AtisMetarCache.Get(_context.OriginIcao).AtisLetter ?? _context.DepartureAtisLetter;
		if (!string.IsNullOrWhiteSpace(atis))
		{
			data["atis_letter"] = ToAtisPhonetic(atis!);
		}

		data["role"] = ResolveTemplateRole();
		data["phase"] = ResolveTemplatePhase(data["role"]);

		return data;
	}

	private string ResolveTemplateRole()
	{
		return _context.CurrentAtcUnit switch
		{
			AtcUnit.ClearanceDelivery => "delivery",
			AtcUnit.Ground => "ground",
			AtcUnit.Tower => "tower",
			AtcUnit.Departure => "departure",
			AtcUnit.Center => "center",
			AtcUnit.Arrival => "approach",
			AtcUnit.Approach => "approach",
			_ => "delivery"
		};
	}

	private string ResolveTemplatePhase(string? role)
	{
		if (!string.IsNullOrWhiteSpace(role) && _packs?.RolePhaseMap != null && _packs.RolePhaseMap.TryGetValue(role, out var phaseFromRole))
		{
			return phaseFromRole;
		}

		return role?.ToLowerInvariant() switch
		{
			"delivery" => "clearance",
			"ground" => "ground",
			"tower" => "tower",
			"departure" => "departure",
			"approach" => "approach",
			"center" => "center",
			_ => "clearance"
		};
	}

	private static bool MentionsAircraftType(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		var lower = pilotTransmission.ToLowerInvariant();
		if (lower.Contains("airbus") || lower.Contains("boeing") || lower.Contains("embraer") ||
		    lower.Contains("cessna") || lower.Contains("piper") || lower.Contains("atr") ||
		    lower.Contains("dash") || lower.Contains("q400") || lower.Contains("king air") ||
		    lower.Contains("crj"))
		{
			return true;
		}

		// ICAO/IATA-like token (A320, B38M, 7M8, etc) or common mistaken "25-100" pattern.
		return Regex.IsMatch(pilotTransmission, @"\b[A-Z]{1,2}\d{2,3}[A-Z]?\b", RegexOptions.IgnoreCase)
		       || Regex.IsMatch(pilotTransmission, @"\b\d{2}[-\s]?\d{2,3}\b", RegexOptions.IgnoreCase);
	}

	private static bool IsValidClearanceRequest(string pilotTransmission, PilotIntent intent)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		// If intent is RequestClearance, it's valid.
		if (intent.Type == IntentType.RequestClearance)
			return true;

		// Check for meaningful clearance-related keywords.
		var lower = pilotTransmission.ToLowerInvariant();
		bool hasRequest = lower.Contains("request") || lower.Contains("requesting");
		bool hasClearance = lower.Contains("clearance") || lower.Contains("clearence") || lower.Contains("clearan");
		bool hasIfr = lower.Contains("ifr");
		
		// Must have request + clearance keywords, or be an IFR request.
		if (!hasRequest || (!hasClearance && !hasIfr))
			return false;

		// Reject obvious nonsense (very short, no meaningful words).
		var words = Regex.Matches(lower, @"\b[a-z]{3,}\b").Count;
		if (words < 3)
			return false;

		// Reject if it's just "request clearance to ding dong" with no real airport name.
		// This is a heuristic: if it contains "to" followed by non-airport-like words, it might be nonsense.
		// But we're lenient here - let the destination mismatch check catch bad destinations.
		return true;
	}

	private static AircraftPerformanceProfile WithIcaoType(AircraftPerformanceProfile? existing, string icaoType)
	{
		var type = (icaoType ?? string.Empty).Trim().ToUpperInvariant();
		if (existing == null)
		{
			return new AircraftPerformanceProfile
			{
				IcaoType = type,
				RequiredTakeoffDistanceFeet = 8000,
				RequiredLandingDistanceFeet = 6000,
				MaxTailwindComponentKnots = 10,
				MaxCrosswindComponentKnots = 25
			};
		}

		return new AircraftPerformanceProfile
		{
			IcaoType = type,
			RequiredTakeoffDistanceFeet = existing.RequiredTakeoffDistanceFeet,
			RequiredLandingDistanceFeet = existing.RequiredLandingDistanceFeet,
			MaxTailwindComponentKnots = existing.MaxTailwindComponentKnots,
			MaxCrosswindComponentKnots = existing.MaxCrosswindComponentKnots
		};
	}

	private static string GenerateSquawk()
	{
		var rng = new Random();
		return $"{rng.Next(1, 8)}{rng.Next(0, 8)}{rng.Next(0, 8)}{rng.Next(0, 8)}";
	}

	private DestinationMention ExtractDestinationMention(string pilotTransmission, PilotIntent intent)
	{
		string? icao = null;
		string? name = null;
		bool hasDestination = false;

		if (intent.Parameters.TryGetValue("destination", out var destVal) && !string.IsNullOrWhiteSpace(destVal))
		{
			hasDestination = true;
			var trimmed = destVal.Trim();
			if (Regex.IsMatch(trimmed, "^[A-Z]{4}$", RegexOptions.IgnoreCase))
			{
				icao = trimmed.ToUpperInvariant();
			}
			else
			{
				name = trimmed;
			}
		}

		foreach (Match match in Regex.Matches(pilotTransmission.ToUpperInvariant(), "\\b[A-Z]{4}\\b"))
		{
			var token = match.Value;
			if (IsNonAirportToken(token))
				continue;
			if (icao == null)
			{
				icao = token;
			}
			hasDestination = true;
		}

		var nameCandidate = ExtractDestinationNameCandidate(pilotTransmission);
		if (!string.IsNullOrWhiteSpace(nameCandidate))
		{
			name = nameCandidate;
			hasDestination = true;
		}

		return new DestinationMention(icao, name, hasDestination);
	}

	private bool DestinationMatchesFlightPlan(DestinationMention mention)
	{
		if (string.IsNullOrWhiteSpace(_context.DestinationIcao))
			return true;

		if (DestinationResolver.Matches($"{mention.Icao} {mention.Name}", _context))
			return true;

		var planIcao = _context.DestinationIcao.Trim().ToUpperInvariant();
		var planName = _context.DestinationName;

		if (!string.IsNullOrWhiteSpace(mention.Icao))
		{
			return string.Equals(mention.Icao.Trim(), planIcao, StringComparison.OrdinalIgnoreCase);
		}

		if (!string.IsNullOrWhiteSpace(mention.Name))
		{
			var cleanedMention = CleanDestinationName(mention.Name);
			if (string.IsNullOrWhiteSpace(cleanedMention))
				return true;

			// Accept spoken destination if it roughly matches the flight plan airport name.
			if (!string.IsNullOrWhiteSpace(planName) && NamesMatch(cleanedMention, planName))
				return true;

			// If we don't have a name on file, fall back to accepting.
			if (string.IsNullOrWhiteSpace(planName))
				return true;

			return false;
		}

		// If the pilot never mentioned a destination, don't block the flow.
		return true;
	}

	private bool IsNonAirportToken(string token)
	{
		if (string.IsNullOrWhiteSpace(token))
			return true;

		var baseSkips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"THIS", "THAT", "WITH", "FROM", "CLEAR", "READY", "STAND",
			"RADIO", "CHECK", "GROUND", "TOWER", "APPROACH", "DEPARTURE", "APP", "DEP"
		};
		if (baseSkips.Contains(token))
			return true;

		// Ignore airline/radio-name tokens (e.g., "EASY" from EasyJet) so we don't mis-read them as ICAO codes.
		var airlineTokens = new List<string>();
		if (!string.IsNullOrWhiteSpace(_context.AirlineIcao))
			airlineTokens.Add(_context.AirlineIcao.ToUpperInvariant());
		if (!string.IsNullOrWhiteSpace(_context.Callsign))
		{
			var letters = new string(_context.Callsign.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
			if (letters.Length >= 2)
				airlineTokens.Add(letters);
		}
		if (!string.IsNullOrWhiteSpace(_context.AirlineName))
		{
			foreach (var part in Regex.Matches(_context.AirlineName.ToUpperInvariant(), "[A-Z]+").Select(m => m.Value))
			{
				if (part.Length >= 3)
					airlineTokens.Add(part);
			}
		}

		return airlineTokens.Any(a => token.Equals(a, StringComparison.OrdinalIgnoreCase));
	}

	private static string? ExtractDestinationNameCandidate(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return null;

		var match = Regex.Match(pilotTransmission, @"\b(?:to|destination|dest)\s+([A-Za-z][A-Za-z\s'\-]{2,40})", RegexOptions.IgnoreCase);
		if (!match.Success)
			return null;

		var candidate = match.Groups[1].Value;
		var stopMarkers = new[]
		{
			",", ".", ";",
			" requesting", " request",
			" for ", " on ", " with ",
			" stand ", " gate ", " runway ",
			" information ", " info ", " clearance ",
			" as filed", " as planned"
		};
		foreach (var marker in stopMarkers)
		{
			var idx = candidate.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			if (idx >= 0)
			{
				candidate = candidate.Substring(0, idx);
				break;
			}
		}

		candidate = CleanDestinationName(candidate);
		return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
	}

	private static string CleanDestinationName(string value)
	{
		var cleaned = value.Trim();
		cleaned = Regex.Replace(cleaned, @"\b(as filed|as planned)\b", "", RegexOptions.IgnoreCase);
		cleaned = Regex.Replace(cleaned, @"\b(request(ing)?|clearance)\b", "", RegexOptions.IgnoreCase);
		cleaned = cleaned.Trim();
		return cleaned;
	}

	private static bool NamesMatch(string spokenName, string planName)
	{
		if (string.IsNullOrWhiteSpace(spokenName) || string.IsNullOrWhiteSpace(planName))
			return false;

		var spokenNorm = NormalizeNameForCompare(spokenName);
		var planNorm = NormalizeNameForCompare(planName);

		if (planNorm.Contains(spokenNorm) || spokenNorm.Contains(planNorm))
			return true;

		var spokenTokens = ExtractNameTokens(spokenName);
		var planTokens = ExtractNameTokens(planName);
		if (spokenTokens.Count == 0 || planTokens.Count == 0)
			return false;

		foreach (var st in spokenTokens)
		{
			foreach (var pt in planTokens)
			{
				if (TokensClose(st, pt))
					return true;
			}
		}

		return false;
	}

	private static List<string> ExtractNameTokens(string value)
	{
		return Regex.Matches(value.ToLowerInvariant(), "[a-z]+")
			.Select(m => m.Value)
			.Where(v => v.Length >= 3)
			.ToList();
	}

	private static string NormalizeNameForCompare(string value)
	{
		return Regex.Replace(value, "[^A-Za-z]", "").ToLowerInvariant();
	}

	private static bool TokensClose(string a, string b)
	{
		if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
			return true;

		if (Math.Abs(a.Length - b.Length) > 1)
			return false;

		if (a.Length <= 2 || b.Length <= 2)
			return false;

		return LevenshteinDistance(a, b) <= 1;
	}

	private static int LevenshteinDistance(string a, string b)
	{
		int[,] d = new int[a.Length + 1, b.Length + 1];
		for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
		for (int j = 0; j <= b.Length; j++) d[0, j] = j;

		for (int i = 1; i <= a.Length; i++)
		{
			for (int j = 1; j <= b.Length; j++)
			{
				int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
				d[i, j] = Math.Min(
					Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
					d[i - 1, j - 1] + cost);
			}
		}
		return d[a.Length, b.Length];
	}

	private readonly record struct DestinationMention(string? Icao, string? Name, bool HasDestination);

	private bool ShouldMaskDestination(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(_context.DestinationIcao))
			return false;

		// We already have destination from the flight plan; do not block on pilot restating it.
		return false;
	}

	private static bool IsRadioCheckRequest(string lowerTransmission)
	{
		if (string.IsNullOrWhiteSpace(lowerTransmission))
			return false;

		// Common variants/mis-hearings (chat/chek/check/mic/comms)
		if (lowerTransmission.Contains("radio check") ||
		    lowerTransmission.Contains("raido check") ||
		    lowerTransmission.Contains("radio chek") ||
		    lowerTransmission.Contains("radio test") ||
		    lowerTransmission.Contains("radio chat") ||
		    lowerTransmission.Contains("radio check in") ||
		    lowerTransmission.Contains("check radio") ||
		    lowerTransmission.Contains("mic check") ||
		    lowerTransmission.Contains("mic test") ||
		    lowerTransmission.Contains("comms check") ||
		    lowerTransmission.Contains("communication check") ||
		    lowerTransmission.Contains("meter check") ||
		    lowerTransmission.Contains("meteor check"))
		{
			return true;
		}

		// Fuzzy regex: "radio" within two words of "check"/"test"/"chat"
		return Regex.IsMatch(lowerTransmission, @"radio\s+\w{0,8}\s*(check|chek|chat|test)", RegexOptions.IgnoreCase);
	}

	private string BuildRadioCheckReply()
	{
		var replyCallsign = !string.IsNullOrWhiteSpace(_context.RadioCallsign)
			? _context.RadioCallsign
			: (!string.IsNullOrWhiteSpace(_context.Callsign) ? _context.Callsign : "Aircraft");
		var unitName = _context.CurrentAtcUnit switch
		{
			AtcUnit.ClearanceDelivery => "delivery",
			AtcUnit.Ground => "ground",
			AtcUnit.Tower => "tower",
			AtcUnit.Departure => "departure",
			AtcUnit.Center => "center",
			AtcUnit.Arrival => "arrival",
			AtcUnit.Approach => "approach",
			_ => "ATC"
		};
		return $"{replyCallsign}, {unitName}, radio check, read you five by five.";
	}

	private bool HasCriticalItems(AtcContext ctx)
	{
		if (ctx?.ClearanceDecision == null)
			return false;

		var cd = ctx.ClearanceDecision;
		// Flag when critical clearance content is present.
		return !string.IsNullOrWhiteSpace(cd.DepRunway)
			|| !string.IsNullOrWhiteSpace(cd.Squawk)
			|| cd.InitialAltitudeFt.HasValue
			|| !string.IsNullOrWhiteSpace(cd.Sid)
			|| (!string.IsNullOrWhiteSpace(cd.ClearedTo) && cd.ClearanceType == "IFR_CLEARANCE")
			|| (cd.ClearanceType == "TAXI" || cd.ClearanceType == "LINEUP" || cd.ClearanceType == "TAKEOFF" || cd.ClearanceType == "LANDING");
	}

	/// <summary>
	/// Check if pilot transmission is relevant to destination confirmation (contains destination, affirmation, etc.).
	/// </summary>
	private bool IsRelevantToDestinationConfirmation(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		var lower = pilotTransmission.ToLowerInvariant();

		// Affirmations are relevant
		if (lower.Contains("affirm") || lower.Contains("affirmative") || lower.Contains("yes") || 
		    lower.Contains("correct") || lower.Contains("confirmed") || lower.Contains("roger") ||
		    lower.Contains("that is correct") || lower.Contains("that's correct"))
			return true;

		// Airport names/ICAO codes are relevant
		if (Regex.IsMatch(pilotTransmission, @"\b[A-Z]{4}\b", RegexOptions.IgnoreCase))
			return true;

		// Common airport name words
		var airportWords = new[] { "airport", "stansted", "heathrow", "gatwick", "manchester", "edinburgh", "birmingham" };
		foreach (var word in airportWords)
		{
			if (lower.Contains(word))
				return true;
		}

		// If it's very short and doesn't contain destination-related words, it's probably unrelated
		if (pilotTransmission.Length < 10 && !lower.Contains("destination") && !lower.Contains("to "))
			return false;

		return true;
	}

	/// <summary>
	/// Check if pilot transmission is relevant to ATIS confirmation (contains ATIS-related content, affirmation, etc.).
	/// </summary>
	private bool IsRelevantToAtisConfirmation(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		// ATIS-related content is always relevant
		if (HasAtisInTransmission(pilotTransmission))
			return true;

		var lower = pilotTransmission.ToLowerInvariant();

		// Affirmations are relevant
		if (lower.Contains("affirm") || lower.Contains("affirmative") || lower.Contains("yes") || 
		    lower.Contains("correct") || lower.Contains("confirmed") || lower.Contains("roger") ||
		    lower.Contains("that is correct") || lower.Contains("that's correct"))
			return true;

		// If it's very short and doesn't contain ATIS-related words, it's probably unrelated
		if (pilotTransmission.Length < 10 && !lower.Contains("information") && !lower.Contains("atis"))
			return false;

		return true;
	}

	/// <summary>
	/// Extract stand/gate from pilot transmission (e.g., "stand 15", "gate A12", "parking 5").
	/// Handles STT variants like "standard 15" -> "stand 15", "on stand 15", spoken numbers, etc.
	/// </summary>
	private static (bool Found, string? Value) ExtractStandGate(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return (false, null);

		// Pre-normalize STT variants
		var normalized = NormalizeStandGateInput(pilotTransmission);
		
		// Patterns: "stand 15", "gate A12", "parking 5", "bay 3", "position 12"
		// Also handles "on stand 15", "at gate A12"
		var patterns = new[]
		{
			// "on stand 15", "at stand 15", "stand 15"
			@"\b(?:on\s+|at\s+)?(?:stand|gate|parking|bay|position|ramp)\s+([A-Z]?\d+[A-Z]?)\b",
			// "gate bravo 12", "gate alpha 3"
			@"\b(?:on\s+|at\s+)?(?:stand|gate)\s+((?:alpha|alfa|bravo|charlie|delta|echo|foxtrot|golf|hotel)\s*\d+)\b",
			// "stand A12", "gate B3"
			@"\b(?:on\s+|at\s+)?(?:stand|gate|parking|bay|position)\s+([A-Z]\s*\d+)\b",
			// Just "A12" or "B3" after "stand" or "gate" keyword nearby
			@"\b(?:stand|gate)\b.*?\b([A-Z]\d+)\b"
		};

		foreach (var pattern in patterns)
		{
			var match = Regex.Match(normalized, pattern, RegexOptions.IgnoreCase);
			if (match.Success && match.Groups.Count > 1)
			{
				var value = NormalizeStandGateValue(match.Groups[1].Value.Trim());
				if (!string.IsNullOrWhiteSpace(value))
					return (true, value);
			}
		}

		return (false, null);
	}

	/// <summary>
	/// Pre-normalize stand/gate input to handle STT variants.
	/// </summary>
	private static string NormalizeStandGateInput(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return input;

		var result = input;

		// STT mishears: "standard" -> "stand", "stent" -> "stand"
		result = Regex.Replace(result, @"\bstandard\b", "stand", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bstent\b", "stand", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bstands\b", "stand", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bgates?\b", "gate", RegexOptions.IgnoreCase);

		// Normalize spoken numbers to digits
		result = NormalizeSpokenNumbersForStand(result);

		return result;
	}

	/// <summary>
	/// Normalize spoken numbers in stand/gate context (e.g., "fifteen" -> "15").
	/// </summary>
	private static string NormalizeSpokenNumbersForStand(string input)
	{
		if (string.IsNullOrWhiteSpace(input))
			return input;

		var result = input;

		// Single digits
		result = Regex.Replace(result, @"\bone\b", "1", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\btwo\b", "2", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthree\b", "3", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bfour\b", "4", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bfive\b", "5", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bsix\b", "6", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseven\b", "7", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\beight\b", "8", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bnine\b", "9", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bniner\b", "9", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bzero\b", "0", RegexOptions.IgnoreCase);

		// Teens
		result = Regex.Replace(result, @"\bten\b", "10", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\beleven\b", "11", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\btwelve\b", "12", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthirteen\b", "13", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bfourteen\b", "14", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bfifteen\b", "15", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bsixteen\b", "16", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bseventeen\b", "17", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\beighteen\b", "18", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bnineteen\b", "19", RegexOptions.IgnoreCase);

		// Tens
		result = Regex.Replace(result, @"\btwenty\b", "20", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bthirty\b", "30", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bforty\b", "40", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bfifty\b", "50", RegexOptions.IgnoreCase);

		return result;
	}

	/// <summary>
	/// Normalize the extracted stand/gate value (e.g., "bravo 12" -> "B12", remove spaces).
	/// </summary>
	private static string NormalizeStandGateValue(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return value;

		var result = value.Trim();

		// Convert phonetic letters to single letters
		result = Regex.Replace(result, @"\balpha\b", "A", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\balfa\b", "A", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bbravo\b", "B", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bcharlie\b", "C", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bdelta\b", "D", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\becho\b", "E", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bfoxtrot\b", "F", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bgolf\b", "G", RegexOptions.IgnoreCase);
		result = Regex.Replace(result, @"\bhotel\b", "H", RegexOptions.IgnoreCase);

		// Remove spaces between letter and number (e.g., "A 12" -> "A12")
		result = Regex.Replace(result, @"([A-Z])\s+(\d)", "$1$2", RegexOptions.IgnoreCase);

		return result.ToUpperInvariant();
	}

	/// <summary>
	/// Check if pilot transmission contains explicit IFR clearance request.
	/// Matches anywhere in the sentence, case-insensitive.
	/// Ignores surrounding politeness phrases.
	/// </summary>
	private static bool HasExplicitIfrRequest(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		var lower = pilotTransmission.ToLowerInvariant();

		// Explicit clearance request phrases (match anywhere in sentence)
		// "requesting ifr clearance", "request ifr clearance", "ifr clearance request", "request clearance"
		if (Regex.IsMatch(lower, @"\brequesting\s+ifr\s+clearance\b"))
			return true;
		if (Regex.IsMatch(lower, @"\brequest\s+ifr\s+clearance\b"))
			return true;
		if (Regex.IsMatch(lower, @"\bifr\s+clearance\s+request\b"))
			return true;
		if (Regex.IsMatch(lower, @"\brequest\s+clearance\b"))
			return true;
		if (Regex.IsMatch(lower, @"\brequesting\s+clearance\b"))
			return true;
		
		// Also accept "as filed" with IFR context
		if (Regex.IsMatch(lower, @"\bas\s+filed\b") && lower.Contains("ifr"))
			return true;
		
		// Accept "ifr to <destination>" pattern
		if (Regex.IsMatch(lower, @"\bifr\s+to\s+\w+"))
			return true;

		return false;
	}

	/// <summary>
	/// Check if aircraft type was explicitly stated/confirmed by pilot (strict training mode requirement).
	/// Accepts natural speech like "Boeing 737", "737-800", "Airbus 320" and normalizes to ICAO code.
	/// Uses the generic AircraftTypeResolver.
	/// </summary>
	private bool IsAircraftTypeConfirmed(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		// Use the new generic AircraftTypeResolver
		var (success, icaoCode, isAmbiguous) = AircraftTypeResolver.Resolve(pilotTransmission);
		if (success && !string.IsNullOrWhiteSpace(icaoCode))
		{
			_context.Aircraft = WithIcaoType(_context.Aircraft, icaoCode);
			_clearanceRequestInfo.AircraftTypeConfirmed = true;
			LogDebug($"[AIRCRAFT] Resolved type '{icaoCode}' from '{pilotTransmission}' (ambiguous={isAmbiguous})");
			return true;
		}

		// Check if pilot confirmed existing type (e.g., "correct", "affirm", "yes" when asked about type)
		var lower = pilotTransmission.ToLowerInvariant();
		if ((lower.Contains("correct") || lower.Contains("affirm") || lower.Contains("yes") || lower.Contains("that's right")) &&
		    !string.IsNullOrWhiteSpace(_context.Aircraft?.IcaoType))
		{
			_clearanceRequestInfo.AircraftTypeConfirmed = true;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Get the next missing training slot to collect (priority order).
	/// </summary>
	private TrainingSlot? GetNextMissingSlot()
	{
		if (!_clearanceRequestInfo.DestinationConfirmed)
			return TrainingSlot.Destination;

		if (TrainingConfig.StrictAtisForClearance && !_clearanceRequestInfo.AtisAcknowledged)
			return TrainingSlot.Atis;

		// Only ask for aircraft type if it's truly unknown (not from SimBrief or already captured)
		if (TrainingConfig.StrictClearanceData && !_clearanceRequestInfo.AircraftTypeConfirmed)
		{
			// If SimBrief already provided aircraft type, mark as confirmed
			if (!string.IsNullOrWhiteSpace(_context.Aircraft?.IcaoType))
			{
				_clearanceRequestInfo.AircraftTypeConfirmed = true;
			}
			else
			{
				return TrainingSlot.AircraftType;
			}
		}

		// Only ask for stand/gate if truly unknown (not already captured from transmission or context)
		if (TrainingConfig.StrictClearanceData && !_clearanceRequestInfo.StandGateCollected)
		{
			// If stand is already known, mark as collected
			if (!string.IsNullOrWhiteSpace(_context.Stand))
			{
				_clearanceRequestInfo.StandGateCollected = true;
				_clearanceRequestInfo.StandGateValue = _context.Stand;
			}
			else
			{
				return TrainingSlot.StandGate;
			}
		}

		// Only ask for IFR confirmation if it wasn't explicitly stated
		// If pilot already said "requesting IFR clearance", don't ask again
		if (TrainingConfig.StrictClearanceData && !_clearanceRequestInfo.IfrRequestExplicit)
		{
			LogDebug("[IFR] IfrRequestExplicit is false - will prompt for confirmation");
			return TrainingSlot.IfrRequest;
		}

		return null;
	}

	/// <summary>
	/// Build prompt for missing training slot.
	/// </summary>
	private string BuildTrainingSlotPrompt(TrainingSlot slot)
	{
		var cs = !string.IsNullOrWhiteSpace(_context.RadioCallsign) ? _context.RadioCallsign : (_context.Callsign ?? "Aircraft");
		
		if (slot == TrainingSlot.Atis)
		{
			var currentAtis = AtisMetarCache.Get(_context.OriginIcao);
			if (!string.IsNullOrWhiteSpace(currentAtis.AtisLetter))
			{
				var phonetic = ToAtisPhonetic(currentAtis.AtisLetter);
				return $"{cs}, confirm you have information {phonetic}.";
			}
			return $"{cs}, confirm you have the latest information.";
		}

		return slot switch
		{
			TrainingSlot.Destination => $"{cs}, confirm destination.",
			TrainingSlot.AircraftType => $"{cs}, confirm aircraft type.",
			TrainingSlot.StandGate => $"{cs}, confirm stand or gate.",
			TrainingSlot.IfrRequest => $"{cs}, confirm IFR clearance request.",
			_ => $"{cs}, standby."
		};
	}

	/// <summary>
	/// Process pilot transmission to collect training slots (deterministic, no LLM).
	/// </summary>
	private bool CollectTrainingSlot(string pilotTransmission, TrainingSlot slot)
	{
		switch (slot)
		{
		case TrainingSlot.Destination:
			// Destination confirmation is handled by existing flow
			return _clearanceRequestInfo.DestinationConfirmed;

		case TrainingSlot.Atis:
			if (IsAtisConfirmation(pilotTransmission) || HasAtisInTransmission(pilotTransmission))
			{
				_clearanceRequestInfo.AtisAcknowledged = true;
				_atisConfirmed = true;
				TryUpdateDepartureAtisLetter(pilotTransmission);
				return true;
			}
			return false;

		case TrainingSlot.AircraftType:
			if (IsAircraftTypeConfirmed(pilotTransmission))
			{
				_clearanceRequestInfo.AircraftTypeConfirmed = true;
				return true;
			}
			return false;

		case TrainingSlot.StandGate:
			var (found, value) = ExtractStandGate(pilotTransmission);
			if (found && !string.IsNullOrWhiteSpace(value))
			{
				_context.Stand = value;
				_clearanceRequestInfo.StandGateCollected = true;
				_clearanceRequestInfo.StandGateValue = value;
				LogDebug($"[STAND] Collected stand/gate '{value}' from slot collection");
				return true;
			}
			return false;

		case TrainingSlot.IfrRequest:
			if (HasExplicitIfrRequest(pilotTransmission))
			{
				_clearanceRequestInfo.IfrRequestExplicit = true;
				return true;
			}
			return false;

		default:
			return false;
		}
	}

	/// <summary>
	/// Check if pilot transmission is relevant to the current training slot being collected.
	/// </summary>
	private bool IsRelevantToTrainingSlot(string pilotTransmission, TrainingSlot slot)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		var lower = pilotTransmission.ToLowerInvariant();

		// Affirmations are always relevant
		if (lower.Contains("affirm") || lower.Contains("affirmative") || lower.Contains("yes") || 
		    lower.Contains("correct") || lower.Contains("confirmed") || lower.Contains("roger"))
			return true;

		return slot switch
		{
			TrainingSlot.Destination => IsRelevantToDestinationConfirmation(pilotTransmission),
			TrainingSlot.Atis => IsRelevantToAtisConfirmation(pilotTransmission),
			TrainingSlot.AircraftType => IsRelevantToAircraftType(pilotTransmission),
			TrainingSlot.StandGate => lower.Contains("stand") || lower.Contains("gate") || lower.Contains("parking") || 
			                          lower.Contains("bay") || lower.Contains("position"),
			TrainingSlot.IfrRequest => lower.Contains("ifr") || lower.Contains("clearance") || lower.Contains("request"),
			_ => true
		};
	}

	/// <summary>
	/// Check if pilot transmission is relevant to aircraft type confirmation.
	/// </summary>
	private static bool IsRelevantToAircraftType(string pilotTransmission)
	{
		if (string.IsNullOrWhiteSpace(pilotTransmission))
			return false;

		var lower = pilotTransmission.ToLowerInvariant();

		// Affirmations are always relevant
		if (lower.Contains("affirm") || lower.Contains("affirmative") || lower.Contains("yes") || 
		    lower.Contains("correct") || lower.Contains("confirmed") || lower.Contains("roger"))
			return true;

		// Aircraft-related keywords
		if (lower.Contains("aircraft") || lower.Contains("type") || lower.Contains("boeing") || 
		    lower.Contains("airbus") || lower.Contains("bowen") || lower.Contains("bowing"))
			return true;

		// ICAO-style aircraft codes (A320, B738, etc.)
		if (Regex.IsMatch(pilotTransmission, @"\b[A-Z]{1,2}\d{2,3}[A-Z]?\b", RegexOptions.IgnoreCase))
			return true;

		// Model numbers (737, 320, etc.)
		if (Regex.IsMatch(pilotTransmission, @"\b(7[3-8]7|3[1-8]0|A?3[2-5]0)\b", RegexOptions.IgnoreCase))
			return true;

		// Spoken numbers that could be aircraft types
		if (lower.Contains("seven three") || lower.Contains("seven four") || lower.Contains("seven five") ||
		    lower.Contains("seven six") || lower.Contains("seven seven") || lower.Contains("seven eight") ||
		    lower.Contains("three twenty") || lower.Contains("three nineteen") || lower.Contains("three thirty") ||
		    lower.Contains("three forty") || lower.Contains("three fifty") || lower.Contains("three eighty"))
			return true;

		return false;
	}
}

/// <summary>
/// Training slots that must be collected in strict training mode.
/// </summary>
internal enum TrainingSlot
{
	Destination,
	Atis,
	AircraftType,
	StandGate,
	IfrRequest
}

public enum ConfirmationSlot
{
	Destination,
	Atis
}

internal sealed record PendingReadbackRequest(AtcContext Context, HashSet<string>? Slots, string? IssuedAtcText);

internal sealed record PendingConfirmation(ConfirmationSlot Slot, string OriginalText, PilotIntent? OriginalIntent);
