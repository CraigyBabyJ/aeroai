using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Audio;

public interface IAtcVoiceEngine
{
    Task SpeakAsync(string text, string? role = null, string? facilityIcao = null, CancellationToken cancellationToken = default);
}
