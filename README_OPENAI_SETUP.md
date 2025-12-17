# OpenAI Integration Setup

AeroAI ships with an OpenAI client and phrase engine; `.env` is loaded via `EnvironmentConfig.Load()` at startup.

## Quick start
1) Copy `.env.example` to `.env`.
2) Set `OPENAI_API_KEY=...` and optionally `OPENAI_MODEL` (default is the fine-tuned id in `EnvironmentConfig`). `OPENAI_BASE_URL` is optional; the client always uses `https://api.openai.com/v1` to avoid 404s.
3) Load env before constructing the client:
   ```csharp
   EnvironmentConfig.Load();
   ```
4) Use the client/phrase engine:
   ```csharp
   EnvironmentConfig.Load();
   using var llm = new OpenAiLlmClient(EnvironmentConfig.GetOpenAiApiKey(), EnvironmentConfig.GetOpenAiModel());
   var session = new AeroAiLlmSession(llm, flightContext);
   var reply = await session.HandlePilotTransmissionAsync("Requesting IFR clearance to Innsbruck.");
   ```
   or
   ```csharp
   EnvironmentConfig.Load();
   var engine = new AeroAiPhraseEngine(
       apiKey: EnvironmentConfig.GetOpenAiApiKey(),
       model: EnvironmentConfig.GetOpenAiModel(),
       systemPromptPath: EnvironmentConfig.GetSystemPromptPath());
   var text = await engine.GenerateAtcTransmissionAsync(atcContext, pilotText, flightContext);
   ```

## Files that matter
- `.env` – holds `OPENAI_API_KEY`, `OPENAI_MODEL`, and optional `AEROAI_SYSTEM_PROMPT_PATH` (defaults to `prompts/aeroai_finetuned_prompt.txt`).
- `AeroAI/Llm/OpenAiLlmClient.cs` – chat/completions client; base URL is hardcoded to prevent double `/v1` errors.
- `AeroAI/Llm/AeroAiPhraseEngine.cs` – builds prompts, logs debug blocks, and runs `AtcResponseValidator` against replies.
- `prompts/aeroai_system_prompt.txt` and `prompts/aeroai_finetuned_prompt.txt` – system prompts (fine-tuned prompt is the default path).

## Switching models
Swap between OpenAI and Ollama via the `ILlmClient` interface:
```csharp
ILlmClient llm = new OpenAiLlmClient(apiKey, "gpt-4o-mini");
llm = new OllamaLlmClient("http://localhost:11434", "aeroai");
```
`AeroAiLlmSession` or `AeroAiPhraseEngine` work with either client.

## Troubleshooting
- 404s: base URL is hardcoded to `https://api.openai.com/v1/`; 404s typically indicate proxy/VPN issues (see `TROUBLESHOOTING_404.md`).
- Key format: must start with `sk-` or `sk-proj-` (enforced by `EnvironmentConfig.GetOpenAiApiKey()`).

