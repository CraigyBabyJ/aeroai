using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Atc;

public interface IAtcResponseGenerator
{
    Task<AtcResponse> GenerateAsync(AtcRequest request, CancellationToken ct = default);
}

public sealed class AtcRequest
{
    public string TranscriptText { get; init; } = string.Empty;
    public string? ControllerRole { get; init; }
    public FlightContext? FlightContext { get; init; }
    public object? SessionState { get; init; }
    public AtcContext? AtcContext { get; init; }
}

public sealed class AtcResponse
{
    public string SpokenText { get; init; } = string.Empty;
    public AtcHandoffSuggestion? Handoff { get; init; }
    public string? IntentTag { get; init; }
}

public sealed class AtcHandoffSuggestion
{
    public string Role { get; init; } = string.Empty;
    public double? FrequencyMhz { get; init; }
}
