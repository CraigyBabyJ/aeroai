using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.UI.Services;

/// <summary>
/// Routes STT requests to the configured backend, with automatic fallback to whisper-cli.
/// </summary>
internal sealed class SttBackendRouter : ISttService, IDisposable
{
    private readonly ISttService _whisperCli;
    private readonly ISttService? _whisperFast;
    private readonly Action<string>? _log;

    private readonly string _backend;

    public bool IsAvailable => _backend == "whisper" ? _whisperCli.IsAvailable : (_whisperFast?.IsAvailable ?? false) || _whisperCli.IsAvailable;

    public SttBackendRouter(ISttService whisperCli, ISttService? whisperFast, string backend, Action<string>? log = null)
    {
        _whisperCli = whisperCli;
        _whisperFast = whisperFast;
        _backend = backend;
        _log = log;
    }

    public async Task<string?> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        // Prefer configured backend; fall back to whisper-cli on any failure.
        if (_backend == "whisper-fast" && _whisperFast != null)
        {
            try
            {
                _log?.Invoke("[STT] backend=whisper-fast");
                var text = await _whisperFast.TranscribeAsync(wavPath, cancellationToken).ConfigureAwait(false);
                return text;
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[STT] fallback to whisper-cli: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _log?.Invoke("[STT] backend=whisper");
        return await _whisperCli.TranscribeAsync(wavPath, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        (_whisperFast as IDisposable)?.Dispose();
        (_whisperCli as IDisposable)?.Dispose();
    }
}
