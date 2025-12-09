# AeroAI ATC Phrase Engine

This document describes the new structured ATC phrase engine that uses OpenAI to generate realistic ATC transmissions from structured context.

## Architecture

The phrase engine follows a clean separation of concerns:

1. **`AtcContext`** - Structured data model matching the JSON schema sent to OpenAI
2. **`AeroAiPhraseEngine`** - Main entry point that takes `AtcContext` + pilot transmission
3. **`OpenAiLlmClient`** - Low-level OpenAI API client (supports Chat Completions)
4. **`EnvironmentConfig`** - Loads configuration from `.env` file

## Quick Start

### 1. Create `.env` file

```bash
OPENAI_API_KEY=sk-your-key-here
OPENAI_MODEL=gpt-4o-mini
OPENAI_BASE_URL=https://api.openai.com/v1
AEROAI_SYSTEM_PROMPT_PATH=prompts/aeroai_system_prompt.txt
```

### 2. Run the demo

```bash
dotnet run --project AeroAI -- DemoClearance
```

Or in code:

```csharp
using AeroAI.Atc;
using AeroAI.Config;
using AeroAI.Llm;

// Load .env
EnvironmentConfig.Load();

// Create phrase engine
using var engine = new AeroAiPhraseEngine(
    apiKey: EnvironmentConfig.GetOpenAiApiKey(),
    model: EnvironmentConfig.GetOpenAiModel(),
    systemPromptPath: EnvironmentConfig.GetSystemPromptPath()
);

// Build context
var context = new AtcContext
{
    ControllerRole = "CLEARANCE",
    Phase = "CLEARANCE",
    FlightInfo = new FlightInfo
    {
        Callsign = "CJ",
        AircraftType = "B738",
        DepIcao = "EGCC",
        DepName = "Manchester",
        ArrIcao = "GMMN",
        ArrName = "Casablanca",
        CruiseLevel = "FL350"
    },
    ClearanceDecision = new ClearanceDecision
    {
        ClearanceType = "IFR_CLEARANCE",
        ClearedTo = "Casablanca",
        RouteSummary = "as filed",
        DepRunway = "23R",
        Sid = "MAN1A",
        InitialAltitudeFt = 5000,
        Squawk = "4672"
    },
    // ... set other properties
};

// Generate ATC response
var atcResponse = await engine.GenerateAtcTransmissionAsync(
    context: context,
    pilotTransmission: "Good evening Clearance, this is CJ requesting IFR clearance to Casablanca as filed"
);

Console.WriteLine($"ATC → {atcResponse}");
```

## Data Flow

```
SimBrief/Nav/Weather/Sim State
    ↓
AtcContext (structured JSON)
    ↓
AeroAiPhraseEngine
    ↓
OpenAiLlmClient (Chat Completions API)
    ↓
OpenAI GPT-4o-mini
    ↓
ATC Transmission (plain text)
```

## Integration with Existing Code

The phrase engine is designed to work alongside your existing `FlightContext` and decision logic:

1. Your existing code (SimBrief, nav data, weather, runway selection, etc.) builds decisions
2. Convert `FlightContext` → `AtcContext` (mapping layer)
3. Call `AeroAiPhraseEngine.GenerateAtcTransmissionAsync()`
4. Return the ATC transmission to the pilot

### Example Mapping

```csharp
public static AtcContext MapFromFlightContext(FlightContext flightContext, string pilotTransmission)
{
    return new AtcContext
    {
        ControllerRole = flightContext.CurrentAtcUnit.ToString().ToUpper(),
        Phase = flightContext.CurrentPhase.ToString().ToUpper(),
        FlightInfo = new FlightInfo
        {
            Callsign = flightContext.Callsign,
            AircraftType = flightContext.Aircraft?.IcaoType,
            DepIcao = flightContext.OriginIcao,
            ArrIcao = flightContext.DestinationIcao,
            CruiseLevel = $"FL{flightContext.CruiseFlightLevel}"
        },
        ClearanceDecision = new ClearanceDecision
        {
            DepRunway = flightContext.SelectedDepartureRunway,
            ArrRunway = flightContext.SelectedArrivalRunway,
            Sid = flightContext.SelectedSID,
            Star = flightContext.SelectedSTAR,
            Approach = flightContext.SelectedApproachName,
            InitialAltitudeFt = flightContext.ClearedAltitude,
            Squawk = flightContext.SquawkCode
        },
        // ... map other fields
    };
}
```

## Files

- **`AeroAI/Atc/AtcContext.cs`** - Structured context model
- **`AeroAI/Llm/AeroAiPhraseEngine.cs`** - Main phrase engine
- **`AeroAI/Llm/OpenAiLlmClient.cs`** - OpenAI API client (updated for Chat Completions)
- **`AeroAI/Config/EnvironmentConfig.cs`** - .env file loader
- **`AeroAI/Examples/DemoClearance.cs`** - Example usage
- **`prompts/aeroai_system_prompt.txt`** - System prompt for OpenAI

## Next Steps

1. Wire in real SimBrief data → `AtcContext.FlightInfo`
2. Wire in nav/PMDG data → `AtcContext.ClearanceDecision` (SID/STAR/approach)
3. Wire in weather → `AtcContext.WeatherRelevant`
4. Wire in sim state → `AtcContext.StateFlags` and `Permissions`
5. Create mapping helper: `FlightContext` → `AtcContext`

The phrase engine is ready to use - just provide it with structured context!

