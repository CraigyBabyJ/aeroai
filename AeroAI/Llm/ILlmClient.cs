using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.Llm;

public interface ILlmClient
{
	Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default(CancellationToken));
}
