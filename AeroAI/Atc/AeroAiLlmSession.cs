using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Config;
using AeroAI.Llm;

namespace AeroAI.Atc;

public class AeroAiLlmSession : IDisposable
{
	private readonly AeroAiPhraseEngine _phraseEngine;

	private readonly FlightContext _context;

	private readonly PilotIntentParser _intentParser;

	private AtcState _state = AtcState.Idle;

	private bool _ifrClearanceIssued = false;

	private string? _lastAtcResponse;

	private AtcContext? _lastContext;

	private bool _disposed = false;

	public AeroAiLlmSession(ILlmClient llm, FlightContext context)
	{
		_context = context ?? throw new ArgumentNullException("context");
		_intentParser = new PilotIntentParser();
		if (llm is OpenAiLlmClient)
		{
			EnvironmentConfig.Load();
			try
			{
				string openAiApiKey = EnvironmentConfig.GetOpenAiApiKey();
				string openAiModel = EnvironmentConfig.GetOpenAiModel();
				string openAiBaseUrl = EnvironmentConfig.GetOpenAiBaseUrl();
				string systemPromptPath = EnvironmentConfig.GetSystemPromptPath();
				_phraseEngine = new AeroAiPhraseEngine(openAiApiKey, openAiModel, openAiBaseUrl, systemPromptPath);
				return;
			}
			catch (InvalidOperationException ex)
			{
				throw new InvalidOperationException("Failed to initialize OpenAI client: " + ex.Message + ". Please ensure your .env file contains OPENAI_API_KEY=sk-your-key-here", ex);
			}
			catch (FileNotFoundException ex2)
			{
				throw new FileNotFoundException("System prompt file not found: " + ex2.Message + ". Please ensure prompts/aeroai_system_prompt.txt exists.", ex2);
			}
		}
		throw new NotSupportedException("Only OpenAI is currently supported. Please use OpenAiLlmClient.");
	}

