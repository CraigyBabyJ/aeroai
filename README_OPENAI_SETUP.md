# OpenAI Integration Setup

AeroAI ships with an OpenAI ATC response generator; `.env` is loaded via `EnvironmentConfig.Load()` at startup.

## Quick start
1) Copy `.env.example` to `.env`.
2) Set `OPENAI_API_KEY=...` and optionally `OPENAI_MODEL` (default is the fine-tuned id in `EnvironmentConfig`). `OPENAI_BASE_URL` is optional; the client always uses `https://api.openai.com/v1` to avoid 404s.
3) Load env before constructing the client:
   ```csharp
   EnvironmentConfig.Load();
   ```
4) Use the ATC response generator:
   ```csharp
   EnvironmentConfig.Load();
   var generator = new OpenAiAtcResponseGenerator(
       EnvironmentConfig.GetOpenAiApiKey(),
       EnvironmentConfig.GetOpenAiModel(),
       EnvironmentConfig.GetOpenAiBaseUrl(),
       EnvironmentConfig.GetSystemPromptPath());
   var session = new AeroAiLlmSession(generator, flightContext);
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
- `AeroAI/Atc/OpenAiAtcResponseGenerator.cs` – OpenAI-backed ATC response generator (prompt build + validation + logging).
- `AeroAI/Llm/AeroAiPhraseEngine.cs` – legacy wrapper around the OpenAI generator for direct prompt usage.
- `prompts/aeroai_system_prompt.txt` and `prompts/aeroai_finetuned_prompt.txt` – system prompts (fine-tuned prompt is the default path).

## Switching providers
Swap between OpenAI and a stub generator via `IAtcResponseGenerator`:
```csharp
IAtcResponseGenerator generator = new OpenAiAtcResponseGenerator(apiKey, "gpt-4o-mini");
generator = new TemplateAtcResponseGenerator();
```
Select in the desktop UI via `userconfig.json` (`AtcTextProvider`: `openai` or `template`).

## Troubleshooting
- 404s: base URL is hardcoded to `https://api.openai.com/v1/`; 404s typically indicate proxy/VPN issues (see `TROUBLESHOOTING_404.md`).
- Key format: must start with `sk-` or `sk-proj-` (enforced by `EnvironmentConfig.GetOpenAiApiKey()`).
