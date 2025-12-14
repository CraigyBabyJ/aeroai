using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Audio;

public sealed class NullVoiceEngine : IAtcVoiceEngine
{
    public Task SpeakAsync(string text, AtcNavDataDemo.Config.VoiceProfile? profile = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
