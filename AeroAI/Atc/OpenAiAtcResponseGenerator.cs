using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Config;
using AeroAI.Data;
using AeroAI.Llm;
using AeroAI.AtcSession;

namespace AeroAI.Atc;

public sealed class OpenAiAtcResponseGenerator : IAtcResponseGenerator, IDisposable
{
    private readonly OpenAiLlmClient _llmClient;
    private readonly string _systemPrompt;
    private readonly string _model;
    private readonly Action<string>? _onDebug;
    private bool _disposed;

    public OpenAiAtcResponseGenerator(
        string apiKey,
        string? model = null,
        string? baseUrl = null,
        string? systemPromptPath = null,
        Action<string>? onDebug = null)
    {
        _model = model ?? EnvironmentConfig.GetOpenAiModel();
        _onDebug = onDebug;
        _llmClient = new OpenAiLlmClient(apiKey, _model, baseUrl, onDebug);
        _systemPrompt = LoadSystemPrompt(systemPromptPath);
    }

    public async Task<AtcResponse> GenerateAsync(AtcRequest request, CancellationToken ct = default)
    {
        try
        {
            var atcContext = request.AtcContext
                ?? request.SessionState as AtcContext
                ?? throw new InvalidOperationException("ATC provider openai requires AtcContext.");

            var pilotTransmission = request.TranscriptText;
            if (string.IsNullOrWhiteSpace(pilotTransmission))
            {
                return new AtcResponse { SpokenText = "Say again?" };
            }

            string userPrompt = BuildUserPrompt(atcContext, pilotTransmission, request.SessionState);
            LogDebugPrompt(_systemPrompt, userPrompt);
            if (ShouldLogApiRequests())
            {
                LogApiRequest(_systemPrompt, userPrompt);
            }
            string response = await _llmClient.GenerateChatCompletionAsync(_systemPrompt, userPrompt, ct);
            LogDebugResponse(response);
            if (ShouldLogApiRequests())
            {
                LogApiResponse(response);
            }

            string trimmed = response.Trim();
            if (request.FlightContext != null)
            {
                var validation = AtcResponseValidator.Validate(trimmed, atcContext, request.FlightContext);
                if (!validation.IsValid)
                {
                    LogValidationFailure(trimmed, validation);
                    trimmed = BuildSafeFallback(atcContext, request.FlightContext);
                }
            }

            var role = request.ControllerRole ?? atcContext.ControllerRole ?? "unknown";
            var line = $"[ATC] provider=openai model={_model} role={role}";
            if (_onDebug != null)
            {
                _onDebug(line);
            }
            else
            {
                Console.WriteLine(line);
            }

            return new AtcResponse { SpokenText = trimmed };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"ATC provider openai failed: {ex.Message}", ex);
        }
    }

    private static bool ShouldLogApiRequests()
    {
        string? environmentVariable = Environment.GetEnvironmentVariable("AEROAI_LOG_API");
        return !string.IsNullOrWhiteSpace(environmentVariable)
               && (environmentVariable.Equals("1", StringComparison.OrdinalIgnoreCase)
                   || environmentVariable.Equals("true", StringComparison.OrdinalIgnoreCase)
                   || environmentVariable.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static void LogApiRequest(string systemPrompt, string userPrompt)
    {
        string value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("══════════════════════════════════════════════════════════");
        StringBuilder stringBuilder2 = stringBuilder;
        StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
        handler.AppendLiteral("API REQUEST [");
        handler.AppendFormatted(value);
        handler.AppendLiteral("]");
        stringBuilder2.AppendLine(ref handler);
        stringBuilder.AppendLine("══════════════════════════════════════════════════════════");
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("SYSTEM PROMPT:");
        stringBuilder.AppendLine("──────────────────────────────────────────────────────────");
        stringBuilder.AppendLine(systemPrompt);
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("USER PROMPT:");
        stringBuilder.AppendLine("──────────────────────────────────────────────────────────");
        stringBuilder.AppendLine(userPrompt);
        stringBuilder.AppendLine("══════════════════════════════════════════════════════════");
        stringBuilder.AppendLine();
        string text = stringBuilder.ToString();
        Console.Write(text);
        WriteToLogFile(text);
    }

    private static void LogApiResponse(string response)
    {
        string value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        StringBuilder stringBuilder = new StringBuilder();
        StringBuilder stringBuilder2 = stringBuilder;
        StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
        handler.AppendLiteral("API RESPONSE [");
        handler.AppendFormatted(value);
        handler.AppendLiteral("]:");
        stringBuilder2.AppendLine(ref handler);
        stringBuilder.AppendLine("──────────────────────────────────────────────────────────");
        stringBuilder.AppendLine(response);
        stringBuilder.AppendLine("══════════════════════════════════════════════════════════");
        stringBuilder.AppendLine();
        string text = stringBuilder.ToString();
        Console.Write(text);
        WriteToLogFile(text);
    }

    private static void WriteToLogFile(string logText)
    {
        try
        {
            string? environmentVariable = Environment.GetEnvironmentVariable("AEROAI_LOG_FILE");
            if (!string.IsNullOrWhiteSpace(environmentVariable))
            {
                File.AppendAllText(environmentVariable, logText);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[Log file write error: " + ex.Message + "]");
        }
    }

    private static string BuildUserPrompt(AtcContext context, string pilotTransmission, object? sessionState)
    {
        if (sessionState is AtcPromptData promptData)
        {
            var builder = new AtcPromptBuilder();
            return builder.BuildUserPrompt(context, promptData, pilotTransmission);
        }

        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.AppendLine("CONTEXT_JSON:");
        stringBuilder.AppendLine("```json");
        stringBuilder.AppendLine(context.ToJson());
        stringBuilder.AppendLine("```");
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("PILOT_TRANSMISSION:");
        StringBuilder stringBuilder2 = stringBuilder;
        StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, stringBuilder2);
        handler.AppendLiteral("\"");
        handler.AppendFormatted(pilotTransmission);
        handler.AppendLiteral("\"");
        stringBuilder2.AppendLine(ref handler);
        stringBuilder.AppendLine();
        stringBuilder.AppendLine("Using ONLY this information and obeying your role and permissions, respond with a single ICAO-style ATC transmission.");
        return stringBuilder.ToString();
    }

    private static string LoadSystemPrompt(string? path)
    {
        string text = path ?? "prompts/aeroai_system_prompt.txt";
        if (!File.Exists(text))
        {
            throw new FileNotFoundException("System prompt file not found: " + text + ". Please ensure the file exists or provide a valid path via AEROAI_SYSTEM_PROMPT_PATH.");
        }
        return File.ReadAllText(text);
    }

    private static void LogDebugPrompt(string systemPrompt, string userPrompt)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== ATC DEBUG PROMPT ===");
        sb.AppendLine("SYSTEM PROMPT:");
        sb.AppendLine(systemPrompt);
        sb.AppendLine("USER PROMPT:");
        sb.AppendLine(userPrompt);
        sb.AppendLine("=== END ATC DEBUG PROMPT ===");
        string log = sb.ToString();
        Console.Write(log);
        WriteToLogFile(log);
    }

    private static void LogDebugResponse(string response)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== ATC DEBUG RESPONSE ===");
        sb.AppendLine(response);
        sb.AppendLine("=== END ATC DEBUG RESPONSE ===");
        string log = sb.ToString();
        Console.Write(log);
        WriteToLogFile(log);
    }

    private static void LogValidationFailure(string response, AtcResponseValidationResult validation)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[ATC RESPONSE VALIDATION] LLM response rejected; using deterministic fallback.");
        if (validation.Reasons.Count > 0)
        {
            sb.AppendLine("Reasons: " + string.Join("; ", validation.Reasons));
        }
        if (validation.OffendingTokens.Count > 0)
        {
            sb.AppendLine("Tokens: " + string.Join(", ", validation.OffendingTokens));
        }
        sb.AppendLine("Response:");
        sb.AppendLine(response);

        var log = sb.ToString();
        Console.Write(log);
        WriteToLogFile(log);
    }

    private string BuildSafeFallback(AtcContext context, FlightContext flightContext)
    {
        var callsign = ResolveCallsign(flightContext, context);
        var phase = context?.Phase?.ToUpperInvariant() ?? string.Empty;
        var clearanceType = context?.ClearanceDecision?.ClearanceType ?? string.Empty;

        if (phase == "CLEARANCE" || clearanceType == "IFR_CLEARANCE")
        {
            if (ClearanceHelpers.ClearanceDataComplete(context))
            {
                return BuildDeterministicClearance(context, flightContext, callsign);
            }
            return $"{callsign}, standby for clearance.";
        }

        return phase switch
        {
            "TAXI_OUT" or "TAXI_IN" => $"{callsign}, say again for taxi.",
            "LINEUP" or "FINAL" => $"{callsign}, standby.",
            "CLIMB" or "ENROUTE" or "DESCENT" or "APPROACH" => $"{callsign}, say again.",
            _ => $"{callsign}, say again."
        };
    }

    private static string ResolveCallsign(FlightContext flightContext, AtcContext context)
    {
        if (!string.IsNullOrWhiteSpace(flightContext.RadioCallsign))
            return flightContext.RadioCallsign;
        if (!string.IsNullOrWhiteSpace(flightContext.Callsign))
            return flightContext.Callsign;
        if (!string.IsNullOrWhiteSpace(context?.FlightInfo?.Callsign))
            return context.FlightInfo.Callsign;
        return "Aircraft";
    }

    private static string BuildDeterministicClearance(AtcContext context, FlightContext flightContext, string callsign)
    {
        var resolvedDestination = AirportNameResolver.ResolveAirportName(flightContext.DestinationIcao, flightContext);
        string clearedTo = context.ClearanceDecision.ClearedTo
                           ?? (!string.IsNullOrWhiteSpace(resolvedDestination)
                               ? resolvedDestination
                               : (!string.IsNullOrWhiteSpace(flightContext.DestinationName)
                                   ? flightContext.DestinationName
                                   : flightContext.DestinationIcao ?? "destination"));
        if (!string.IsNullOrWhiteSpace(flightContext.DestinationIcao)
            && string.Equals(clearedTo, flightContext.DestinationIcao, StringComparison.OrdinalIgnoreCase))
        {
            clearedTo = flightContext.DestinationName ?? "destination airport";
        }

        string depRunway = context.ClearanceDecision.DepRunway
                           ?? flightContext.SelectedDepartureRunway
                           ?? flightContext.DepartureRunway?.RunwayIdentifier
                           ?? "runway";

        string sid = !string.IsNullOrWhiteSpace(context.ClearanceDecision.Sid)
            ? context.ClearanceDecision.Sid
            : (!string.IsNullOrWhiteSpace(flightContext.SelectedSID) ? flightContext.SelectedSID : "radar vectors");

        string initialClimb = context.ClearanceDecision.InitialAltitudeFt.HasValue
            ? $"{context.ClearanceDecision.InitialAltitudeFt.Value} feet"
            : "initial altitude";

        string squawk = context.ClearanceDecision.Squawk ?? flightContext.SquawkCode ?? "XXXX";

        string expectFl = !string.IsNullOrWhiteSpace(context.FlightInfo?.CruiseLevel)
            ? context.FlightInfo.CruiseLevel!
            : (flightContext.CruiseFlightLevel > 0 ? $"FL{flightContext.CruiseFlightLevel}" : "cruise flight level");

        return $"{callsign}, cleared to {clearedTo} via {sid} departure, then as filed. Departure runway {depRunway}, initial climb {initialClimb}, squawk {squawk}, expect {expectFl} ten minutes after departure.";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _llmClient.Dispose();
        _disposed = true;
    }
}
