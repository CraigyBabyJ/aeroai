using System;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace AeroAI.UI.Services;

public sealed class DebugLogCollector
{
    private readonly ObservableCollection<DebugLogEntry> _entries = new();
    private readonly Dispatcher _dispatcher;
    private readonly int _maxEntries;

    public DebugLogCollector(Dispatcher dispatcher, int maxEntries = 200)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _maxEntries = maxEntries > 0 ? maxEntries : 200;
        Entries = _entries;
    }

    public ObservableCollection<DebugLogEntry> Entries { get; }

    public void Add(string message, string level = "DEBUG")
    {
        var entry = new DebugLogEntry(DateTime.Now, level, ExtractCategory(message), message);
        if (_dispatcher.CheckAccess())
        {
            AddInternal(entry);
        }
        else
        {
            _dispatcher.BeginInvoke(() => AddInternal(entry));
        }
    }

    public void Clear()
    {
        if (_dispatcher.CheckAccess())
        {
            _entries.Clear();
        }
        else
        {
            _dispatcher.BeginInvoke(() => _entries.Clear());
        }
    }

    private void AddInternal(DebugLogEntry entry)
    {
        _entries.Add(entry);
        while (_entries.Count > _maxEntries)
        {
            _entries.RemoveAt(0);
        }
    }

    private static string ExtractCategory(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "General";
        }

        var match = Regex.Match(message, @"^\[(?<category>[^\]]+)\]");
        return match.Success ? match.Groups["category"].Value : "General";
    }
}

public sealed record DebugLogEntry(DateTime Timestamp, string Level, string Category, string Message);
