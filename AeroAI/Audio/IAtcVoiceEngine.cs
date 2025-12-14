using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Audio;

public interface IAtcVoiceEngine
{
    Task SpeakAsync(string text, AtcNavDataDemo.Config.VoiceProfile? profile = null, CancellationToken cancellationToken = default);
}
