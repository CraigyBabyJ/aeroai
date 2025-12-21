using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AtcNavDataDemo.Config;
using AeroAI.Config;
using AeroAI.Services;
using AeroAI.UI.Dialogs;
using AeroAI.UI.Services;
using AeroAI.UI.ViewModels;
using AeroAI.Atc;
using AeroAI.Audio;
using AeroAI.Data;
using NAudio.Wave;

namespace AeroAI.UI;

public partial class MainWindow : Window
{
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    private readonly AtcService _atcService;
    private readonly MicrophoneRecorder _recorder = new();
    private readonly ISttService _sttService;
    private readonly ISttCorrectionLayer _sttCorrectionLayer;
    private readonly FlightLogService _flightLogService = new();
    private readonly Action<string> _sttDebugLog;
    private bool _isProcessing;
    private bool _isTranscribing;
    private string _lastAtcMessage = string.Empty;
    private bool _firstPilotContact = true;
    private readonly ObservableCollection<FrequencyOption> _frequencyOptions = new();
    private FrequencyOption? _selectedFrequency;
    private FrequencyOption? _suggestedController;
    private string? _connectedControllerRole;
    private double? _connectedControllerFrequency;
    private string? _pendingHandoffRole;
    private double? _pendingHandoffFrequency;
    private DateTime? _pendingHandoffIssuedAt;
    private bool _pendingHandoffActive;
    private bool _suppressFrequencySelection;
    private readonly SolidColorBrush _recordingBrush = new(Color.FromRgb(0xff, 0x55, 0x55));
    private readonly SolidColorBrush _transcribingBrush = new(Color.FromRgb(0xff, 0xa5, 0x00));
    private SolidColorBrush? _idleMicBrush;
    private SolidColorBrush? _idleButtonBrush;
    private SolidColorBrush? _idleBadgeBrush;
    private Storyboard? _recordingPulse;
    private bool _debugEnabled;
    private static readonly Lazy<HashSet<string>> AircraftTypes = new(() => LoadAircraftTypes(), isThreadSafe: true);
    private UserConfig _config = new();
    private List<AudioDeviceOption> _micDevices = new();
    private List<AudioDeviceOption> _outputDevices = new();

