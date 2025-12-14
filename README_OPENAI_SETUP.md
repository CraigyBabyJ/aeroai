# OpenAI Integration Setup

AeroAI now supports OpenAI models (GPT-4o-mini, GPT-4, etc.) in addition to local Ollama models.

## Quick Start

1. **Create `.env` file** (copy from `.env.template`):
   ```bash
   cp .env.template .env
   ```

2. **Edit `.env`** and add your OpenAI API key:
   ```
   OPENAI_API_KEY=sk-your-key-here
   OPENAI_MODEL=gpt-4o-mini
   ```

3. **Load environment variables** in your app:
   ```csharp
   var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
       ?? throw new InvalidOperationException("OPENAI_API_KEY not set");
   var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4o-mini";
   ```

4. **Use OpenAI client**:
   ```csharp
   var llm = new OpenAiLlmClient(apiKey, model);
   var session = new AeroAiLlmSession(llm, flightContext);
   var atcResponse = await session.HandlePilotTransmissionAsync("...");
   ```

## Files Created

- **`.env.template`**: Template for environment variables (don't commit `.env` to Git)
- **`prompts/aeroai_system_prompt.txt`**: System prompt for OpenAI
- **`prompts/aeroai_user_prompt_template.txt`**: User prompt template (reference)
- **`AeroAI/Llm/OpenAiLlmClient.cs`**: OpenAI API client implementation
- **`AeroAI/Llm/OpenAiPromptTemplate.cs`**: Prompt builder for OpenAI format
- **`AeroAI/Examples/OpenAiAtcDemo.cs`**: Example usage

## Switching Between Ollama and OpenAI

The `AeroAiLlmSession` automatically detects which client you're using and selects the appropriate prompt template:

```csharp
// Use Ollama (local)
var ollama = new OllamaLlmClient("http://192.168.1.100:11434", "aeroai");
var session1 = new AeroAiLlmSession(ollama, context);

// Use OpenAI (cloud)
var openai = new OpenAiLlmClient(apiKey, "gpt-4o-mini");
var session2 = new AeroAiLlmSession(openai, context);
```

Both use the same `ILlmClient` interface, so switching is seamless.

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `OPENAI_API_KEY` | Your OpenAI API key (required) | - |
| `OPENAI_MODEL` | Model to use | `gpt-4o-mini` |
| `OPENAI_BASE_URL` | API base URL | `https://api.openai.com/v1` |
| `AEROAI_SYSTEM_PROMPT_PATH` | Path to system prompt file | `prompts/aeroai_system_prompt.txt` |

## Cost Considerations

- **GPT-4o-mini**: ~$0.15 per 1M input tokens, ~$0.60 per 1M output tokens
- Typical ATC response: ~50-100 tokens
- Estimated cost per interaction: **$0.0001 - $0.0002** (very cheap)

For high-volume testing, consider using Ollama with a local model to avoid API costs.

