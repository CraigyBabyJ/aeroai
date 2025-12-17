using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AeroAI.UI.Services;

/// <summary>
/// Captures microphone audio to a temporary WAV file (16-bit PCM, mono, 16 kHz).
/// </summary>
public sealed class MicrophoneRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private WasapiCapture? _wasapiCapture;
    private WaveFileWriter? _writer;
    private TaskCompletionSource<bool>? _stopTcs;
    private string? _wavPath;
    private bool _disposed;

    public bool IsRecording { get; private set; }
    public string? DeviceId { get; set; }
    public int? DeviceNumber { get; set; }
    
    /// <summary>
    /// Software mic gain in dB. Range: -12 to +12.
    /// </summary>
    public double GainDb { get; set; } = 0.0;

    /// <summary>
    /// Event raised when audio level is calculated (0.0 to 1.0, post-gain).
    /// </summary>
    public event Action<double>? OnAudioLevel;
    
    /// <summary>
    /// Event raised when clipping is detected (peak > -1 dBFS).
    /// </summary>
    public event Action? OnClipping;

    /// <summary>
    /// Starts recording. If already recording, does nothing.
    /// </summary>
    public void StartRecording()
    {
        if (_disposed || IsRecording)
            return;

        _wavPath = Path.Combine(Path.GetTempPath(), $"aeroai_ptt_{Guid.NewGuid():N}.wav");
        _stopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!string.IsNullOrWhiteSpace(DeviceId))
        {
            try
            {
                var device = new MMDeviceEnumerator().GetDevice(DeviceId);
                _wasapiCapture = new WasapiCapture(device);
                _wasapiCapture.DataAvailable += OnWasapiDataAvailable;
                _wasapiCapture.RecordingStopped += OnRecordingStopped;
            }
            catch
            {
                _wasapiCapture = null;
            }
        }
        else
        {
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16_000, 16, 1),
                DeviceNumber = DeviceNumber.GetValueOrDefault(-1)
            };

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
        }

        var format = _wasapiCapture?.WaveFormat ?? _waveIn?.WaveFormat ?? new WaveFormat(16_000, 16, 1);
        _writer = new WaveFileWriter(_wavPath, format);
        if (_wasapiCapture != null)
            _wasapiCapture.StartRecording();
        else
            _waveIn?.StartRecording();
        IsRecording = true;
    }

    /// <summary>
    /// Stops recording and returns the recorded WAV path, or null if nothing was recorded.
    /// </summary>
    public async Task<string?> StopRecordingAsync(CancellationToken cancellationToken)
    {
        if (!IsRecording || _disposed)
            return null;

        IsRecording = false;
        _waveIn?.StopRecording();
        _wasapiCapture?.StopRecording();

        if (_stopTcs != null)
        {
            using var reg = cancellationToken.Register(() => _stopTcs.TrySetCanceled(cancellationToken));
            await _stopTcs.Task.ConfigureAwait(false);
        }

        var path = _wavPath;
        _wavPath = null;
        return path;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        // Apply software gain and write
        var processed = ApplyGain16Bit(e.Buffer, e.BytesRecorded);
        _writer?.Write(processed, 0, e.BytesRecorded);
        RaiseAudioLevel(processed, e.BytesRecorded, 16);
    }

    private void OnWasapiDataAvailable(object? sender, WaveInEventArgs e)
    {
        var bitsPerSample = _wasapiCapture?.WaveFormat?.BitsPerSample ?? 32;
        byte[] processed;
        
        if (bitsPerSample == 32)
            processed = ApplyGain32BitFloat(e.Buffer, e.BytesRecorded);
        else
            processed = ApplyGain16Bit(e.Buffer, e.BytesRecorded);
            
        _writer?.Write(processed, 0, e.BytesRecorded);
        RaiseAudioLevel(processed, e.BytesRecorded, bitsPerSample);
    }

    /// <summary>
    /// Apply software gain to 16-bit PCM audio.
    /// </summary>
    private byte[] ApplyGain16Bit(byte[] buffer, int bytesRecorded)
    {
        if (Math.Abs(GainDb) < 0.01)
            return buffer; // No gain adjustment needed
            
        double gainMultiplier = Math.Pow(10.0, GainDb / 20.0);
        var result = new byte[bytesRecorded];
        bool clipped = false;
        
        for (int i = 0; i < bytesRecorded - 1; i += 2)
        {
            short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            double amplified = sample * gainMultiplier;
            
            // Clamp to prevent overflow
            if (amplified > 32767)
            {
                amplified = 32767;
                clipped = true;
            }
            else if (amplified < -32768)
            {
                amplified = -32768;
                clipped = true;
            }
            
            short clampedSample = (short)amplified;
            result[i] = (byte)(clampedSample & 0xFF);
            result[i + 1] = (byte)((clampedSample >> 8) & 0xFF);
        }
        
        if (clipped)
            OnClipping?.Invoke();
            
        return result;
    }

    /// <summary>
    /// Apply software gain to 32-bit float audio.
    /// </summary>
    private byte[] ApplyGain32BitFloat(byte[] buffer, int bytesRecorded)
    {
        if (Math.Abs(GainDb) < 0.01)
            return buffer; // No gain adjustment needed
            
        double gainMultiplier = Math.Pow(10.0, GainDb / 20.0);
        var result = new byte[bytesRecorded];
        bool clipped = false;
        
        for (int i = 0; i < bytesRecorded - 3; i += 4)
        {
            float sample = BitConverter.ToSingle(buffer, i);
            float amplified = (float)(sample * gainMultiplier);
            
            // Clamp to prevent overflow
            if (amplified > 1.0f)
            {
                amplified = 1.0f;
                clipped = true;
            }
            else if (amplified < -1.0f)
            {
                amplified = -1.0f;
                clipped = true;
            }
            
            var bytes = BitConverter.GetBytes(amplified);
            Array.Copy(bytes, 0, result, i, 4);
        }
        
        if (clipped)
            OnClipping?.Invoke();
            
        return result;
    }

    /// <summary>
    /// Calculate RMS level from audio buffer (post-gain) and raise event.
    /// </summary>
    private void RaiseAudioLevel(byte[] buffer, int bytesRecorded, int bitsPerSample)
    {
        if (OnAudioLevel == null || bytesRecorded == 0)
            return;

        double sum = 0;
        double peak = 0;
        int sampleCount = 0;

        if (bitsPerSample == 16)
        {
            // 16-bit audio
            for (int i = 0; i < bytesRecorded - 1; i += 2)
            {
                short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                double normalized = sample / 32768.0;
                sum += normalized * normalized;
                peak = Math.Max(peak, Math.Abs(normalized));
                sampleCount++;
            }
        }
        else if (bitsPerSample == 32)
        {
            // 32-bit float audio (WASAPI default)
            for (int i = 0; i < bytesRecorded - 3; i += 4)
            {
                float sample = BitConverter.ToSingle(buffer, i);
                sum += sample * sample;
                peak = Math.Max(peak, Math.Abs(sample));
                sampleCount++;
            }
        }

        if (sampleCount > 0)
        {
            double rms = Math.Sqrt(sum / sampleCount);
            // Scale to 0-1 range (RMS of 0.1 is fairly loud speech)
            double level = Math.Min(1.0, rms * 3.0);
            OnAudioLevel?.Invoke(level);
            
            // Check for clipping (peak > -1 dBFS = ~0.89)
            if (peak > 0.89)
                OnClipping?.Invoke();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        _writer?.Dispose();
        _writer = null;
        _waveIn?.Dispose();
        _waveIn = null;
        _wasapiCapture?.Dispose();
        _wasapiCapture = null;
        _stopTcs?.TrySetResult(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        try
        {
            if (IsRecording)
            {
                _waveIn?.StopRecording();
                _wasapiCapture?.StopRecording();
            }
        }
        catch
        {
            // ignored
        }

        _writer?.Dispose();
        _waveIn?.Dispose();
        _wasapiCapture?.Dispose();
    }
}
