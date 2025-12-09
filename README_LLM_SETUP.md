# AeroAI LLM Setup Guide

## System Prompt

The system prompt is embedded in the `AeroAI.Modelfile`. It defines AeroAI as a professional ICAO-standard ATC controller with strict operational rules.

## Creating the Custom Ollama Model

1. **Save the Modelfile:**
   ```bash
   # The Modelfile is located at: AeroAI.Modelfile
   ```

2. **Create the custom model:**
   ```bash
   ollama create aeroai -f AeroAI.Modelfile
   ```

3. **Test the model:**
   ```bash
   ollama run aeroai
   ```

4. **Update your C# code to use the custom model:**
   ```csharp
   var llm = new OllamaLlmClient(
       baseUrl: "http://192.168.1.100:11434",
       model: "aeroai"  // Use the custom model name
   );
   ```

## Prompt Template

The prompt template is implemented in `AeroAI/Llm/PromptTemplate.cs`. It builds the dynamic context that gets sent to the LLM on each call.

The template includes:
- Current flight phase
- Callsign, departure/arrival airports
- Selected runways, SID/STAR/Approach
- Current clearances (altitude, heading, squawk)
- Weather information
- Navdata (runway headings)
- Sim state (current altitude)
- Pilot transmission

## Usage Example

```csharp
using AeroAI.Atc;
using AeroAI.Llm;
using AeroAI.Models;

// Create LLM client with custom model
var llm = new OllamaLlmClient(
    baseUrl: "http://192.168.1.100:11434",
    model: "aeroai"  // Your custom model
);

// Create flight context
var context = new FlightContext
{
    CurrentPhase = FlightPhase.ClearanceDelivery,
    Callsign = "AeroAI123",
    OriginIcao = "EDDM",
    DestinationIcao = "LOWI",
    SelectedDepartureRunway = "26R",
    CruiseFlightLevel = 330,
    SquawkCode = "4672"
};

// Create ATC session
var session = new AeroAiLlmSession(llm, context);

// Handle pilot transmission
string atcResponse = await session.HandlePilotTransmissionAsync(
    "Munich Clearance, AeroAI one two three, IFR to Innsbruck, ready to copy."
);

Console.WriteLine($"ATC: {atcResponse}");
```

## Model Parameters

The Modelfile includes optimized parameters for low-latency ATC responses:
- `temperature 0.3` - Lower creativity, more consistent responses
- `top_p 0.9` - Focused token selection
- `num_predict 150` - Limit response length (typically 1-2 sentences)
- `stop "ATC:"` - Stop generation at ATC label
- `stop "\n\n"` - Stop at double newline (prevents multi-paragraph responses)

## Troubleshooting

If the model doesn't respond correctly:
1. Verify Ollama is running: `ollama list`
2. Check model exists: `ollama show aeroai`
3. Test with: `ollama run aeroai "Test prompt"`
4. Check logs: `ollama logs`

If responses are too verbose:
- Lower `temperature` in Modelfile (try 0.2)
- Reduce `num_predict` (try 100)

If responses are too short:
- Increase `num_predict` (try 200)
- Raise `temperature` slightly (try 0.4)

