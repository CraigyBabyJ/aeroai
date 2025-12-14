using System;
using System.IO;
using AeroAI.Atc;
using AtcNavDataDemo.Config;
using NAudio.Wave;

namespace AeroAI.Audio;

/// <summary>
/// Radio-effect processor that applies VHF radio effects and squelch tail to WAV audio.
/// </summary>
public static class RadioEffectProcessor
{
	// Cache the tail samples to avoid re-reading from disk
	private static float[]? _cachedTailSamples;
	private static string? _cachedTailPath;

	/// <summary>
	/// Apply radio effects and squelch tail to WAV audio. Fully in-memory processing.
	/// </summary>
	public static byte[] ApplyToWavResponse(byte[] wavData, AtcUnit unit)
	{
		if (wavData == null || wavData.Length < 44)
			return wavData;

		var profile = AudioEffectsConfigStore.GetProfileForUnit(unit);
		if (profile == null)
			return wavData;

		try
		{
			// Parse WAV header to get format info
			if (!TryParseWavHeader(wavData, out var format, out var audioFormat, out var dataOffset, out var dataLength))
			{
				Console.WriteLine("[AUDIOFX] Failed to parse WAV header");
				return wavData;
			}
			
			Console.WriteLine($"[AUDIOFX] WAV: {format.SampleRate}Hz, {format.BitsPerSample}bit, {format.Channels}ch, audioFormat={audioFormat}, dataLen={dataLength}");

			// Convert PCM bytes to float samples
			var inputSamples = PcmToFloat(wavData, dataOffset, dataLength, format, audioFormat);
			if (inputSamples == null || inputSamples.Length == 0)
			{
				Console.WriteLine("[AUDIOFX] Failed to convert PCM to float");
				return wavData;
			}
			
			Console.WriteLine($"[AUDIOFX] Converted {inputSamples.Length} samples");

			// Apply radio effects in-place
			ApplyRadioEffects(inputSamples, format.SampleRate, profile);

			// Append tail if configured
			var tailPath = ResolveTailPath(profile.SquelchTailFile);
			float[]? outputSamples = inputSamples;
			
			if (!string.IsNullOrWhiteSpace(tailPath))
			{
				var tailSamples = GetCachedTailSamples(tailPath, profile.SquelchTailGainDb);
				if (tailSamples != null && tailSamples.Length > 0)
				{
					outputSamples = new float[inputSamples.Length + tailSamples.Length];
					Array.Copy(inputSamples, 0, outputSamples, 0, inputSamples.Length);
					Array.Copy(tailSamples, 0, outputSamples, inputSamples.Length, tailSamples.Length);
					Console.WriteLine("[AUDIOFX] Applied effects + tail");
				}
				else
				{
					Console.WriteLine("[AUDIOFX] Applied effects (tail load failed)");
				}
			}
			else
			{
				Console.WriteLine("[AUDIOFX] Applied effects");
			}

			// Convert back to WAV bytes
			return FloatToWav(outputSamples, format.SampleRate, format.Channels);
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[AUDIOFX] Error: {ex.Message}");
			return wavData;
		}
	}

	private static bool TryParseWavHeader(byte[] data, out WaveFormat format, out int audioFormat, out int dataOffset, out int dataLength)
	{
		format = new WaveFormat(24000, 16, 1);
		audioFormat = 1; // PCM
		dataOffset = 0;
		dataLength = 0;

		if (data.Length < 44) return false;
		
		// Check RIFF header
		if (data[0] != 'R' || data[1] != 'I' || data[2] != 'F' || data[3] != 'F') return false;
		if (data[8] != 'W' || data[9] != 'A' || data[10] != 'V' || data[11] != 'E') return false;

		int pos = 12;
		while (pos < data.Length - 8)
		{
			string chunkId = $"{(char)data[pos]}{(char)data[pos + 1]}{(char)data[pos + 2]}{(char)data[pos + 3]}";
			int chunkSize = BitConverter.ToInt32(data, pos + 4);

			if (chunkId == "fmt ")
			{
				audioFormat = BitConverter.ToInt16(data, pos + 8); // 1=PCM, 3=IEEE float
				int channels = BitConverter.ToInt16(data, pos + 10);
				int sampleRate = BitConverter.ToInt32(data, pos + 12);
				int bitsPerSample = BitConverter.ToInt16(data, pos + 22);
				
				format = new WaveFormat(sampleRate, bitsPerSample, channels);
			}
			else if (chunkId == "data")
			{
				dataOffset = pos + 8;
				// Handle streaming WAV where chunk size is -1 or very large
				if (chunkSize < 0 || chunkSize > data.Length - dataOffset)
				{
					dataLength = data.Length - dataOffset;
				}
				else
				{
					dataLength = chunkSize;
				}
				return true;
			}

			pos += 8 + chunkSize;
			if (chunkSize % 2 == 1) pos++; // Padding byte
		}

		return false;
	}

