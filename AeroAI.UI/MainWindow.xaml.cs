using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using AtcNavDataDemo.Config;
using AeroAI.UI.Dialogs;
using AeroAI.UI.Services;

namespace AeroAI.UI;

public partial class MainWindow : Window
{
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    private readonly AtcService _atcService;
    private bool _isProcessing;
    private string _lastAtcMessage = string.Empty;
    private bool _firstPilotContact = true;
    private static readonly Lazy<HashSet<string>> AircraftTypes = new(() => LoadAircraftTypes(), isThreadSafe: true);

    public MainWindow()
    {
        InitializeComponent();
        MessageList.ItemsSource = Messages;
        
        _atcService = new AtcService();
        _atcService.OnAtcMessage += OnAtcMessageReceived;
        _atcService.OnFlightContextUpdated += OnFlightContextUpdated;
    }

    private void OnAtcMessageReceived(string station, string message)
    {
        Dispatcher.Invoke(() =>
        {
            AddAtcMessage(station, message);
        });
    }

    private void OnFlightContextUpdated(AeroAI.Atc.FlightContext context)
    {
        Dispatcher.Invoke(() =>
        {
            Callsign.Text = context.Callsign ?? context.RawCallsign ?? "---";
            AircraftType.Text = $"[{context.Aircraft?.IcaoType ?? "---"}]";
            AirportIcao.Text = context.OriginIcao ?? "----";
            DestIcao.Text = context.DestinationIcao ?? "---";
            FlightIdText.Text = context.Callsign ?? "---";
            FlightIdDisplay.Text = context.Callsign ?? "---";
            
            if (!string.IsNullOrEmpty(context.SquawkCode))
            {
                SquawkDisplay.Text = context.SquawkCode;
                XpdrCode.Text = context.SquawkCode;
            }

            // Get frequencies from lookup
            if (AeroAI.Data.AirportFrequencies.TryGetFrequencies(context.OriginIcao, out var freqs))
            {
                if (freqs.Clearance is double clr) ClrFreq.Text = clr.ToString("F3");
                if (freqs.Ground is double gnd)
                {
                    var freq = gnd.ToString("F3");
                    GndFreq.Text = freq;
                    Com1Freq.Text = freq;
                }
                if (freqs.Tower is double twr) TwrFreq.Text = twr.ToString("F3");
                if (freqs.Departure is double dep) DepFreq.Text = dep.ToString("F3");
            }

            // Update airport name (would need lookup)
            AirportName.Text = GetAirportName(context.OriginIcao);
        });
    }

