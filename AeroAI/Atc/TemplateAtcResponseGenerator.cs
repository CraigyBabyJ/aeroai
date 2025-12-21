using System;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Atc;

public sealed class TemplateAtcResponseGenerator : IAtcResponseGenerator
{
    private readonly Action<string>? _onDebug;

    public TemplateAtcResponseGenerator(Action<string>? onDebug = null)
    {
        _onDebug = onDebug;
    }

    public Task<AtcResponse> GenerateAsync(AtcRequest request, CancellationToken ct = default)
    {
        var role = request.ControllerRole ?? "unknown";
        var line = $"[ATC] provider=template role={role}";
        if (_onDebug != null)
        {
            _onDebug(line);
        }
        else
        {
            Console.WriteLine(line);
        }
        return Task.FromResult(new AtcResponse { SpokenText = "Standby." });
    }
}
