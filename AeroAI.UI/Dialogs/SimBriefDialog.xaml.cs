using System.IO;
using System.Windows;
using AtcNavDataDemo.Config;

namespace AeroAI.UI.Dialogs;

public partial class SimBriefDialog : Window
{
    public string? PilotId { get; private set; }
    public bool WasImported { get; private set; }
    private readonly UserConfig _config;

    public SimBriefDialog()
    {
        InitializeComponent();
        
        // Load config - try both current directory and parent directory
        _config = LoadUserConfig();

        // Load saved pilot ID if present
        PilotId = _config.SimBriefUsername ?? string.Empty;
        if (!string.IsNullOrEmpty(PilotId))
        {
            PilotIdBox.Text = PilotId;
        }
    }

    private static UserConfig LoadUserConfig()
    {
        // Try current directory first (UI project output)
        var currentDir = Path.Combine(Directory.GetCurrentDirectory(), "userconfig.json");
        if (File.Exists(currentDir))
        {
            try
            {
                var json = File.ReadAllText(currentDir);
                var config = System.Text.Json.JsonSerializer.Deserialize<UserConfig>(json);
                if (config != null && !string.IsNullOrEmpty(config.SimBriefUsername))
                    return config;
            }
            catch { }
        }

        // Try parent directory (project root)
        var parentDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "userconfig.json");
        if (File.Exists(parentDir))
        {
            try
            {
                var json = File.ReadAllText(parentDir);
                var config = System.Text.Json.JsonSerializer.Deserialize<UserConfig>(json);
                if (config != null)
                    return config;
            }
            catch { }
        }

        // Try absolute path from project root
        var projectRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
        var rootConfig = Path.Combine(projectRoot, "userconfig.json");
        if (File.Exists(rootConfig))
        {
            try
            {
                var json = File.ReadAllText(rootConfig);
                var config = System.Text.Json.JsonSerializer.Deserialize<UserConfig>(json);
                if (config != null)
                    return config;
            }
            catch { }
        }

        return new UserConfig();
    }

    private static void SaveUserConfig(UserConfig config)
    {
        // Try to save to project root first
        var projectRoot = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", ".."));
        var rootConfig = Path.Combine(projectRoot, "userconfig.json");
        
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(rootConfig, json);
        }
        catch
        {
            // Fallback to current directory
            try
            {
                var currentConfig = Path.Combine(Directory.GetCurrentDirectory(), "userconfig.json");
                var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(currentConfig, json);
            }
            catch { }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        WasImported = false;
        Close();
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        PilotId = PilotIdBox.Text.Trim();
        
        if (string.IsNullOrEmpty(PilotId))
        {
            StatusText.Text = "Please enter a Pilot ID";
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xff, 0x66, 0x66));
            return;
        }

        // Persist for next time
        _config.SimBriefUsername = PilotId;
        SaveUserConfig(_config);

        WasImported = true;
        Close();
    }
}
