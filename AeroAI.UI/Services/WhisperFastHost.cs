using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AeroAI.UI.Services;

/// <summary>
/// Manages the lifetime of the local whisper-fast Python HTTP service.
/// </summary>
internal sealed class WhisperFastHost : IDisposable
{
    private readonly string _pythonPath;
    private readonly string _serverPath;
    private readonly string _modelName;
    public int Port { get; }
    private readonly Action<string>? _log;
    private Process? _process;
    private readonly HttpClient _httpClient = new();

    public WhisperFastHost(string pythonPath, string serverPath, string modelName, int port, Action<string>? log = null)
    {
        _pythonPath = pythonPath;
        _serverPath = serverPath;
        _modelName = modelName;
        Port = port;
        _log = log;
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
            return true;

        Stop();

        _log?.Invoke($"[STT] whisper-fast launching: python=\"{_pythonPath}\", script=\"{_serverPath}\", model=\"{_modelName}\", port={Port}");

        var psi = new ProcessStartInfo
        {
            FileName = _pythonPath,
            WorkingDirectory = Path.GetDirectoryName(_serverPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(_serverPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(Port.ToString());
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(_modelName);

        try
        {
            _process = Process.Start(psi);
            if (_process == null)
            {
                _log?.Invoke("[STT] failed to start whisper-fast process (null)");
                return false;
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[STT] failed to start whisper-fast: {ex.Message}");
            _process = null;
            return false;
        }

        var healthUrl = $"http://127.0.0.1:{Port}/health";
        for (int i = 0; i < 20 && !cancellationToken.IsCancellationRequested; i++)
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            if (_process.HasExited)
            {
                _log?.Invoke($"[STT] whisper-fast exited during startup (exit {_process.ExitCode})");
                return false;
            }

            try
            {
                var resp = await _httpClient.GetAsync(healthUrl, cancellationToken).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("ok", out var okElem) && okElem.ValueKind == System.Text.Json.JsonValueKind.True)
                        {
                            _log?.Invoke("[STT] whisper-fast started");
                            return true;
                        }
                    }
                    catch
                    {
                        // ignore parse failure; retry
                    }
                }
            }
            catch
            {
                // keep retrying
            }
        }

        _log?.Invoke("[STT] whisper-fast healthcheck failed");
        return false;
    }

    public void Stop()
    {
        if (_process == null)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
        finally
        {
            _process?.Dispose();
            _process = null;
        }
    }

    public void Dispose()
    {
        Stop();
        _httpClient.Dispose();
    }
}
