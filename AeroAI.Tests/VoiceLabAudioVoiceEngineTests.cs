using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AeroAI.Atc;
using AeroAI.Audio;
using Xunit;

namespace AeroAI.Tests;

public class VoiceLabAudioVoiceEngineTests
{
    [Fact]
    public async Task SpeakAsync_SendsRoleAndFacilityIcao()
    {
        var client = new StubTtsClient();
        var flight = new FlightContext
        {
            OriginIcao = "EGLL",
            DestinationIcao = "LFPG",
            CurrentPhase = FlightPhase.Taxi_Out,
            CurrentAtcUnit = AtcUnit.Ground
        };
        var engine = new VoiceLabAudioVoiceEngine(
            client,
            () => flight,
            playWavAsync: (_, __) => Task.CompletedTask);

        await engine.SpeakAsync("Test", role: "tower", facilityIcao: "EGLL");

        Assert.NotNull(client.LastRequest);
        Assert.Equal("auto", client.LastRequest!.VoiceId);
        Assert.Equal("tower", client.LastRequest.Role);
        Assert.Equal("EGLL", client.LastRequest.AirportIcao);
    }

    private sealed class StubTtsClient : ITtsClient
    {
        public TtsRequest? LastRequest { get; private set; }

        public Task<TtsHealth> HealthAsync(CancellationToken ct = default)
            => Task.FromResult(new TtsHealth { Online = true });

        public Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new TtsResult { WavBytes = CreateSilentWav() });
        }
    }

    private static byte[] CreateSilentWav(int sampleRate = 8000, int durationMs = 50)
    {
        int sampleCount = sampleRate * durationMs / 1000;
        int dataSize = sampleCount * 2;
        int fileSize = 36 + dataSize;

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(fileSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);
        return ms.ToArray();
    }
}
