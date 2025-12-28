using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AeroAI.Audio;

/// <summary>
/// Simple WAV playback helper from byte[] or temp file.
/// </summary>
public static class TtsPlayback
{
    public static int OutputDeviceNumber { get; set; } = -1;
    public static string? OutputDeviceId { get; set; }
    
    /// <summary>
    /// ATC volume as a linear multiplier (0.0 to 1.0). Default: 1.0 (100%).
    /// </summary>
    public static float Volume { get; set; } = 1.0f;
    
    /// <summary>
    /// Event raised during playback with current level (0-1).
    /// </summary>
    public static event Action<float>? OnPlaybackLevel;

    public static async Task PlayWavBytesAsync(byte[] wavData, CancellationToken cancellationToken = default)
    {
        if (wavData == null || wavData.Length == 0)
            return;
        if (!IsValidWave(wavData))
        {
            Console.WriteLine($"[TTS playback error] Payload is not a valid WAV. Bytes={wavData.Length}");
            return;
        }
        await PlayWavAsync(wavData, cancellationToken);
    }

    private static Task PlayWavAsync(byte[] wavData, CancellationToken cancellationToken)
    {
        // Defensive checks
        if (wavData == null || wavData.Length == 0)
            return Task.CompletedTask;

        // Use Task.Run but ensure the thread is STA which is sometimes required for audio
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                // Read-only MemoryStream from the provided bytes
                using var ms = new MemoryStream(wavData, 0, wavData.Length, writable: false, publiclyVisible: true);
                using var reader = new WaveFileReader(ms);
                if (!TryPlayWithWasapi(reader, cancellationToken))
                    PlayWithWaveOut(reader, cancellationToken);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS playback error] {ex.GetType().Name}: {ex.Message}");
                tcs.SetException(ex);
            }
        });
        
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    public static Task PlayMp3BytesAsync(byte[] mp3Data, CancellationToken cancellationToken = default)
    {
        if (mp3Data == null || mp3Data.Length == 0)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(mp3Data, 0, mp3Data.Length, writable: false, publiclyVisible: true);
                using var reader = new Mp3FileReader(ms);
                if (!TryPlayWithWasapi(reader, cancellationToken))
                    PlayWithWaveOut(reader, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS playback error] {ex.GetType().Name}: {ex.Message}");
            }
        }, cancellationToken);
    }

    private static bool TryPlayWithWasapi(IWaveProvider provider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(OutputDeviceId))
            return false;

        try
        {
            using var device = new MMDeviceEnumerator().GetDevice(OutputDeviceId);
            using var output = new WasapiOut(device, AudioClientShareMode.Shared, false, 100);
            
            // Wrap with volume control
            var volumeProvider = new VolumeSampleProvider(provider.ToSampleProvider()) { Volume = Volume };
            var levelProvider = new LevelMonitorSampleProvider(volumeProvider, OnPlaybackLevel);
            
            output.Init(levelProvider.ToWaveProvider());
            output.Play();
            while (output.PlaybackState == PlaybackState.Playing &&
                   !cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(50);
            }
            output.Stop();
            OnPlaybackLevel?.Invoke(0); // Reset level on stop
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TTS playback fallback to WaveOut] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void PlayWithWaveOut(IWaveProvider provider, CancellationToken cancellationToken)
    {
        using var output = new WaveOutEvent { DeviceNumber = OutputDeviceNumber };
        
        // Wrap with volume control
        var volumeProvider = new VolumeSampleProvider(provider.ToSampleProvider()) { Volume = Volume };
        var levelProvider = new LevelMonitorSampleProvider(volumeProvider, OnPlaybackLevel);
        
        output.Init(levelProvider.ToWaveProvider());
        output.Play();
        while (output.PlaybackState == PlaybackState.Playing &&
               !cancellationToken.IsCancellationRequested)
        {
            Thread.Sleep(50);
        }
        output.Stop();
        OnPlaybackLevel?.Invoke(0); // Reset level on stop
    }

    private static bool IsValidWave(byte[] data)
    {
        if (data == null || data.Length < 44) return false;
        // "RIFF"
        if (!(data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'))
            return false;
        // "WAVE"
        if (!(data[8] == (byte)'W' && data[9] == (byte)'A' && data[10] == (byte)'V' && data[11] == (byte)'E'))
            return false;
        // Chunk size sanity: bytes 4-7 (little endian) should not exceed buffer length
        int reportedSize = data[4] | (data[5] << 8) | (data[6] << 16) | (data[7] << 24);
        if (reportedSize <= 0 || reportedSize + 8 > data.Length)
            return false;
        return true;
    }
}

/// <summary>
/// Sample provider that monitors audio level and raises events.
/// </summary>
internal class LevelMonitorSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action<float>? _onLevel;
    private int _sampleCount;
    private float _peakLevel;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public LevelMonitorSampleProvider(ISampleProvider source, Action<float>? onLevel)
    {
        _source = source;
        _onLevel = onLevel;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        
        if (_onLevel != null && read > 0)
        {
            // Track peak level
            for (int i = offset; i < offset + read; i++)
            {
                float abs = Math.Abs(buffer[i]);
                if (abs > _peakLevel) _peakLevel = abs;
            }
            
            _sampleCount += read;
            
            // Report level approximately every 50ms worth of samples
            int reportInterval = WaveFormat.SampleRate / 20; // ~20 FPS
            if (_sampleCount >= reportInterval)
            {
                _onLevel.Invoke(Math.Min(1.0f, _peakLevel));
                _peakLevel = 0;
                _sampleCount = 0;
            }
        }
        
        return read;
    }
}
