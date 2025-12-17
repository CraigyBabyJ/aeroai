using System;
using System.IO;
using System.Text;

namespace AeroAI.UI.Services;

/// <summary>
/// Simple per-flight conversation logger. Starts a new log file when a flight is loaded and appends all chat/system messages.
/// </summary>
public sealed class FlightLogService : IDisposable
{
    private readonly object _sync = new();
    private StreamWriter? _writer;

    public void StartNewLog(string? originIcao, string? destinationIcao, string? callsign)
    {
        lock (_sync)
        {
            _writer?.Dispose();

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AeroAI", "logs");
            Directory.CreateDirectory(dir);

            var origin = SanitizePart(string.IsNullOrWhiteSpace(originIcao) ? "UNKNOWN" : originIcao.ToUpperInvariant());
            var dest = SanitizePart(string.IsNullOrWhiteSpace(destinationIcao) ? "UNKNOWN" : destinationIcao.ToUpperInvariant());
            var cs = SanitizePart(string.IsNullOrWhiteSpace(callsign) ? "NO_CALLSIGN" : callsign.ToUpperInvariant());
            var name = $"{origin}-{dest}_{cs}_{DateTime.Now:yyyyMMdd_HHmmss}.log";

            var path = Path.Combine(dir, name);
            _writer = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            _writer.WriteLine($"# AeroAI chat log");
            _writer.WriteLine($"# Origin={originIcao ?? "?"}, Destination={destinationIcao ?? "?"}, Callsign={callsign ?? "?"}");
            _writer.WriteLine($"# Started={DateTime.Now:O}");
        }
    }

    public void Log(string role, string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (_sync)
        {
            if (_writer == null)
                return;

            var line = $"[{DateTime.Now:O}] {role}: {message}";
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static string SanitizePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
