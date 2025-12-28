using System;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AeroAI.UI.Services;

namespace AeroAI.UI;

public partial class DebugLogWindow : Window
{
    private readonly DebugLogCollector _collector;
    
    public DebugLogWindow(DebugLogCollector collector)
    {
        InitializeComponent();
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        
        // Initial population
        PopulateLog();
        
        // Subscribe to updates
        _collector.Entries.CollectionChanged += Entries_CollectionChanged;
        
        // Unsubscribe on close
        Closed += (s, e) => _collector.Entries.CollectionChanged -= Entries_CollectionChanged;
    }

    private void PopulateLog()
    {
        var sb = new StringBuilder();
        foreach (var entry in _collector.Entries)
        {
            sb.AppendLine(FormatEntry(entry));
        }
        LogTextBox.Text = sb.ToString();
        ScrollToEndIfEnabled();
    }

    private void Entries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            var sb = new StringBuilder();
            foreach (DebugLogEntry item in e.NewItems)
            {
                sb.AppendLine(FormatEntry(item));
            }
            LogTextBox.AppendText(sb.ToString());
            ScrollToEndIfEnabled();
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            LogTextBox.Clear();
        }
    }

    private static string FormatEntry(DebugLogEntry entry)
    {
        return $"[{entry.Timestamp:HH:mm:ss}] [{entry.Category}] {entry.Message}";
    }

    private void ScrollToEndIfEnabled()
    {
        if (AutoScrollCheck.IsChecked == true)
        {
            LogTextBox.ScrollToEnd();
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        _collector.Clear();
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.SelectAll();
        LogTextBox.Copy();
        LogTextBox.SelectionLength = 0; // Deselect after copy
    }
}