    private string GetAirportName(string? icao)
    {
        return icao?.ToUpperInvariant() switch
        {
            "EGCC" => "Manchester Airport",
            "EGLL" => "Heathrow Airport",
            "EGKK" => "Gatwick Airport",
            "EGPH" => "Edinburgh Airport",
            "EHAM" => "Schiphol Airport",
            "LFPG" => "Paris CDG",
            "KJFK" => "JFK International",
            _ => "Airport"
        };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void PilotInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(PilotInput.Text) && !_isProcessing)
        {
            var text = PilotInput.Text.Trim();
            PilotInput.Clear();
            
            await ProcessPilotInputAsync(text);
        }
    }

    private void AddPilotMessage(string message)
    {
        Messages.Add(new ChatMessage
        {
            Station = _atcService.GetCurrentStation(),
            Message = "<< " + message,
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x21, 0x3e)),
            MessageColor = new SolidColorBrush(Colors.White)
        });
        ScrollToBottom();
    }

    private void AddAtcMessage(string station, string message)
    {
        _lastAtcMessage = message;
        Messages.Add(new ChatMessage
        {
            Station = station,
            Message = ">> " + message,
            Background = new SolidColorBrush(Color.FromRgb(0x1f, 0x40, 0x68)),
            MessageColor = new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff))
        });
        ScrollToBottom();
    }

    private void AddSystemMessage(string message)
    {
        Messages.Add(new ChatMessage
        {
            Station = "SYSTEM",
            Message = message,
            Background = new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x4a)),
            MessageColor = new SolidColorBrush(Color.FromRgb(0xff, 0xd7, 0x00))
        });
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        MessageScroller.ScrollToEnd();
    }

    private async Task ProcessPilotInputAsync(string text)
    {
        if (!_atcService.IsInitialized)
        {
            AddSystemMessage("No flight loaded. Click 'SimBrief' to import a flight plan.");
            return;
        }

        if (!PilotCallsignPresent(text))
        {
            AddSystemMessage("Who is calling? Please include your callsign at the end of the transmission (e.g., \"request clearance ... BAW123\").");
            return;
        }

        if (_firstPilotContact && IsClearanceRequest(text) && !ContainsAircraftType(text))
        {
            AddSystemMessage("Please include your aircraft type (ICAO) in the first transmission (e.g., \"request clearance A320 ... BAW123\").");
            return;
        }

        _isProcessing = true;
        AddPilotMessage(text);
        _firstPilotContact = false;

        try
        {
            var response = await _atcService.SendPilotMessageAsync(text);
            
            if (string.IsNullOrEmpty(response))
            {
                AddSystemMessage("No response from ATC.");
            }
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Error: {ex.Message}");
        }
        finally
        {
            _isProcessing = false;
        }
    }

    private void NewFlight_Click(object sender, RoutedEventArgs e)
    {
        Messages.Clear();
        _atcService.ResetFlight();
        _firstPilotContact = true;
        AddSystemMessage("Flight reset. Import a new flight plan from SimBrief.");
    }

    private async void SimBrief_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SimBriefDialog { Owner = this };
        dialog.ShowDialog();

        if (dialog.WasImported && !string.IsNullOrEmpty(dialog.PilotId))
        {
            await LoadFlightPlanAsync(dialog.PilotId);
        }
    }

    private async void ImportFlightPlan_Click(object sender, RoutedEventArgs e)
    {
        var config = UserConfigStore.Load();
        var pilotId = config.SimBriefUsername?.Trim();

        if (string.IsNullOrEmpty(pilotId))
        {
            AddSystemMessage("SimBrief Pilot ID not set. Click 'SimBrief' to enter it first.");
            return;
        }

        await LoadFlightPlanAsync(pilotId);
    }

    private async Task LoadFlightPlanAsync(string pilotId)
    {
        AddSystemMessage($"Loading flight plan from SimBrief...");

        try
        {
            await _atcService.InitializeAsync(pilotId);

            var flight = _atcService.CurrentFlight;
            if (flight != null)
            {
                AddSystemMessage($"Flight loaded: {flight.Callsign} {flight.OriginIcao} -> {flight.DestinationIcao}");
                AddSystemMessage($"Runway: {flight.SelectedDepartureRunway ?? "---"}, SID: {flight.SelectedSID ?? "---"}");
                AddSystemMessage("Ready for clearance request.");
                _firstPilotContact = true;
            }
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Failed to load: {ex.Message}");
        }
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { CommandParameter: ChatMessage msg })
        {
            try
            {
                Clipboard.SetText($"{msg.Station}: {msg.Message}");
            }
            catch (Exception ex)
            {
                AddSystemMessage($"Failed to copy: {ex.Message}");
            }
        }
    }

    private void CopyLastAtc_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastAtcMessage))
        {
            AddSystemMessage("No ATC message to copy yet.");
            return;
        }

        try
        {
            Clipboard.SetText(_lastAtcMessage);
            AddSystemMessage("Copied last ATC reply to clipboard.");
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Failed to copy: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _atcService?.Dispose();
        base.OnClosed(e);
    }

    private bool PilotCallsignPresent(string text)
    {
        var flight = _atcService.CurrentFlight;
        if (flight == null)
            return true; // cannot validate without context

        var trimmed = text.Trim();
        var candidate = flight.Callsign ?? flight.RawCallsign ?? string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
            return true;

        string Normalize(string s) => s.Trim().TrimEnd('.', ',', ';', '?', '!', ' ');

        var normInput = Normalize(trimmed).ToUpperInvariant();
        var normCall = Normalize(candidate).ToUpperInvariant();

        // Accept if the callsign appears at the end
        return normInput.EndsWith(normCall, StringComparison.OrdinalIgnoreCase);
    }

    private bool ContainsAircraftType(string text)
    {
        var flight = _atcService.CurrentFlight;
        string? knownType = flight?.Aircraft?.IcaoType;

        var tokens = text.ToUpperInvariant().Split(new[] { ' ', ',', '.', ';', ':', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(knownType) && tokens.Any(t => string.Equals(t, knownType.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var typeSet = AircraftTypes.Value;
        return tokens.Any(t => typeSet.Contains(t));
    }

    private static bool IsClearanceRequest(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("clearance") || lower.Contains("ifr");
    }

    private static HashSet<string> LoadAircraftTypes()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "aircraft_type.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var models = JsonSerializer.Deserialize<List<AircraftTypeModel>>(json) ?? new();
                return new HashSet<string>(models.Select(m => m.Icao.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // ignore and fall back to empty set
        }

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}

public class ChatMessage
{
    public string Station { get; set; } = "";
    public string Message { get; set; } = "";
    public Brush Background { get; set; } = Brushes.Transparent;
    public Brush MessageColor { get; set; } = Brushes.White;
}

public class AircraftTypeModel
{
    public string Icao { get; set; } = "";
    public string? Iata { get; set; }
    public string? Model { get; set; }
}