	private static float[]? PcmToFloat(byte[] data, int offset, int length, WaveFormat format, int audioFormat)
	{
		int bytesPerSample = format.BitsPerSample / 8;
		int sampleCount = length / bytesPerSample;
		var samples = new float[sampleCount];

		for (int i = 0; i < sampleCount; i++)
		{
			int bytePos = offset + i * bytesPerSample;
			if (bytePos + bytesPerSample > data.Length) break;

			if (audioFormat == 3) // IEEE float
			{
				if (format.BitsPerSample == 32)
					samples[i] = BitConverter.ToSingle(data, bytePos);
				else if (format.BitsPerSample == 64)
					samples[i] = (float)BitConverter.ToDouble(data, bytePos);
			}
			else // PCM (audioFormat == 1)
			{
				if (format.BitsPerSample == 16)
				{
					short sample = BitConverter.ToInt16(data, bytePos);
					samples[i] = sample / 32768f;
				}
				else if (format.BitsPerSample == 24)
				{
					int sample = data[bytePos] | (data[bytePos + 1] << 8) | (data[bytePos + 2] << 16);
					if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
					samples[i] = sample / 8388608f;
				}
				else if (format.BitsPerSample == 32)
				{
					int sample = BitConverter.ToInt32(data, bytePos);
					samples[i] = sample / 2147483648f;
				}
				else if (format.BitsPerSample == 8)
				{
					samples[i] = (data[bytePos] - 128) / 128f;
				}
			}
		}

		return samples;
	}

	private static void ApplyRadioEffects(float[] samples, int sampleRate, AudioEffectsProfile profile)
	{
		// Simple IIR filters for bandpass
		float hpFreq = profile.BandpassLowHz / (float)sampleRate;
		float lpFreq = profile.BandpassHighHz / (float)sampleRate;
		
		// High-pass filter state
		float hpAlpha = 1f / (1f + 2f * MathF.PI * hpFreq);
		float hpPrev = 0, hpPrevIn = 0;
		
		// Low-pass filter state  
		float lpAlpha = 2f * MathF.PI * lpFreq / (1f + 2f * MathF.PI * lpFreq);
		float lpPrev = 0;

		float drive = 1f + (float)profile.CompressionAmount * 8f;
		float noiseAmp = (float)profile.NoiseLevel * 0.04f;
		float dryWet = (float)profile.DryWetMix;
		var rng = new Random(42);

		for (int i = 0; i < samples.Length; i++)
		{
			float dry = samples[i];
			float wet = dry;

			// High-pass
			float hpOut = hpAlpha * (hpPrev + wet - hpPrevIn);
			hpPrevIn = wet;
			hpPrev = hpOut;
			wet = hpOut;

			// Low-pass
			lpPrev += lpAlpha * (wet - lpPrev);
			wet = lpPrev;

			// Soft clipping/drive
			wet = MathF.Tanh(wet * drive);

			// Add noise
			if (noiseAmp > 0)
			{
				wet += (float)(rng.NextDouble() * 2.0 - 1.0) * noiseAmp;
			}

			// Mix
			samples[i] = dry * (1f - dryWet) + wet * dryWet;
		}
	}

	private static byte[] FloatToWav(float[] samples, int sampleRate, int channels)
	{
		using var ms = new MemoryStream();
		var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
		using (var writer = new WaveFileWriter(ms, format))
		{
			writer.WriteSamples(samples, 0, samples.Length);
		}
		return ms.ToArray();
	}

	private static float[]? GetCachedTailSamples(string tailPath, double gainDb)
	{
		if (_cachedTailPath != tailPath || _cachedTailSamples == null)
		{
			try
			{
				using var reader = new AudioFileReader(tailPath);
				var samples = new float[(int)(reader.Length / sizeof(float)) + 1000];
				int total = 0;
				float[] buf = new float[4096];
				int read;
				
				while ((read = reader.Read(buf, 0, buf.Length)) > 0)
				{
					if (total + read > samples.Length)
						Array.Resize(ref samples, samples.Length * 2);
					Array.Copy(buf, 0, samples, total, read);
					total += read;
				}
				
				Array.Resize(ref samples, total);
				_cachedTailSamples = samples;
				_cachedTailPath = tailPath;
			}
			catch
			{
				return null;
			}
		}

		// Apply gain to a copy
		float gain = (float)Math.Pow(10.0, gainDb / 20.0);
		var result = new float[_cachedTailSamples.Length];
		for (int i = 0; i < result.Length; i++)
		{
			result[i] = _cachedTailSamples[i] * gain;
		}
		return result;
	}

	/// <summary>
	/// Legacy method for MP3 input.
	/// </summary>
	public static byte[] ApplyToAtcResponse(byte[] mp3Data, AtcUnit unit)
	{
		// For MP3, we still need temp file for decoding
		if (mp3Data == null || mp3Data.Length == 0)
			return mp3Data;

		try
		{
			var tempMp3 = Path.Combine(Path.GetTempPath(), $"aeroai_{Guid.NewGuid():N}.mp3");
			var tempWav = Path.Combine(Path.GetTempPath(), $"aeroai_{Guid.NewGuid():N}.wav");
			
			try
			{
				File.WriteAllBytes(tempMp3, mp3Data);
				using (var mp3Reader = new Mp3FileReader(tempMp3))
				{
					WaveFileWriter.CreateWaveFile(tempWav, mp3Reader);
				}
				return ApplyToWavResponse(File.ReadAllBytes(tempWav), unit);
			}
			finally
			{
				try { File.Delete(tempMp3); } catch { }
				try { File.Delete(tempWav); } catch { }
			}
		}
		catch
		{
			return mp3Data;
		}
	}

	private static string? ResolveTailPath(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
			return null;

		if (File.Exists(path))
			return path;

		if (!Path.IsPathRooted(path))
		{
			var baseDir = AppContext.BaseDirectory;
			if (!string.IsNullOrWhiteSpace(baseDir))
			{
				var candidate = Path.Combine(baseDir, path);
				if (File.Exists(candidate))
					return candidate;
			}

			var cwd = Directory.GetCurrentDirectory();
			if (!string.IsNullOrWhiteSpace(cwd))
			{
				var candidate = Path.Combine(cwd, path);
				if (File.Exists(candidate))
					return candidate;
			}
		}

		return null;
	}
}
