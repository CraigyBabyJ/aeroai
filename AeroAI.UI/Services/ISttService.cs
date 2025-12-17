using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.UI.Services;

public interface ISttService
{
    bool IsAvailable { get; }
    Task<string?> TranscribeAsync(string wavPath, CancellationToken cancellationToken);
}
