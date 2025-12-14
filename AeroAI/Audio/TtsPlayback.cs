using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace AeroAI.Audio;

/// <summary>
/// Simple WAV playback helper from byte[] or temp file.
/// </summary>
public static class TtsPlayback
{
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

        return Task.Run(() =>
        {
            try
            {
                // Read-only MemoryStream from the provided bytes
                using var ms = new MemoryStream(wavData, 0, wavData.Length, writable: false, publiclyVisible: true);
                using var reader = new WaveFileReader(ms);
                using var output = new WaveOutEvent();
                output.Init(reader);
                output.Play();

                // Wait until playback completes or cancellation requested
                while (output.PlaybackState == PlaybackState.Playing &&
                       !cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(50);
                }

                output.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS playback error] {ex.GetType().Name}: {ex.Message}");
            }
        }, cancellationToken);
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
                using var waveOut = new WaveOutEvent();
                waveOut.Init(reader);
                waveOut.Play();

                while (waveOut.PlaybackState == PlaybackState.Playing &&
                       !cancellationToken.IsCancellationRequested)
                {
                    Thread.Sleep(50);
                }

                waveOut.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS playback error] {ex.GetType().Name}: {ex.Message}");
            }
        }, cancellationToken);
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
