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
        psi.Environment["PYTHONUNBUFFERED"] = "1";
        AppendWhisperFastDllPaths(psi);
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add(_serverPath);
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(Port.ToString());
        psi.ArgumentList.Add("--model");
        psi.ArgumentList.Add(_modelName);

        var device = Environment.GetEnvironmentVariable("WHISPER_FAST_DEVICE");
        if (!string.IsNullOrWhiteSpace(device))
        {
            psi.ArgumentList.Add("--device");
            psi.ArgumentList.Add(device.Trim());
            if (string.Equals(device.Trim(), "cpu", StringComparison.OrdinalIgnoreCase))
            {
                psi.Environment["CTRANSLATE2_FORCE_CPU"] = "1";
                psi.Environment["CUDA_VISIBLE_DEVICES"] = "";
            }
        }

        var computeType = Environment.GetEnvironmentVariable("WHISPER_FAST_COMPUTE_TYPE");
        if (!string.IsNullOrWhiteSpace(computeType))
        {
            psi.ArgumentList.Add("--compute-type");
            psi.ArgumentList.Add(computeType.Trim());
        }

        try
        {
            _process = Process.Start(psi);
            if (_process == null)
            {
                _log?.Invoke("[STT] failed to start whisper-fast process (null)");
                return false;
            }
            AttachOutputHandlers(_process);
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

    private void AttachOutputHandlers(Process process)
    {
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _log?.Invoke($"[STT] whisper-fast: {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _log?.Invoke($"[STT] whisper-fast error: {e.Data}");
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void AppendWhisperFastDllPaths(ProcessStartInfo psi)
    {
        var serverDir = Path.GetDirectoryName(_serverPath);
        var pythonDir = Path.GetDirectoryName(_pythonPath);
        var venvRoot = pythonDir != null ? Directory.GetParent(pythonDir)?.FullName : null;
        var cudnnBin = venvRoot != null ? Path.Combine(venvRoot, "Lib", "site-packages", "nvidia", "cudnn", "bin") : null;
        var cublasBin = venvRoot != null ? Path.Combine(venvRoot, "Lib", "site-packages", "nvidia", "cublas", "bin") : null;
        var cu13Bin = venvRoot != null ? Path.Combine(venvRoot, "Lib", "site-packages", "nvidia", "cu13", "bin", "x86_64") : null;
        var extraFromEnv = Environment.GetEnvironmentVariable("WHISPER_FAST_DLL_DIRS");
        var extraDirs = SplitPaths(extraFromEnv);

        var allDirs = new[]
        {
            serverDir,
            pythonDir,
            cudnnBin,
            cublasBin,
            cu13Bin
        }.Concat(extraDirs).Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d)).ToArray();

        if (allDirs.Length == 0)
            return;

        var existing = psi.Environment["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var combined = string.Join(Path.PathSeparator, allDirs) + Path.PathSeparator + existing;
        psi.Environment["PATH"] = combined;
        _log?.Invoke($"[STT] whisper-fast PATH extended with: {string.Join(", ", allDirs)}");
    }

    private static string[] SplitPaths(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();
        return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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