	public async Task<string?> HandlePilotTransmissionAsync(string pilotTransmission, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (ClearanceHelpers.IsNonOperationalAck(pilotTransmission))
		{
			return null;
		}
		if (string.IsNullOrWhiteSpace(pilotTransmission))
		{
			return "Say again?";
		}
		PilotIntent pilotIntent = _intentParser.ParseIntent(pilotTransmission, _context);
		UpdateStateFromPilotTransmission(pilotTransmission);
		AtcContext atcContext = FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, pilotIntent);
		PhaseDefaults.ApplyPhaseDefaults(_context.CurrentPhase, atcContext);
		return await RouteToPhaseHandlerAsync(atcContext, pilotTransmission, pilotIntent, cancellationToken);
	}

	private async Task<string?> RouteToPhaseHandlerAsync(AtcContext atcContext, string pilotTransmission, PilotIntent pilotIntent, CancellationToken cancellationToken)
	{
		switch (_context.CurrentPhase)
		{
		case FlightPhase.Preflight_Clearance:
			if (_state == AtcState.ClearancePendingData)
			{
				if (string.IsNullOrWhiteSpace(_context.SquawkCode))
				{
					_context.SquawkCode = "4672";
					atcContext = FlightContextToAtcContextMapper.Map(_context, _ifrClearanceIssued, pilotIntent);
					PhaseDefaults.ApplyPhaseDefaults(_context.CurrentPhase, atcContext);
				}
				if (ClearanceHelpers.ClearanceDataComplete(atcContext))
				{
					return await HandleClearanceAsync(atcContext, "Pilot is waiting for IFR clearance.", cancellationToken);
				}
			}
			return await HandleClearanceAsync(atcContext, pilotTransmission, cancellationToken);
		case FlightPhase.Taxi_Out:
			return await PhaseHandlers.HandleTaxiOutPhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		case FlightPhase.Lineup_Takeoff:
			return await PhaseHandlers.HandleLineupTakeoffPhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		case FlightPhase.Climb_Departure:
			return await PhaseHandlers.HandleDepartureClimbPhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		case FlightPhase.Enroute:
			return await PhaseHandlers.HandleEnroutePhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		case FlightPhase.Descent_Arrival:
			return await PhaseHandlers.HandleArrivalPhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		case FlightPhase.Approach:
			return await PhaseHandlers.HandleApproachPhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		case FlightPhase.Landing:
			return await PhaseHandlers.HandleLandingPhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		case FlightPhase.Taxi_In:
			return await PhaseHandlers.HandleTaxiInPhase(pilotTransmission, atcContext, _context, _phraseEngine, cancellationToken);
		default:
			if (!HasContextChanged(atcContext))
			{
				return _lastAtcResponse;
			}
			return await CallLlmAsync(atcContext, pilotTransmission, cancellationToken);
		}
	}

	public async Task<string?> HandleClearanceAsync(AtcContext context, string pilotTransmission, CancellationToken ct = default(CancellationToken))
	{
		if (ClearanceHelpers.IsNonOperationalAck(pilotTransmission))
		{
			return null;
		}
		bool isIfrRequest = ClearanceHelpers.IsIfrRequest(pilotTransmission);
		switch (_state)
		{
		case AtcState.Idle:
		{
			if (!isIfrRequest)
			{
				string lower = pilotTransmission.ToLowerInvariant();
				if (lower.Contains("clearance") || lower.Contains("clearence") || lower.Contains("clearan"))
				{
					context.Permissions.AllowIfrClearance = false;
					context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
					return await CallLlmAsync(context, pilotTransmission, ct);
				}
				context.Permissions.AllowIfrClearance = false;
				context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
				return await CallLlmAsync(context, pilotTransmission, ct);
			}
			_state = AtcState.IfrRequested;
			if (string.IsNullOrWhiteSpace(_context.SquawkCode))
			{
				_context.SquawkCode = "4672";
			}
			string? logApi = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
			if (!string.IsNullOrWhiteSpace(logApi) && (logApi.Equals("1", StringComparison.OrdinalIgnoreCase) || logApi.Equals("true", StringComparison.OrdinalIgnoreCase) || logApi.Equals("yes", StringComparison.OrdinalIgnoreCase)))
			{
				Console.WriteLine("[DEBUG] FlightContext before mapping:");
				Console.WriteLine("  DestinationIcao: '" + _context.DestinationIcao + "'");
				Console.WriteLine("  SquawkCode: '" + _context.SquawkCode + "'");
				Console.WriteLine($"  ClearedAltitude: {_context.ClearedAltitude}");
				Console.WriteLine($"  CruiseFlightLevel: {_context.CruiseFlightLevel}");
			}
			context = FlightContextToAtcContextMapper.Map(pilotIntent: _intentParser.ParseIntent(pilotTransmission, _context), flightContext: _context, ifrClearanceIssued: _ifrClearanceIssued);
			if (!string.IsNullOrWhiteSpace(logApi) && (logApi.Equals("1", StringComparison.OrdinalIgnoreCase) || logApi.Equals("true", StringComparison.OrdinalIgnoreCase) || logApi.Equals("yes", StringComparison.OrdinalIgnoreCase)))
			{
				Console.WriteLine("[DEBUG] AtcContext after mapping:");
				Console.WriteLine("  ClearedTo: '" + context.ClearanceDecision.ClearedTo + "'");
				Console.WriteLine($"  InitialAltitudeFt: {context.ClearanceDecision.InitialAltitudeFt}");
				Console.WriteLine("  Squawk: '" + context.ClearanceDecision.Squawk + "'");
			}
			if (!ClearanceHelpers.ClearanceDataComplete(context))
			{
				context.Permissions.AllowIfrClearance = false;
				context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
				string atc2 = await CallLlmAsync(context, pilotTransmission, ct);
				_state = AtcState.ClearancePendingData;
				BeginLoadingSimbriefNavWeatherAsync(context);
				return atc2;
			}
			context.Permissions.AllowIfrClearance = true;
			context.ClearanceDecision.ClearanceType = "IFR_CLEARANCE";
			string atc3 = await CallLlmAsync(context, pilotTransmission, ct);
			_state = AtcState.ClearanceIssued;
			_ifrClearanceIssued = true;
			context.StateFlags.IfrClearanceIssued = true;
			return atc3;
		}
		case AtcState.ClearancePendingData:
			if (string.IsNullOrWhiteSpace(_context.SquawkCode))
			{
				_context.SquawkCode = "4672";
				context = FlightContextToAtcContextMapper.Map(pilotIntent: _intentParser.ParseIntent(pilotTransmission, _context), flightContext: _context, ifrClearanceIssued: _ifrClearanceIssued);
			}
			context = FlightContextToAtcContextMapper.Map(pilotIntent: _intentParser.ParseIntent(pilotTransmission, _context), flightContext: _context, ifrClearanceIssued: _ifrClearanceIssued);
			if (ClearanceHelpers.ClearanceDataComplete(context))
			{
				_state = AtcState.ClearanceReady;
				goto case AtcState.ClearanceReady;
			}
			if (isIfrRequest && !ClearanceHelpers.IsNonOperationalAck(pilotTransmission))
			{
				context.Permissions.AllowIfrClearance = false;
				context.ClearanceDecision.ClearanceType = "INFORMATION_ONLY";
				return await CallLlmAsync(context, pilotTransmission, ct);
			}
			return null;
		case AtcState.ClearanceReady:
		{
			context.Permissions.AllowIfrClearance = true;
			context.ClearanceDecision.ClearanceType = "IFR_CLEARANCE";
			string effectivePilotText = (string.IsNullOrWhiteSpace(pilotTransmission) ? "Pilot is waiting for IFR clearance." : pilotTransmission);
			string atc = await CallLlmAsync(context, effectivePilotText, ct);
			_state = AtcState.ClearanceIssued;
			_ifrClearanceIssued = true;
			context.StateFlags.IfrClearanceIssued = true;
			return atc;
		}
		case AtcState.ClearanceIssued:
			// Previously we swallowed readbacks here, which meant no reply.
			// Route the readback to the LLM so it can confirm/correct it.
			return await CallLlmAsync(context, pilotTransmission, ct);
		default:
			return null;
		}
	}

	private async Task<string> CallLlmAsync(AtcContext context, string pilotTransmission, CancellationToken cancellationToken)
	{
		try
		{
			_lastAtcResponse = (await _phraseEngine.GenerateAtcTransmissionAsync(context, pilotTransmission, cancellationToken)).Trim();
			_lastContext = context;
			return _lastAtcResponse;
		}
		catch (OperationCanceledException)
		{
			return "Standby, processing your request.";
		}
		catch (InvalidOperationException ex2)
		{
			InvalidOperationException ex3 = ex2;
			Console.Error.WriteLine("ERROR: " + ex3.Message);
			if (ex3.InnerException != null)
			{
				Console.Error.WriteLine("  Inner: " + ex3.InnerException.Message);
			}
			return "Standby, experiencing technical difficulties. Please check your .env file and API key.";
		}
		catch (Exception ex4)
		{
			Exception ex5 = ex4;
			Console.Error.WriteLine("ERROR: " + ex5.GetType().Name + ": " + ex5.Message);
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
			}, flightContext: _context, ifrClearanceIssued: _ifrClearanceIssued);
			if (ClearanceHelpers.ClearanceDataComplete(atcContext))
			{
				_state = AtcState.ClearanceReady;
				return await HandleClearanceAsync(atcContext, "Pilot is waiting for IFR clearance.", cancellationToken);
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
			_context.CurrentAtcUnit = AtcUnit.ClearanceDelivery;
		}
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
			_phraseEngine?.Dispose();
			_disposed = true;
		}
	}
}
