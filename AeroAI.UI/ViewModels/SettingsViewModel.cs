using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Threading;
using System.Windows.Input;
using AtcNavDataDemo.Config;
using AeroAI.UI.Services;
using AeroAI.Audio;

namespace AeroAI.UI.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AudioDeviceService _audioService;
    private readonly UserConfig _config;
    private readonly Action<string?, string?, double, string?, int> _applyCallback;
    private readonly MicrophoneRecorder? _recorder;
    private bool _isTestingMic;
    private bool _isTestingAtc;
    private readonly DispatcherTimer _voiceLabTimer;
    private readonly HttpClient _voiceLabHttp = new();
    private bool _voiceLabHealthBusy;

    public ObservableCollection<AudioDeviceOption> MicrophoneDevices { get; } = new();
    public ObservableCollection<AudioDeviceOption> OutputDevices { get; } = new();
    public ObservableCollection<AudioDeviceOption> AtcOutputDevices { get; } = new();

    private string _simBriefId = string.Empty;
    public string SimBriefId
    {
        get => _simBriefId;
        set => SetField(ref _simBriefId, value);
    }

    private AudioDeviceOption? _selectedMic;
    public AudioDeviceOption? SelectedMicrophone
    {
        get => _selectedMic;
        set
        {
            if (SetField(ref _selectedMic, value))
                StatusText = string.Empty;
        }
    }

    private AudioDeviceOption? _selectedOutput;
    public AudioDeviceOption? SelectedOutput
    {
        get => _selectedOutput;
        set
        {
            if (SetField(ref _selectedOutput, value))
                StatusText = string.Empty;
        }
    }

    private double _micGainDb;
    public double MicGainDb
    {
        get => _micGainDb;
        set
        {
            if (SetField(ref _micGainDb, Math.Clamp(value, -12.0, 12.0)))
            {
                OnPropertyChanged(nameof(MicGainDisplay));
                if (_recorder != null)
                    _recorder.GainDb = _micGainDb;
            }
        }
    }
    
    public string MicGainDisplay => $"{MicGainDb:+0.0;-0.0;0} dB";

    private double _micLevel;
    public double MicLevel
    {
        get => _micLevel;
        set => SetField(ref _micLevel, value);
    }

    private bool _isClipping;
    public bool IsClipping
    {
        get => _isClipping;
        set => SetField(ref _isClipping, value);
    }

    private AudioDeviceOption? _selectedAtcOutput;
    public AudioDeviceOption? SelectedAtcOutput
    {
        get => _selectedAtcOutput;
        set
        {
            if (SetField(ref _selectedAtcOutput, value))
                StatusText = string.Empty;
        }
    }

    private int _atcVolumePercent = 100;
    public int AtcVolumePercent
    {
        get => _atcVolumePercent;
        set
        {
            if (SetField(ref _atcVolumePercent, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(AtcVolumeDisplay));
                // Apply live
                TtsPlayback.Volume = _atcVolumePercent / 100f;
            }
        }
    }
    
    public string AtcVolumeDisplay => $"{AtcVolumePercent}%";

    private double _atcPlaybackLevel;
    public double AtcPlaybackLevel
    {
        get => _atcPlaybackLevel;
        set => SetField(ref _atcPlaybackLevel, value);
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    private bool _voiceLabEnabled = true;
    public bool VoiceLabEnabled
    {
        get => _voiceLabEnabled;
        set
        {
            if (SetField(ref _voiceLabEnabled, value))
                RefreshVoiceLabHealthAsync();
        }
    }

    private string _voiceLabBaseUrl = "http://127.0.0.1:8008";
    public string VoiceLabBaseUrl
    {
        get => _voiceLabBaseUrl;
        set
        {
            if (SetField(ref _voiceLabBaseUrl, value))
                RefreshVoiceLabHealthAsync();
        }
    }

    private string _voiceLabHealthStatus = "Unknown";
    public string VoiceLabHealthStatus
    {
        get => _voiceLabHealthStatus;
        set => SetField(ref _voiceLabHealthStatus, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand TestMicCommand { get; }
    public ICommand StopTestMicCommand { get; }
    public ICommand TestAtcCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(AudioDeviceService audioService, UserConfig config, Action<string?, string?, double, string?, int> applyCallback, MicrophoneRecorder? recorder = null)
    {
        _audioService = audioService;
        _config = config;
        _applyCallback = applyCallback;
        _recorder = recorder;
        SaveCommand = new RelayCommand(_ => Save());
        TestMicCommand = new RelayCommand(_ => StartMicTest(), _ => !_isTestingMic);
        StopTestMicCommand = new RelayCommand(_ => StopMicTest(), _ => _isTestingMic);
        TestAtcCommand = new RelayCommand(_ => TestAtcAudio(), _ => !_isTestingAtc);
        _simBriefId = _config.SimBriefUsername ?? string.Empty;
        _micGainDb = _config.Audio?.MicGainDb ?? 0.0;
        _atcVolumePercent = _config.Audio?.AtcVolumePercent ?? 100;
        _voiceLabEnabled = _config.Tts?.VoiceLabEnabled ?? true;
        _voiceLabBaseUrl = string.IsNullOrWhiteSpace(_config.Tts?.VoiceLabBaseUrl)
            ? "http://127.0.0.1:8008"
            : _config.Tts!.VoiceLabBaseUrl;
        
        if (_recorder != null)
        {
            _recorder.GainDb = _micGainDb;
            _recorder.OnAudioLevel += level => MicLevel = level;
            _recorder.OnClipping += () => IsClipping = true;
        }
        
        // Hook up ATC playback level monitoring
        TtsPlayback.OnPlaybackLevel += level => AtcPlaybackLevel = level;
        TtsPlayback.Volume = _atcVolumePercent / 100f;

        _voiceLabTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _voiceLabTimer.Tick += (_, _) => RefreshVoiceLabHealthAsync();
        _voiceLabTimer.Start();
        RefreshVoiceLabHealthAsync();
        
        LoadDevices();
    }
    
    private void StartMicTest()
    {
        if (_recorder == null || _isTestingMic)
            return;
            
        _isTestingMic = true;
        _recorder.DeviceId = SelectedMicrophone?.Id;
        _recorder.StartRecording();
        StatusText = "Testing microphone...";
    }
    
    private async void StopMicTest()
    {
        if (_recorder == null || !_isTestingMic)
            return;
            
        _isTestingMic = false;
        await _recorder.StopRecordingAsync(default);
        MicLevel = 0;
        IsClipping = false;
        StatusText = "Test stopped";
    }

    private void LoadDevices()
    {
        MicrophoneDevices.Clear();
        OutputDevices.Clear();
        AtcOutputDevices.Clear();

        foreach (var mic in _audioService.GetInputDevices())
            MicrophoneDevices.Add(mic);
        foreach (var outDev in _audioService.GetOutputDevices())
        {
            OutputDevices.Add(outDev);
            AtcOutputDevices.Add(outDev);
        }

        if (!string.IsNullOrWhiteSpace(_config.Audio?.MicrophoneDeviceId))
        {
            SelectedMicrophone = MicrophoneDevices.FirstOrDefault(d => d.Id == _config.Audio.MicrophoneDeviceId)
                                 ?? MicrophoneDevices.FirstOrDefault();
        }
        else
        {
            SelectedMicrophone = MicrophoneDevices.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(_config.Audio?.OutputDeviceId))
        {
            SelectedOutput = OutputDevices.FirstOrDefault(d => d.Id == _config.Audio.OutputDeviceId)
                             ?? OutputDevices.FirstOrDefault();
        }
        else
        {
            SelectedOutput = OutputDevices.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(_config.Audio?.AtcOutputDeviceId))
        {
            SelectedAtcOutput = AtcOutputDevices.FirstOrDefault(d => d.Id == _config.Audio.AtcOutputDeviceId)
                                ?? AtcOutputDevices.FirstOrDefault();
        }
        else
        {
            SelectedAtcOutput = AtcOutputDevices.FirstOrDefault();
        }
    }
    
    private async void TestAtcAudio()
    {
        if (_isTestingAtc) return;
        
        _isTestingAtc = true;
        StatusText = "Playing test audio...";
        
        try
        {
            // Apply current selection for test
            TtsPlayback.OutputDeviceId = SelectedAtcOutput?.Id;
            TtsPlayback.Volume = AtcVolumePercent / 100f;
            
            // Generate a simple test tone (1 second, 440Hz sine wave)
            var testWav = GenerateTestTone(440, 1.0, 16000);
            await TtsPlayback.PlayWavBytesAsync(testWav);
        }
        catch (Exception ex)
        {
            StatusText = $"Test failed: {ex.Message}";
        }
        finally
        {
            _isTestingAtc = false;
            AtcPlaybackLevel = 0;
            StatusText = "Test complete";
        }
    }
    
    private static byte[] GenerateTestTone(double frequency, double durationSeconds, int sampleRate)
    {
        int numSamples = (int)(sampleRate * durationSeconds);
        var samples = new short[numSamples];
        
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / sampleRate;
            // Fade in/out envelope
            double envelope = Math.Min(1.0, Math.Min(t * 10, (durationSeconds - t) * 10));
            samples[i] = (short)(Math.Sin(2 * Math.PI * frequency * t) * 16000 * envelope);
        }
        
        // Build WAV file
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        
        // RIFF header
        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + numSamples * 2); // File size - 8
        writer.Write("WAVE".ToCharArray());
        
        // fmt chunk
        writer.Write("fmt ".ToCharArray());
        writer.Write(16); // Subchunk1Size
        writer.Write((short)1); // AudioFormat (PCM)
        writer.Write((short)1); // NumChannels
        writer.Write(sampleRate); // SampleRate
        writer.Write(sampleRate * 2); // ByteRate
        writer.Write((short)2); // BlockAlign
        writer.Write((short)16); // BitsPerSample
        
        // data chunk
        writer.Write("data".ToCharArray());
        writer.Write(numSamples * 2);
        foreach (var sample in samples)
            writer.Write(sample);
        
        return ms.ToArray();
    }

    private void Save()
    {
        _config.SimBriefUsername = SimBriefId?.Trim() ?? string.Empty;
        _config.Audio.MicrophoneDeviceId = SelectedMicrophone?.Id;
        _config.Audio.OutputDeviceId = SelectedOutput?.Id;
        _config.Audio.MicGainDb = MicGainDb;
        _config.Audio.AtcOutputDeviceId = SelectedAtcOutput?.Id;
        _config.Audio.AtcVolumePercent = AtcVolumePercent;
        _config.Tts.VoiceLabEnabled = VoiceLabEnabled;
        _config.Tts.VoiceLabBaseUrl = VoiceLabBaseUrl?.Trim() ?? "http://127.0.0.1:8008";
        UserConfigStore.Save(_config);
        _applyCallback?.Invoke(_config.Audio.MicrophoneDeviceId, _config.Audio.OutputDeviceId, MicGainDb, 
                                _config.Audio.AtcOutputDeviceId, AtcVolumePercent);
        StatusText = "Saved";
    }

    private async void RefreshVoiceLabHealthAsync()
    {
        if (_voiceLabHealthBusy)
            return;

        _voiceLabHealthBusy = true;
        try
        {
            if (!VoiceLabEnabled)
            {
                VoiceLabHealthStatus = "Disabled";
                return;
            }

            var probeConfig = new UserConfig
            {
                Tts = new TtsConfig { VoiceLabBaseUrl = VoiceLabBaseUrl }
            };
            var client = new VoiceLabTtsClient(_voiceLabHttp, probeConfig);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var health = await client.HealthAsync(cts.Token);
            VoiceLabHealthStatus = health.Online ? "Online" : "Offline";
        }
        catch
        {
            VoiceLabHealthStatus = "Offline";
        }
        finally
        {
            _voiceLabHealthBusy = false;
        }
    }
    
    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
