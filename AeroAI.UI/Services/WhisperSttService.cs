using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace AeroAI.UI.Services;

/// <summary>
/// Transcribes speech with whisper-cli (local GPU build).
/// </summary>
public sealed class WhisperSttService : ISttService
{
    private const string WhisperFolderName = "whisper";
    private const string WhisperExeName = "whisper-cli.exe";
    private const string ModelRelativePath = @"models\ggml-medium.en-q5_0.bin";

    public bool IsAvailable => TryResolvePaths(out _, out _, out _);

    private static bool TryResolvePaths(out string workingDir, out string exePath, out string modelPath)
    {
        var baseDir = AppContext.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var prefix = Enumerable.Repeat("..", i).ToArray();
            var candidate = Path.GetFullPath(Path.Combine(new[] { baseDir }.Concat(prefix).Concat(new[] { WhisperFolderName }).ToArray()));
            var exe = Path.Combine(candidate, WhisperExeName);
            var model = Path.Combine(candidate, ModelRelativePath);
            if (File.Exists(exe) && File.Exists(model))
            {
                workingDir = candidate;
                exePath = exe;
                modelPath = model;
                return true;
            }
        }

        workingDir = exePath = modelPath = string.Empty;
        return false;
    }

    public async Task<string?> TranscribeAsync(string wavPath, CancellationToken cancellationToken)
    {
        if (!TryResolvePaths(out var workingDir, out var exePath, out var modelPath))
        {
            throw new InvalidOperationException("Whisper not found. Place whisper-cli.exe in /whisper and model in /whisper/models");
        }

        if (!File.Exists(wavPath))
            throw new FileNotFoundException("Recorded audio was not found for transcription.", wavPath);

        var outputBase = Path.Combine(Path.GetTempPath(), $"aeroai_whisper_{Guid.NewGuid():N}");
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(ModelRelativePath); // relative to working dir
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(wavPath);
        psi.ArgumentList.Add("--language");
        psi.ArgumentList.Add("en");
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add("16");
        psi.ArgumentList.Add("--beam-size");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("--best-of");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("--no-timestamps");
        psi.ArgumentList.Add("-otxt");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add(outputBase);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start whisper-cli.exe");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var msg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            throw new InvalidOperationException($"Whisper failed (exit {process.ExitCode}): {msg}".Trim());
        }

        var transcript = await ReadTranscriptAsync(outputBase + ".txt", stdout, cancellationToken).ConfigureAwait(false);
        TryDelete(outputBase + ".txt");

        return string.IsNullOrWhiteSpace(transcript) ? null : transcript.Trim();
    }

    private static async Task<string?> ReadTranscriptAsync(string txtPath, string stdout, CancellationToken cancellationToken)
    {
        if (File.Exists(txtPath))
        {
            return await File.ReadAllTextAsync(txtPath, cancellationToken).ConfigureAwait(false);
        }

        return ParseFromStdout(stdout);
    }

    private static string? ParseFromStdout(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return null;

        var sb = new StringBuilder();
        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("[") && line.Contains("]"))
            {
                var idx = line.IndexOf("] ", StringComparison.Ordinal);
                if (idx >= 0 && idx + 2 < line.Length)
                    line = line[(idx + 2)..];
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                sb.Append(line);
                sb.Append(' ');
            }
        }

        return sb.ToString().Trim();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }
}

