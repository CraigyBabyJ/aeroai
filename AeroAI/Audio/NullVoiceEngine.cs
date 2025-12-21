using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Audio;

public sealed class NullVoiceEngine : IAtcVoiceEngine
{
    public Task SpeakAsync(string text, string? role = null, string? facilityIcao = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
