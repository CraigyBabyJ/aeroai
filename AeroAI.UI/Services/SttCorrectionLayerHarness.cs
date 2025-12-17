#if DEBUG
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AeroAI.UI.Services;

/// <summary>
/// Lightweight verification harness for SttCorrectionLayer. Call Run() from a debug context to validate rules quickly.
/// </summary>
internal static class SttCorrectionLayerHarness
{
    public static void Run()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"stt_corrections_test_{Guid.NewGuid():N}.json");
        File.WriteAllText(tempPath,
@"{
  ""enabled"": true,
  ""rules"": [
    { ""name"": ""Ground misheard as Graham"", ""type"": ""regex"", ""pattern"": ""\\bgraham\\b"", ""replacement"": ""ground"", ""ignoreCase"": true },
    { ""name"": ""Radio chat -> radio check"", ""type"": ""regex"", ""pattern"": ""\\bradio\\s+chat\\b"", ""replacement"": ""radio check"", ""ignoreCase"": true }
  ]
}");

        try
        {
            var logs = new System.Collections.Concurrent.ConcurrentBag<string>();
            var layer = new SttCorrectionLayer(logs.Add, tempPath, enableHotReload: false);

            var res1 = layer.Apply("Good morning graham");
            Debug.Assert(res1 == "Good morning ground", "Graham -> ground");

            var res2 = layer.Apply("request radio chat");
            Debug.Assert(res2 == "request radio check", "radio chat -> radio check");

            var res3 = layer.Apply("program");
            Debug.Assert(res3 == "program", "word boundaries prevent proground");

            // Ensure invalid JSON keeps last known good rules.
            File.WriteAllText(tempPath, "{ this is not valid json");
            layer.ForceReloadForTests();
            var res4 = layer.Apply("holding graham short runway two seven");
            Debug.Assert(res4.Contains("ground"), "invalid JSON keeps last-known-good rules");
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* ignore cleanup */ }
        }
    }
}
#endif
