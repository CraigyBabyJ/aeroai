using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.UI.Services;

/// <summary>
/// Routes STT requests to the configured backend.
/// </summary>
internal sealed class SttBackendRouter : ISttService, IDisposable
{
    private readonly ISttService _whisperCli;
    private readonly ISttService? _whisperFast;
    private readonly Action<string>? _log;

    private readonly string _backend;

    public bool IsAvailable => _backend == "whisper"
        ? _whisperCli.IsAvailable
        : (_whisperFast?.IsAvailable ?? false);

    public SttBackendRouter(ISttService whisperCli, ISttService? whisperFast, string backend, Action<string>? log = null)
    {
        _whisperCli = whisperCli;
        _whisperFast = whisperFast;
        _backend = backend;
        _log = log;
    }

    public async Task<string?> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        // Prefer configured backend.
        if (_backend == "whisper-fast")
        {
            if (_whisperFast == null)
            {
                _log?.Invoke("[STT] whisper-fast unavailable (host not configured).");
                throw new InvalidOperationException("whisper-fast not available.");
            }

            try
            {
                _log?.Invoke("[STT] backend=whisper-fast");
                var text = await _whisperFast.TranscribeAsync(wavPath, cancellationToken).ConfigureAwait(false);
                return text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[STT] whisper-fast failed: {ex.GetType().Name}: {ex.Message}");
                throw new InvalidOperationException($"whisper-fast failed: {ex.Message}");
            }
        }

        if (!_whisperCli.IsAvailable)
            throw new InvalidOperationException("whisper-cli not available.");

        _log?.Invoke("[STT] backend=whisper");
        return await _whisperCli.TranscribeAsync(wavPath, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        (_whisperFast as IDisposable)?.Dispose();
        (_whisperCli as IDisposable)?.Dispose();
    }
}
