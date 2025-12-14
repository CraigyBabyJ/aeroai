using NAudio.Dsp;
using NAudio.Wave;

namespace AeroAI.Audio;

/// <summary>
/// Applies a simple VHF-style bandpass, soft drive, noise, and gain.
/// </summary>
public sealed class RadioEffectSampleProvider : ISampleProvider
{
	private readonly ISampleProvider _source;
	private readonly BiQuadFilter _hp;
	private readonly BiQuadFilter _lp;
	private readonly float _drive;
	private readonly float _noiseAmp;
	private readonly float _dryWet;
	private readonly Random _rng = new();

	public RadioEffectSampleProvider(ISampleProvider source, int highPassHz, int lowPassHz, double compressionAmount, double noiseLevel, double dryWetMix)
	{
		_source = source;
		var sr = source.WaveFormat.SampleRate;
		_hp = BiQuadFilter.HighPassFilter(sr, highPassHz, 0.707f);
		_lp = BiQuadFilter.LowPassFilter(sr, lowPassHz, 0.707f);

		var comp = Math.Clamp(compressionAmount, 0.0, 1.0);
		_drive = (float)(1.0 + comp * 10.0); // gentle drive

		var noise = Math.Clamp(noiseLevel, 0.0, 1.0);
		_noiseAmp = (float)(noise * 0.05); // small amplitude noise

		_dryWet = (float)Math.Clamp(dryWetMix, 0.0, 1.0);
	}

	public WaveFormat WaveFormat => _source.WaveFormat;

	public int Read(float[] buffer, int offset, int count)
	{
		var read = _source.Read(buffer, offset, count);
		for (int i = 0; i < read; i++)
		{
			var idx = offset + i;
			float dry = buffer[idx];
			float wet = dry;

			wet = _hp.Transform(wet);
			wet = _lp.Transform(wet);

			// Soft drive/tanh
			wet = (float)Math.Tanh(wet * _drive);

			// Add light noise
			if (_noiseAmp > 0)
			{
				wet += (float)((_rng.NextDouble() * 2.0 - 1.0) * _noiseAmp);
			}

			// Dry/wet mix
			buffer[idx] = dry * (1.0f - _dryWet) + wet * _dryWet;
		}
		return read;
	}
}