    public MainWindow()
    {
        InitializeComponent();
        _atcService = new AtcService();
        _atcService.OnAtcMessage += OnAtcMessageReceived;
        _atcService.OnFlightContextUpdated += OnFlightContextUpdated;
        _atcService.OnDebug += OnDebugReceived;
        _atcService.OnTtsNotice += OnTtsNoticeReceived;
        MessageList.ItemsSource = Messages;
        ConnectedToCombo.ItemsSource = _frequencyOptions;
        ConnectedToCombo.DisplayMemberPath = nameof(FrequencyOption.Display);
        SetFrequencyPlaceholder("Import SimBrief to load frequencies.");

        _sttDebugLog = msg =>
        {
            System.Diagnostics.Debug.WriteLine(msg);
            Console.WriteLine(msg);
            if (_debugEnabled)
            {
                Dispatcher.Invoke(() => AddSystemMessage("[DEBUG] " + msg));
            }
        };
        _sttService = CreateSttService();
        _sttCorrectionLayer = new SttCorrectionLayer(LogSttCorrection);
        _idleMicBrush = TryFindResource("TextSecondaryBrush") as SolidColorBrush ?? new SolidColorBrush(Colors.Gray);
        _idleButtonBrush = TryFindResource("BackgroundLightBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0x1f, 0x40, 0x68));
        _idleBadgeBrush = TryFindResource("BackgroundMediumBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromRgb(0x23, 0x33, 0x4d));
        
        // Hook up mic level for VHF-style bar
        _recorder.OnAudioLevel += level => Dispatcher.Invoke(() => UpdateMicLevelBar(level));
        ResetPushToTalkUi();
        StateChanged += (_, _) => UpdateMaxRestoreIcon();
        UpdateMaxRestoreIcon();
        InitializeUserConfigAndAudio();
        UpdateTestVoiceButtonState();

        if (!_sttService.IsAvailable)
        {
            PttButton.IsEnabled = false;
            AddSystemMessage(BuildSttUnavailableMessage());
        }
    }

    private void OnAtcMessageReceived(string station, string message)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateStationBadge(station);
            AddAtcMessage(station, message);
            HandleHandoffPrompt(message);
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
            FlightIdDisplay.Text = context.CanonicalCallsign ?? context.RawCallsign ?? context.Callsign ?? "---";

            // UI should be resilient if the weather hasn't populated into the context yet.
            var atisLetter = context.DepartureAtisLetter;
            var metar = context.OriginWeather?.RawMetar;
            if (!string.IsNullOrWhiteSpace(context.OriginIcao))
            {
                var cached = AtisMetarCache.Get(context.OriginIcao);
                atisLetter = string.IsNullOrWhiteSpace(atisLetter) ? cached.AtisLetter : atisLetter;
                metar = string.IsNullOrWhiteSpace(metar) ? cached.RawMetar : metar;
            }

            AtisLetterDisplay.Text = string.IsNullOrWhiteSpace(atisLetter) ? "Info ---" : $"Info {ToAtisPhonetic(atisLetter)}";
            MetarDisplay.Text = string.IsNullOrWhiteSpace(metar) ? "---" : CollapseWhitespace(metar);

            bool clearanceIssued = context.CurrentAtcState == AtcState.ClearanceIssued;
            var depRunway = context.SelectedDepartureRunway ?? context.DepartureRunway?.RunwayIdentifier;
            AdvisoryRunwayDisplay.Text = string.IsNullOrWhiteSpace(depRunway) ? "---" : depRunway;
            DepRunwayDisplay.Text = clearanceIssued && !string.IsNullOrWhiteSpace(depRunway) ? depRunway : "---";

            // Show initial cleared altitude only after clearance.
            int initAlt = context.ClearedAltitude ?? (context.CruiseFlightLevel > 300 ? 5000 : 3000);
            InitAltDisplay.Text = clearanceIssued && initAlt > 0 ? $"{initAlt} ft" : "---";
            UpdateStationBadge(_atcService.GetCurrentStation());
            
            if (clearanceIssued && !string.IsNullOrEmpty(context.SquawkCode))
            {
                SquawkDisplay.Text = context.SquawkCode;
                XpdrCode.Text = context.SquawkCode;
            }
            else
            {
                SquawkDisplay.Text = "----";
                XpdrCode.Text = "----";
            }

            // Get frequencies from lookup
            ClrFreq.Text = "---";
            GndFreq.Text = "---";
            TwrFreq.Text = "---";
            DepFreq.Text = "---";
            Com1Freq.Text = "---";
            if (AeroAI.Data.AirportFrequencies.TryGetFrequencies(context.OriginIcao, out var freqs))
            {
                if (freqs.Clearance is double clr) ClrFreq.Text = clr.ToString("F3");
                if (freqs.Ground is double gnd) GndFreq.Text = gnd.ToString("F3");
                if (freqs.Tower is double twr) TwrFreq.Text = twr.ToString("F3");
                if (freqs.Departure is double dep) DepFreq.Text = dep.ToString("F3");
            }
            var controllers = AirportFrequencyService.GetControllers(context.OriginIcao);
            UpdateFrequencyOptions(controllers, context.OriginIcao);
            UpdateNextControllerHint(context, controllers);

            // Update airport name (would need lookup)
            AirportName.Text = AirportNameResolver.ResolveAirportName(context.OriginIcao, _atcService.CurrentFlight);
        });
    }

    private void UpdateFrequencyOptions(IReadOnlyList<ControllerFrequency> controllers, string? icao)
    {
        _frequencyOptions.Clear();
        foreach (var controller in controllers)
        {
            _frequencyOptions.Add(new FrequencyOption(controller.Role, controller.FrequencyMhz));
        }

        if (_frequencyOptions.Count == 0)
        {
            var label = string.IsNullOrWhiteSpace(icao)
                ? "Import SimBrief to load frequencies."
                : $"No frequencies available for {icao}.";
            SetFrequencyPlaceholder(label);
            UpdateNextControllerHint(null, controllers);
            return;
        }

        if (ConnectedToCombo != null)
            ConnectedToCombo.IsEnabled = true;

        FrequencyOption? selection = null;
        if (_selectedFrequency != null && !_selectedFrequency.IsPlaceholder)
            selection = FindFrequencyOption(_selectedFrequency.Role, _selectedFrequency.FrequencyMhz);

        if (selection == null)
            selection = ChooseDefaultFrequency();

        SetSelectedFrequency(selection, updateComDisplay: true, userInitiated: false);
    }

    private FrequencyOption? FindFrequencyOption(string role, double frequencyMhz)
    {
        return _frequencyOptions.FirstOrDefault(option =>
            !option.IsPlaceholder &&
            option.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(option.FrequencyMhz - frequencyMhz) < 0.005);
    }

    private FrequencyOption? ChooseDefaultFrequency()
    {
        var order = new[] { "Delivery", "Ground", "Tower", "Departure", "Approach" };
        foreach (var role in order)
        {
            var match = _frequencyOptions.FirstOrDefault(option =>
                !option.IsPlaceholder &&
                option.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }
        return _frequencyOptions.FirstOrDefault(option => !option.IsPlaceholder);
    }

    private void SetSelectedFrequency(FrequencyOption? option, bool updateComDisplay, bool userInitiated)
    {
        _selectedFrequency = option;
        if (ConnectedToCombo != null)
        {
            _suppressFrequencySelection = true;
            ConnectedToCombo.SelectedItem = option;
            _suppressFrequencySelection = false;
        }

        if (updateComDisplay && option != null && !option.IsPlaceholder)
        {
            Com1Freq.Text = option.FrequencyMhz.ToString("F3");
        }
        else if (updateComDisplay)
        {
            Com1Freq.Text = "---";
        }

        if (option != null && !option.IsPlaceholder)
        {
            _connectedControllerRole = NormalizeConnectedRole(option.Role);
            _connectedControllerFrequency = option.FrequencyMhz;
            _atcService.UpdateConnectedController(_connectedControllerRole, _connectedControllerFrequency);
        }
        else
        {
            _connectedControllerRole = null;
            _connectedControllerFrequency = null;
            _atcService.UpdateConnectedController(null, null);
        }

        if (userInitiated && option != null && !option.IsPlaceholder)
            TryAcknowledgeHandoff(option);

        if (option != null)
        {
            var controllers = _frequencyOptions
                .Where(o => !o.IsPlaceholder)
                .Select(o => new ControllerFrequency(o.Role, o.FrequencyMhz))
                .ToList();
            UpdateNextControllerHint(_atcService.CurrentFlight, controllers);
        }
    }

    private void ConnectedToCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFrequencySelection)
            return;

        if (ConnectedToCombo.SelectedItem is FrequencyOption option)
        {
            if (option.IsPlaceholder)
                return;
            SetSelectedFrequency(option, updateComDisplay: true, userInitiated: true);
        }
    }

    private void SetFrequencyPlaceholder(string message)
    {
        _frequencyOptions.Clear();
        var placeholder = FrequencyOption.Placeholder(message);
        _frequencyOptions.Add(placeholder);
        _selectedFrequency = placeholder;
        _suppressFrequencySelection = true;
        ConnectedToCombo.SelectedItem = placeholder;
        _suppressFrequencySelection = false;
        ConnectedToCombo.IsEnabled = false;
        Com1Freq.Text = "---";
        if (NextControllerHint != null)
            NextControllerHint.Text = string.Empty;
        if (NextControllerSwitchButton != null)
            NextControllerSwitchButton.Visibility = Visibility.Collapsed;
        _suggestedController = null;
        _connectedControllerRole = null;
        _connectedControllerFrequency = null;
        _atcService.UpdateConnectedController(null, null);
    }

    private void HandleHandoffPrompt(string message)
    {
        if (_pendingHandoffActive)
            return;

        var pending = TryParseHandoff(message);
        if (pending == null)
            return;

        _pendingHandoffRole = NormalizeRole(pending.Role);
        _pendingHandoffFrequency = pending.FrequencyMhz;
        _pendingHandoffIssuedAt = DateTime.UtcNow;
        _pendingHandoffActive = true;
        ShowHandoffPrompt();
        var controllers = _frequencyOptions
            .Where(o => !o.IsPlaceholder)
            .Select(o => new ControllerFrequency(o.Role, o.FrequencyMhz))
            .ToList();
        UpdateNextControllerHint(_atcService.CurrentFlight, controllers);
    }

    private HandoffMatch? TryParseHandoff(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var match = Regex.Match(
            message,
            @"\b(contact|switch to|monitor|call)\b.*?\b(?<role>tower|ground|departure|approach|center|delivery|clearance)\b.*?\b(?<freq>\d{3}\.\d{1,3})",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            match = Regex.Match(
                message,
                @"\b(contact|switch to|monitor|call)\b.*?\b(?<freq>\d{3}\.\d{1,3}).*?\b(?<role>tower|ground|departure|approach|center|delivery|clearance)\b",
                RegexOptions.IgnoreCase);
        }

        if (!match.Success)
            return null;

        var roleRaw = match.Groups["role"].Value;
        var freqRaw = match.Groups["freq"].Value;
        if (string.IsNullOrWhiteSpace(roleRaw) || string.IsNullOrWhiteSpace(freqRaw))
            return null;

        if (!double.TryParse(freqRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
            return null;

        return new HandoffMatch(roleRaw, freq);
    }

    private void ShowHandoffPrompt()
    {
        if (HandoffPromptPanel == null || HandoffPromptText == null)
            return;

        if (!_pendingHandoffActive || string.IsNullOrWhiteSpace(_pendingHandoffRole) || _pendingHandoffFrequency == null)
        {
            HandoffPromptPanel.Visibility = Visibility.Collapsed;
            return;
        }

        HandoffPromptText.Text = $"Handoff: Select {_pendingHandoffRole} ({_pendingHandoffFrequency.Value:F3}) in the Connected to dropdown.";
        HandoffPromptPanel.Visibility = Visibility.Visible;
    }

    private void TryAcknowledgeHandoff(FrequencyOption selection)
    {
        if (!_pendingHandoffActive || string.IsNullOrWhiteSpace(_pendingHandoffRole) || _pendingHandoffFrequency == null)
            return;

        if (!RolesMatch(selection.Role, _pendingHandoffRole))
            return;

        if (Math.Abs(selection.FrequencyMhz - _pendingHandoffFrequency.Value) > 0.005)
            return;

        var acknowledgedRole = _pendingHandoffRole;
        var acknowledgedFrequency = _pendingHandoffFrequency.Value;
        _pendingHandoffRole = null;
        _pendingHandoffFrequency = null;
        _pendingHandoffIssuedAt = null;
        _pendingHandoffActive = false;
        if (HandoffPromptPanel != null)
            HandoffPromptPanel.Visibility = Visibility.Collapsed;

        AddSystemMessage($"Handoff acknowledged: {acknowledgedRole} {acknowledgedFrequency:F3}.");
        var controllers = _frequencyOptions
            .Where(o => !o.IsPlaceholder)
            .Select(o => new ControllerFrequency(o.Role, o.FrequencyMhz))
            .ToList();
        UpdateNextControllerHint(_atcService.CurrentFlight, controllers);
    }

    private static string NormalizeRole(string role)
    {
        var trimmed = role.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "delivery" => "Delivery",
            "clearance" => "Delivery",
            "ground" => "Ground",
            "tower" => "Tower",
            "departure" => "Departure",
            "approach" => "Approach",
            "center" => "Center",
            _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed)
        };
    }

    private static string NormalizeConnectedRole(string role)
    {
        var trimmed = role.Trim().ToLowerInvariant();
        return trimmed switch
        {
            "delivery" => "delivery",
            "clearance" => "delivery",
            "ground" => "ground",
            "tower" => "tower",
            "departure" => "departure",
            "approach" => "approach",
            "center" => "center",
            _ => trimmed
        };
    }

    private static bool RolesMatch(string a, string b)
    {
        return string.Equals(NormalizeRole(a), NormalizeRole(b), StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateNextControllerHint(FlightContext? context, IReadOnlyList<ControllerFrequency> controllers)
    {
        if (NextControllerHint == null)
            return;

        if (_pendingHandoffActive)
        {
            NextControllerHint.Text = string.Empty;
            if (NextControllerSwitchButton != null)
                NextControllerSwitchButton.Visibility = Visibility.Collapsed;
            _suggestedController = null;
            return;
        }

        if (context == null || controllers.Count == 0)
        {
            NextControllerHint.Text = string.Empty;
            if (NextControllerSwitchButton != null)
                NextControllerSwitchButton.Visibility = Visibility.Collapsed;
            _suggestedController = null;
            return;
        }

        bool isArrival = context.CurrentPhase >= FlightPhase.Descent_Arrival;
        var available = controllers.Select(c => (c.Role, c.FrequencyMhz)).ToList();
        var currentUnit = context.CurrentAtcUnit;
        if (_selectedFrequency != null && !_selectedFrequency.IsPlaceholder)
        {
            currentUnit = ControllerFlowHelper.FromRoleLabel(_selectedFrequency.Role) ?? currentUnit;
        }
        var suggestion = ControllerFlowHelper.SuggestNextAvailable(currentUnit, isArrival, available);
        if (suggestion == null)
        {
            NextControllerHint.Text = string.Empty;
            if (NextControllerSwitchButton != null)
                NextControllerSwitchButton.Visibility = Visibility.Collapsed;
            _suggestedController = null;
            return;
        }

        NextControllerHint.Text = $"Next suggested: {suggestion.Role} ({suggestion.FrequencyMhz:F3})";
        if (NextControllerSwitchButton != null)
            NextControllerSwitchButton.Visibility = Visibility.Visible;
        _suggestedController = _frequencyOptions.FirstOrDefault(option =>
            !option.IsPlaceholder &&
            option.Role.Equals(suggestion.Role, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(option.FrequencyMhz - suggestion.FrequencyMhz) < 0.005);
    }

    private static string CollapseWhitespace(string value)
    {
        var trimmed = value.Trim();
        return Regex.Replace(trimmed, "\\s+", " ");
    }

    private string? BuildSttInitialPrompt()
    {
        var flight = _atcService.CurrentFlight;
        if (flight == null)
            return null;

        var parts = new List<string>();
        var airports = new List<string>();

        var originName = AirportNameResolver.ResolveAirportName(flight.OriginIcao, flight);
        var originLabel = BuildAirportLabel(flight.OriginIcao, originName);
        if (!string.IsNullOrWhiteSpace(originLabel))
            airports.Add(originLabel);

        var destName = AirportNameResolver.ResolveAirportName(flight.DestinationIcao, flight);
        var destLabel = BuildAirportLabel(flight.DestinationIcao, destName);
        if (!string.IsNullOrWhiteSpace(destLabel))
            airports.Add(destLabel);

        if (airports.Count > 0)
            parts.Add($"Airports: {string.Join(", ", airports)}");

        var waypoints = flight.EnrouteRoute?.WaypointIdentifiers;
        if (waypoints != null && waypoints.Count > 0)
        {
            var routeFixes = waypoints
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
            if (routeFixes.Count > 0)
                parts.Add($"Route fixes: {string.Join(", ", routeFixes)}");
        }

        if (!string.IsNullOrWhiteSpace(flight.SelectedSID))
            parts.Add($"SID: {flight.SelectedSID}");
        if (!string.IsNullOrWhiteSpace(flight.SelectedSTAR))
            parts.Add($"STAR: {flight.SelectedSTAR}");
        if (!string.IsNullOrWhiteSpace(flight.SelectedDepartureRunway))
            parts.Add($"Departure runway: {flight.SelectedDepartureRunway}");
        if (!string.IsNullOrWhiteSpace(flight.SelectedArrivalRunway))
            parts.Add($"Arrival runway: {flight.SelectedArrivalRunway}");

        if (parts.Count == 0)
            return null;

        var prompt = CollapseWhitespace(string.Join(". ", parts));
        return prompt.Length > 500 ? prompt.Substring(0, 500) : prompt;
    }

    private static string? BuildAirportLabel(string? icao, string? name)
    {
        if (string.IsNullOrWhiteSpace(icao))
            return null;

        var trimmedIcao = icao.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(name))
            return trimmedIcao;

        var trimmedName = name.Trim();
        if (string.Equals(trimmedName, trimmedIcao, StringComparison.OrdinalIgnoreCase))
            return trimmedIcao;

        return $"{trimmedName} ({trimmedIcao})";
    }

    private static string ToAtisPhonetic(string atisLetter)
    {
        if (string.IsNullOrWhiteSpace(atisLetter))
            return "---";

        var c = char.ToUpperInvariant(atisLetter.Trim()[0]);
        return c switch
        {
            'A' => "Alfa",
            'B' => "Bravo",
            'C' => "Charlie",
            'D' => "Delta",
            'E' => "Echo",
            'F' => "Foxtrot",
            'G' => "Golf",
            'H' => "Hotel",
            'I' => "India",
            'J' => "Juliett",
            'K' => "Kilo",
            'L' => "Lima",
            'M' => "Mike",
            'N' => "November",
            'O' => "Oscar",
            'P' => "Papa",
            'Q' => "Quebec",
            'R' => "Romeo",
            'S' => "Sierra",
            'T' => "Tango",
            'U' => "Uniform",
            'V' => "Victor",
            'W' => "Whiskey",
            'X' => "X-ray",
            'Y' => "Yankee",
            'Z' => "Zulu",
            _ => c.ToString()
        };
    }

    private void ResetPushToTalkUi()
    {
        PttButton.Content = "Hold to talk";
        PttButton.Background = _idleButtonBrush ?? new SolidColorBrush(Color.FromRgb(0x1f, 0x40, 0x68));
        PttStateBadge.Background = _idleBadgeBrush ?? new SolidColorBrush(Color.FromRgb(0x23, 0x33, 0x4d));
        PttSpinner.Visibility = Visibility.Collapsed;
        
        // Turn off TX indicator
        Com1TxLight.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
        PttButton.IsEnabled = true;
        UpdateMicLevelBar(0); // Reset mic level bar
    }

    private void SetPushToTalkRecording()
    {
        PttButton.Content = "Release to send";
        PttButton.Background = _recordingBrush;
        PttStateBadge.Background = _recordingBrush;
        PttSpinner.Visibility = Visibility.Collapsed;
        PttButton.IsEnabled = true;
        
        // Light up TX indicator (pilot transmitting)
        Com1TxLight.Background = new SolidColorBrush(Color.FromRgb(255, 80, 80)); // Red
    }

    private void SetPushToTalkTranscribing()
    {
        PttButton.Content = "Transcribing...";
        PttButton.Background = _transcribingBrush;
        PttStateBadge.Background = _transcribingBrush;
        PttSpinner.Visibility = Visibility.Visible;
        PttButton.IsEnabled = false;
        UpdateMicLevelBar(0); // Reset mic level bar
    }

    /// <summary>
    /// Update the VHF-style mic level bar (0.0 to 1.0).
    /// </summary>
    private void UpdateMicLevelBar(double level)
    {
        if (MicLevelBar == null) return;
        
        // Clamp to 0-1 range
        level = Math.Clamp(level, 0.0, 1.0);
        
        // Get parent width for scaling
        var parent = MicLevelBar.Parent as FrameworkElement;
        var maxWidth = (parent?.ActualWidth ?? 150) - 2; // Account for margin
        
        MicLevelBar.Width = maxWidth * level;
    }

    private void PttButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        StartPushToTalk();
    }

    private async void PttButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        await FinishPushToTalkAsync();
    }

    private async void PttButton_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_recorder.IsRecording)
            await FinishPushToTalkAsync();
    }

    private void StartPushToTalk()
    {
        if (_isProcessing || _isTranscribing)
            return;

        if (!_sttService.IsAvailable)
        {
            AddSystemMessage(BuildSttUnavailableMessage());
            return;
        }

        if (!_atcService.IsInitialized)
        {
            AddSystemMessage("No flight loaded. Import a SimBrief plan before using push-to-talk.");
            return;
        }

        if (_recorder.IsRecording)
        {
            AddSystemMessage("Microphone is already active. Stop the mic test before using push-to-talk.");
            return;
        }

        try
        {
            PttButton?.CaptureMouse();
            _recorder.StartRecording();
            SetPushToTalkRecording();
        }
        catch (Exception ex)
        {
            PttButton?.ReleaseMouseCapture();
            ResetPushToTalkUi();
            AddSystemMessage($"Microphone error: {ex.Message}");
        }
    }

    private async Task FinishPushToTalkAsync()
    {
        if (_isTranscribing || !_recorder.IsRecording)
            return;

        _isTranscribing = true;
        SetPushToTalkTranscribing();

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            var wavPath = await _recorder.StopRecordingAsync(cts.Token);
            if (string.IsNullOrWhiteSpace(wavPath))
            {
                AddSystemMessage("Did not capture audio. Please try again.");
                return;
            }

            string? transcript = null;
            try
            {
                transcript = await _sttService.TranscribeAsync(wavPath, cts.Token);
            }
            finally
            {
                TryDeleteFile(wavPath);
            }

            if (string.IsNullOrWhiteSpace(transcript))
            {
                AddSystemMessage("Did not catch any speech. Please try again.");
            }
            else
            {
                // Apply STT corrections first, then SimBrief name corrections, then normalize.
                var corrected = _sttCorrectionLayer.Apply(transcript);
                var flight = _atcService.CurrentFlight;
                if (flight != null)
                {
                    corrected = SimBriefSttCorrector.Apply(corrected, flight, _sttDebugLog, _debugEnabled);
                    var normalized = PilotTransmissionNormalizer.Normalize(corrected, flight, enableDebugLogging: _debugEnabled);
                    AddSystemMessage($"Heard: \"{corrected}\"");
                    await ProcessPilotInputAsync(normalized);
                }
                else
                {
                    AddSystemMessage($"Heard: \"{corrected}\"");
                    await ProcessPilotInputAsync(corrected);
                }
            }
        }
        catch (OperationCanceledException)
        {
            AddSystemMessage("Speech timeout. Please try again.");
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Speech error: {ex.Message}");
        }
        finally
        {
            _isTranscribing = false;
            PttButton?.ReleaseMouseCapture();
            ResetPushToTalkUi();
        }
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxRestoreIcon();
    }

    private void UpdateMaxRestoreIcon()
    {
        if (MaxRestoreButton == null)
            return;

        MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void StartRecordingPulse()
    {
        // No longer needed - mic level bar provides visual feedback
    }

    private void StopRecordingPulse()
    {
        if (_recordingPulse != null)
        {
            _recordingPulse.Stop();
            _recordingPulse = null;
        }
    }
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
        var callsign = _atcService.CurrentFlight?.Callsign ?? _atcService.CurrentFlight?.RawCallsign ?? "Pilot";
        var stationLabel = string.IsNullOrWhiteSpace(callsign) ? "Pilot" : $"Pilot ({callsign})";
        _flightLogService.Log("PILOT", message);
        Messages.Add(new ChatMessage
        {
            Station = stationLabel,
            Message = "<< " + message,
            Background = new SolidColorBrush(Color.FromRgb(0x16, 0x21, 0x3e)),
            MessageColor = new SolidColorBrush(Colors.White)
        });
        ScrollToBottom();
    }

    private void AddAtcMessage(string station, string message)
    {
        _lastAtcMessage = message;
        _flightLogService.Log("ATC", message);
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
        _flightLogService.Log("SYSTEM", message);
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

        // Use centralized normalizer if flight is loaded
        var flight = _atcService.CurrentFlight;
        if (flight != null)
        {
            text = PilotTransmissionNormalizer.Normalize(text, flight, enableDebugLogging: _debugEnabled);
        }

        bool allowAnywhereCallsign = _firstPilotContact && IsClearanceRequest(text);

        // CALLSIGN GATING: Only run if callsign is NOT already known (session-sticky).
        // Once FlightContext.Callsign or CanonicalCallsign is set, NEVER ask "Who is calling?" again.
        // Also skip if any pending confirmation is active (readback, destination, ATIS).
        var callsignKnown = !string.IsNullOrWhiteSpace(flight?.Callsign) || !string.IsNullOrWhiteSpace(flight?.CanonicalCallsign);
        
        if (!callsignKnown && !_atcService.HasPendingConfirmation)
        {
            if (!CallsignValidator.IsPresent(text, _atcService.CurrentFlight, allowAnywhereCallsign))
            {
                var prompt = allowAnywhereCallsign
                    ? "Who is calling? Please include your callsign (e.g., \"this is BAW123 requesting clearance\")."
                    : "Who is calling? Please include your callsign at the end of the transmission (e.g., \"request taxi ... BAW123\").";
                AddSystemMessage(prompt);
                _ = _atcService.SpeakAtcAsync(prompt);
                return;
            }

            if (_firstPilotContact && IsClearanceRequest(text) && !ContainsAircraftType(text))
            {
                // Log the pilot attempt but require explicit aircraft type on first contact.
                AddPilotMessage(text);
                var prompt = "Please include your aircraft type (ICAO) in the first transmission (e.g., \"request clearance A320 ... BAW123\").";
                AddSystemMessage(prompt);
                _ = _atcService.SpeakAtcAsync(prompt);
                return;
            }
        }

        _isProcessing = true;
        AddPilotMessage(text);
        _firstPilotContact = false;

        try
        {
            // Light up RX indicator (ATC transmitting)
            Com1RxLight.Background = new SolidColorBrush(Color.FromRgb(80, 255, 80)); // Green
            
            var response = await _atcService.SendPilotMessageAsync(text);
            
            // Turn off RX indicator
            Com1RxLight.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
            
            if (string.IsNullOrEmpty(response))
            {
                AddSystemMessage("No response from ATC.");
            }
        }
        catch (Exception ex)
        {
            // Turn off RX indicator on error
            Com1RxLight.Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
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
        _flightLogService.Dispose();
        AddSystemMessage("Flight reset. Import a new flight plan from SimBrief.");
    }

    private async void SimBrief_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SimBriefDialog { Owner = this };
        dialog.ShowDialog();

        if (dialog.WasImported && !string.IsNullOrEmpty(dialog.PilotId))
        {
            await LoadFlightPlanAsync(dialog.PilotId, forceRefresh: true, clearMessages: true);
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

        await LoadFlightPlanAsync(pilotId, forceRefresh: true, clearMessages: true);
    }

    private async Task LoadFlightPlanAsync(string pilotId, bool forceRefresh = false, bool clearMessages = false)
    {
        AddSystemMessage(forceRefresh
            ? "Loading flight plan from SimBrief (fresh fetch)..."
            : "Loading flight plan from SimBrief...");

        try
        {
            // Reset state before loading a new/updated plan.
            _atcService.ResetFlight();
            _firstPilotContact = true;
            _lastAtcMessage = string.Empty;

            await _atcService.InitializeAsync(pilotId, forceRefresh);

            var flight = _atcService.CurrentFlight;
            if (flight != null)
            {
                if (clearMessages)
                    Messages.Clear();

                AddSystemMessage($"Flight loaded: {flight.Callsign} {flight.OriginIcao} -> {flight.DestinationIcao}");
                AddSystemMessage("Ready for clearance request.");
                _flightLogService.StartNewLog(flight.OriginIcao, flight.DestinationIcao, flight.Callsign ?? flight.RawCallsign);
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

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _atcService?.Dispose();
        _recorder?.Dispose();
        if (_sttCorrectionLayer is IDisposable disposable)
            disposable.Dispose();
        if (_sttService is IDisposable sttDisposable)
            sttDisposable.Dispose();
        _flightLogService?.Dispose();
        base.OnClosed(e);
    }

    private void OnDebugReceived(string message)
    {
        if (_debugEnabled)
            AddSystemMessage("[DEBUG] " + message);
    }

    private void OnTtsNoticeReceived(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        Dispatcher.Invoke(() => AddSystemMessage(message));
    }

    private ISttService CreateSttService()
    {
        LoadDotEnvIfPresent();

        var backend = (Environment.GetEnvironmentVariable("STT_BACKEND") ?? "whisper").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(backend))
            backend = "whisper";
        _sttDebugLog?.Invoke($"[STT] configured backend={backend}");

        var whisperCli = new WhisperSttService();
        ISttService? whisperFast = null;

        if (backend == "whisper-fast")
        {
            var python = Environment.GetEnvironmentVariable("WHISPER_FAST_PYTHON");
            if (string.IsNullOrWhiteSpace(python))
            {
                python = ResolveUpwards("whisper-fast\\venv311\\Scripts\\python.exe");
                if (!File.Exists(python))
                    python = ResolveUpwards("whisper-fast\\venv\\Scripts\\python.exe");
                if (IsBrokenWhisperFastVenv(python))
                {
                    _sttDebugLog?.Invoke("[STT] whisper-fast venv missing pyvenv.cfg; using system python from PATH (set WHISPER_FAST_PYTHON to override).");
                    python = "python";
                }
            }
            else if (!LooksLikeCommandName(python) && !Path.IsPathRooted(python))
            {
                python = ResolveUpwards(python);
            }
            var serverPath = ResolveUpwards("whisper-fast\\service.py");
            var model = Environment.GetEnvironmentVariable("WHISPER_FAST_MODEL")
                        ?? "jacktol/whisper-medium.en-fine-tuned-for-ATC-faster-whisper";
            var portEnv = Environment.GetEnvironmentVariable("WHISPER_FAST_PORT");
            var port = 8765;
            if (!string.IsNullOrWhiteSpace(portEnv) && int.TryParse(portEnv, out var parsedPort) && parsedPort > 0)
                port = parsedPort;
            var device = Environment.GetEnvironmentVariable("WHISPER_FAST_DEVICE") ?? "auto";
            var compute = Environment.GetEnvironmentVariable("WHISPER_FAST_COMPUTE_TYPE") ?? "auto";
            var dllDirs = Environment.GetEnvironmentVariable("WHISPER_FAST_DLL_DIRS") ?? "none";
            _sttDebugLog?.Invoke($"[STT] whisper-fast config: python=\"{python}\", server=\"{serverPath}\", model=\"{model}\", port={port}, device={device}, compute={compute}, dll_dirs=\"{dllDirs}\"");

            if (IsExecutableCandidate(python) && !string.IsNullOrWhiteSpace(serverPath) && File.Exists(serverPath))
            {
                var host = new WhisperFastHost(python, serverPath, model, port, _sttDebugLog);
                // Start asynchronously at launch; failures will be logged and per-request fallback will use whisper-cli.
                _ = Task.Run(async () =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await host.StartAsync(cts.Token);
                });
                whisperFast = new WhisperFastSttService(
                    host,
                    _sttDebugLog,
                    available: true,
                    initialPromptProvider: BuildSttInitialPrompt);
            }
            else
            {
                _sttDebugLog?.Invoke("[STT] whisper-fast unavailable (python/server missing), using whisper-cli");
                backend = "whisper";
            }
        }

        _sttDebugLog?.Invoke($"[STT] selected backend={backend}, fast_available={(whisperFast != null ? "yes" : "no")}, cli_available={(whisperCli.IsAvailable ? "yes" : "no")}");
        return new SttBackendRouter(whisperCli, whisperFast, backend, _sttDebugLog);
    }

    private static void LoadDotEnvIfPresent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ".env");
        if (!File.Exists(path))
        {
            // Walk upwards to repo root
            for (var i = 0; i < 6; i++)
            {
                var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, string.Join(Path.DirectorySeparatorChar, Enumerable.Repeat("..", i)) , ".env"));
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }
        }

        if (!File.Exists(path))
            return;

        try
        {
            var lines = File.ReadAllLines(path);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;
                var idx = line.IndexOf('=');
                if (idx <= 0)
                    continue;
                var key = line[..idx].Trim();
                var val = line[(idx + 1)..].Trim().Trim('"').Trim('\'');
                if (Environment.GetEnvironmentVariable(key) == null)
                    Environment.SetEnvironmentVariable(key, val);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string ResolveUpwards(string relativePath)
    {
        var baseDir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.GetFullPath(Path.Combine(new[] { baseDir }.Concat(Enumerable.Repeat("..", i)).Concat(new[] { relativePath }).ToArray()));
            if (File.Exists(candidate))
                return candidate;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, relativePath));
    }

    private static string BuildSttUnavailableMessage()
    {
        var backend = (Environment.GetEnvironmentVariable("STT_BACKEND") ?? "whisper").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(backend))
            backend = "whisper";
        return backend == "whisper-fast"
            ? "Whisper-fast not available. Check WHISPER_FAST_PYTHON, model, and the whisper-fast service."
            : "Whisper not found. Place whisper-cli.exe in /whisper and model in /whisper/models";
    }

    private static bool IsExecutableCandidate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (LooksLikeCommandName(value))
            return true;
        return File.Exists(value);
    }

    private static bool LooksLikeCommandName(string value)
    {
        return !Path.IsPathRooted(value)
               && value.IndexOf(Path.DirectorySeparatorChar) < 0
               && value.IndexOf(Path.AltDirectorySeparatorChar) < 0;
    }

    private static bool IsBrokenWhisperFastVenv(string? pythonPath)
    {
        if (string.IsNullOrWhiteSpace(pythonPath) || !File.Exists(pythonPath))
            return false;

        var scriptsDir = Path.GetDirectoryName(pythonPath);
        if (string.IsNullOrWhiteSpace(scriptsDir) || !string.Equals(Path.GetFileName(scriptsDir), "Scripts", StringComparison.OrdinalIgnoreCase))
            return false;

        var venvRoot = Path.GetDirectoryName(scriptsDir);
        if (string.IsNullOrWhiteSpace(venvRoot) || !string.Equals(Path.GetFileName(venvRoot), "venv", StringComparison.OrdinalIgnoreCase))
            return false;

        var cfgPath = Path.Combine(venvRoot, "pyvenv.cfg");
        return !File.Exists(cfgPath);
    }

    private void DebugToggle_Checked(object sender, RoutedEventArgs e) => _debugEnabled = true;
    private void DebugToggle_Unchecked(object sender, RoutedEventArgs e) => _debugEnabled = false;

    private bool ContainsAircraftType(string text)
    {
        var flight = _atcService.CurrentFlight;
        if (flight?.Aircraft?.IcaoType is { Length: > 0 })
            return true; // already known from flight plan

        string? knownType = flight?.Aircraft?.IcaoType;

        var tokens = text.ToUpperInvariant().Split(new[] { ' ', ',', '.', ';', ':', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (!string.IsNullOrWhiteSpace(knownType) && tokens.Any(t => string.Equals(t, knownType.ToUpperInvariant(), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Accept common natural-speech aircraft references (e.g., "boeing 737", "airbus three twenty", "dash 8", "king air").
        var joined = text.ToLowerInvariant();
        if (joined.Contains("boeing") || joined.Contains("airbus") || joined.Contains("embraer") ||
            joined.Contains("crj") || joined.Contains("cessna") || joined.Contains("piper") ||
            joined.Contains("king air") || joined.Contains("dash 8") || joined.Contains("q400") ||
            joined.Contains("atr") || joined.Contains("twin otter"))
        {
            return true;
        }

        // Use resolver against authoritative dataset.
        var resolved = AircraftTypeResolver.ResolveSimple(text);
        if (!string.IsNullOrWhiteSpace(resolved))
            return true;

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

    private string NormalizePilotTransmissionForFlight(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var flight = _atcService.CurrentFlight;
        if (flight == null)
            return text;

        var normalized = CallsignNormalizer.Normalize(text, flight);
        normalized = ReadbackNormalizer.Normalize(normalized, flight);
        return normalized;
    }

    private void UpdateStationBadge(string? stationText)
    {
        StationBadgeText.Text = string.IsNullOrWhiteSpace(stationText) ? "Delivery" : stationText;
    }

    private void LogSttCorrection(string message)
    {
        // Only surface applied-rule logs to the user; keep lifecycle noise in debug.
        if (string.IsNullOrWhiteSpace(message))
            return;

        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);

        if (message.StartsWith("[STT-CORR]", StringComparison.OrdinalIgnoreCase))
        {
            Dispatcher.Invoke(() => AddSystemMessage(message));
        }
    }

    private void InitializeUserConfigAndAudio()
    {
        try
        {
            _config = UserConfigStore.Load() ?? new UserConfig();
        }
        catch
        {
            _config = new UserConfig();
        }

        try
        {
            LoadAudioDevices();
            ApplyAudioSelection(
                _config.Audio?.MicrophoneDeviceId, 
                _config.Audio?.OutputDeviceId, 
                _config.Audio?.MicGainDb ?? 0.0,
                _config.Audio?.AtcOutputDeviceId,
                _config.Audio?.AtcVolumePercent ?? 100);
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Audio init fallback: {ex.Message}");
        }

        SetTabSelection(isSettings: false);
    }

    private void LoadAudioDevices()
    {
        try
        {
            var service = new AudioDeviceService();
            _micDevices = service.GetInputDevices().ToList();
            _outputDevices = service.GetOutputDevices().ToList();

            // Pass recorder to SettingsViewModel for mic test and gain control
            var settingsVm = new SettingsViewModel(service, _config, ApplyAudioSelection, _recorder);
            var view = new Views.SettingsView { DataContext = settingsVm };
            if (SettingsHost != null)
            {
                if (SettingsHost.Content is UserControl existing && existing.DataContext is IDisposable disposable)
                    disposable.Dispose();
                SettingsHost.Content = view;
            }
        }
        catch (Exception ex)
        {
            _micDevices = new List<AudioDeviceOption>();
            _outputDevices = new List<AudioDeviceOption>();
            if (SettingsHost != null)
                SettingsHost.Content = null;
            AddSystemMessage($"Audio device enumeration failed: {ex.Message}");
        }
    }

    private void ApplyAudioSelection(string? micId, string? outputId, double gainDb, string? atcOutputId, int atcVolumePercent)
    {
        _recorder.DeviceId = micId;
        _recorder.DeviceNumber = null;
        _recorder.GainDb = gainDb;
        TtsPlayback.OutputDeviceId = atcOutputId ?? outputId; // ATC uses dedicated output if set
        TtsPlayback.OutputDeviceNumber = -1;
        TtsPlayback.Volume = atcVolumePercent / 100f;
    }

    private void TabAtcButton_Click(object sender, RoutedEventArgs e)
    {
        SetTabSelection(isSettings: false);
    }

    private void TabSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SetTabSelection(isSettings: true);
    }

    private async void TestVoiceButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = UserConfigStore.Load();
            var voiceLabEnabled = config.Tts?.VoiceLabEnabled ?? true;
            if (!voiceLabEnabled)
            {
                AddSystemMessage("VoiceLab is disabled in Settings.");
                return;
            }
            if (!_atcService.HasAnyTtsProvider())
            {
                AddSystemMessage("No TTS provider is available.");
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var health = await _atcService.GetVoiceLabHealthAsync(cts.Token);
            if (!health.Online)
            {
                AddSystemMessage("VoiceLab is offline. Start the VoiceLab service and try again.");
                return;
            }

            var ok = await _atcService.TestSpeakAsync("Radio check | testing one two three", cts.Token);
            if (!ok)
            {
                AddSystemMessage("No TTS provider is available.");
            }
        }
        catch (Exception ex)
        {
            AddSystemMessage($"Test voice failed: {ex.Message}");
        }
    }

    private void NextControllerSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_suggestedController == null || _suggestedController.IsPlaceholder)
            return;

        SetSelectedFrequency(_suggestedController, updateComDisplay: true, userInitiated: true);
    }

    private void UpdateTestVoiceButtonState()
    {
        if (TestVoiceButton == null && TestVoiceButtonBottom == null)
            return;

        var config = UserConfigStore.Load();
        var voiceLabEnabled = config.Tts?.VoiceLabEnabled ?? true;
        var canTest = voiceLabEnabled && _atcService.HasAnyTtsProvider();
        if (TestVoiceButton != null)
            TestVoiceButton.IsEnabled = canTest;
        if (TestVoiceButtonBottom != null)
            TestVoiceButtonBottom.IsEnabled = canTest;
    }

    private void SetTabSelection(bool isSettings)
    {
        SettingsHost.Visibility = isSettings ? Visibility.Visible : Visibility.Collapsed;
        ComBar.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        AirportInfoPanel.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        MessagesPanel.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        InputBar.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        BottomButtonsBar.Visibility = isSettings ? Visibility.Collapsed : Visibility.Visible;
        if (!isSettings)
            UpdateTestVoiceButtonState();

        if (TabAtcButton != null && TabSettingsButton != null)
        {
            TabAtcButton.Style = TryFindResource(isSettings ? "NavButton" : "PrimaryNavButton") as Style ?? TabAtcButton.Style;
            TabSettingsButton.Style = TryFindResource(isSettings ? "PrimaryNavButton" : "NavButton") as Style ?? TabSettingsButton.Style;
        }
    }
}

public class ChatMessage
{
    public string Station { get; set; } = "";
    public string Message { get; set; } = "";
    public Brush Background { get; set; } = Brushes.Transparent;
    public Brush MessageColor { get; set; } = Brushes.White;
}

public sealed class FrequencyOption
{
    public FrequencyOption(string role, double frequencyMhz)
    {
        Role = role;
        FrequencyMhz = frequencyMhz;
        Display = $"{role} ({frequencyMhz:F3})";
    }

    private FrequencyOption(string display)
    {
        Role = display;
        FrequencyMhz = 0;
        Display = display;
        IsPlaceholder = true;
    }

    public string Role { get; }
    public double FrequencyMhz { get; }
    public string Display { get; }
    public bool IsPlaceholder { get; }

    public static FrequencyOption Placeholder(string display)
    {
        return new FrequencyOption(display);
    }
}

public sealed class HandoffMatch
{
    public HandoffMatch(string role, double frequencyMhz)
    {
        Role = role;
        FrequencyMhz = frequencyMhz;
    }

    public string Role { get; }
    public double FrequencyMhz { get; }
}

public class AircraftTypeModel
{
    public string Icao { get; set; } = "";
    public string? Iata { get; set; }
    public string? Model { get; set; }
}
