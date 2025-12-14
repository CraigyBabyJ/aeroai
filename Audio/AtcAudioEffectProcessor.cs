using System;
using System.Collections.Generic;
using System.IO;
using AeroAI.Atc;
using AtcNavDataDemo.Config;
using NAudio.Wave;

namespace AeroAI.Audio;

/// <summary>
/// Optional post-processing helper for ATC audio (radio tone + squelch tail).
/// Intended for hosts that already TTS to audio; not wired into the console loop.
/// </summary>
	public sealed class AtcAudioEffectProcessor
	{
		private readonly AudioEffectsConfig _config;

		public AtcAudioEffectProcessor(AudioEffectsConfig config)
		{
			_config = config;
		}

	/// <summary>
	/// Apply effects to a WAV byte array (PCM/float) and return WAV bytes.
	/// Caller can re-encode to MP3 as needed.
	/// </summary>
	public byte[] ApplyEffectsToWaveBytes(byte[] waveBytes, AtcUnit unit)
	{
		if (_config == null || !_config.Enabled)
		{
			return waveBytes;
		}

		if (!_config.Profiles.TryGetValue(unit.ToString(), out var profile) || profile == null || !profile.Enabled)
		{
			return waveBytes;
		}

		using var inputMs = new MemoryStream(waveBytes);
		using var reader = new WaveFileReader(inputMs);
		var sampleProvider = reader.ToSampleProvider();

		ISampleProvider processed = sampleProvider;

		processed = new RadioEffectSampleProvider(
			processed,
			profile.BandpassLowHz,
			profile.BandpassHighHz,
			profile.CompressionAmount,
			profile.NoiseLevel,
			profile.DryWetMix);

		byte[] effected = WriteToWaveBytes(processed, reader.WaveFormat);

		var resolvedTail = ResolveTailPath(profile.SquelchTailFile);

		if (!string.IsNullOrWhiteSpace(resolvedTail))
		{
			var appended = TryAppendTail(effected, profile, reader.WaveFormat, resolvedTail);
			if (appended is not null)
			{
				return appended;
			}
		}

		return effected;
	}

	private static byte[] WriteToWaveBytes(ISampleProvider provider, WaveFormat format)
	{
		using var outMs = new MemoryStream();
		using (var writer = new WaveFileWriter(outMs, format))
		{
			float[] buffer = new float[format.SampleRate * format.Channels];
			int read;
			while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
			{
				writer.WriteSamples(buffer, 0, read);
			}
		}
		return outMs.ToArray();
	}

	private static byte[]? TryAppendTail(byte[] effected, AudioEffectsProfile profile, WaveFormat mainFormat, string tailPath)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(tailPath))
			{
				return null;
			}

			if (!File.Exists(tailPath))
			{
				Console.WriteLine($"[AUDIOFX] Tail file not found: {tailPath}");
				return null;
			}

			using var tailReader = new AudioFileReader(tailPath);
			ISampleProvider tailProvider = tailReader.ToSampleProvider();

			var tail = new List<float>();
			float[] temp = new float[mainFormat.SampleRate * mainFormat.Channels];
			int read;
			while ((read = tailProvider.Read(temp, 0, temp.Length)) > 0)
			{
				for (int i = 0; i < read; i++)
				{
					float sample = temp[i] * (float)Math.Pow(10.0, profile.SquelchTailGainDb / 20.0);
					tail.Add(sample);
				}
			}

			if (tail.Count == 0)
			{
				return null;
			}

			using var mainMs = new MemoryStream(effected);
			using var mainReader = new WaveFileReader(mainMs);
			var mainSamples = new List<float>();
			float[] mainBuf = new float[mainReader.SampleCount];
			int got = mainReader.ToSampleProvider().Read(mainBuf, 0, mainBuf.Length);
			for (int i = 0; i < got; i++)
			{
				mainSamples.Add(mainBuf[i]);
			}

			mainSamples.AddRange(tail);

			var concatenated = mainSamples.ToArray();
			using var outMs = new MemoryStream();
			using (var writer = new WaveFileWriter(outMs, mainReader.WaveFormat))
			{
				writer.WriteSamples(concatenated, 0, concatenated.Length);
			}
			Console.WriteLine($"[AUDIOFX] Appended squelch tail: {tailPath}");
			return outMs.ToArray();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[AUDIOFX] Failed to append tail: {ex.Message}");
			return null;
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
