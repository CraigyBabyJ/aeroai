using System;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;

namespace AeroAI.Llm;

public sealed class AeroAiPhraseEngine : IDisposable
{
    private readonly OpenAiAtcResponseGenerator _generator;
    private bool _disposed;

    public AeroAiPhraseEngine(
        string apiKey,
        string? model = null,
        string? baseUrl = null,
        string? systemPromptPath = null,
        Action<string>? onDebug = null)
    {
        _generator = new OpenAiAtcResponseGenerator(apiKey, model, baseUrl, systemPromptPath, onDebug);
    }

    public Task<string> GenerateAtcTransmissionAsync(
        AtcContext context,
        string pilotTransmission,
        CancellationToken cancellationToken = default)
    {
        return GenerateAtcTransmissionAsync(context, pilotTransmission, null, cancellationToken);
    }

    public async Task<string> GenerateAtcTransmissionAsync(
        AtcContext context,
        string pilotTransmission,
        FlightContext? flightContext,
        CancellationToken cancellationToken = default)
    {
        var request = new AtcRequest
        {
            TranscriptText = pilotTransmission,
            ControllerRole = context?.ControllerRole,
            FlightContext = flightContext,
            AtcContext = context
        };
        var response = await _generator.GenerateAsync(request, cancellationToken);
        return response.SpokenText;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _generator.Dispose();
        _disposed = true;
    }
}
