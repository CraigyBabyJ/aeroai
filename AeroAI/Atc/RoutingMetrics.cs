using System;
using System.Threading;

namespace AeroAI.Atc;

/// <summary>
/// Thread-safe in-memory metrics for routing decisions.
/// </summary>
public sealed class RoutingMetrics
{
    private long _totalTranscripts;
    private long _proceduralHits;
    private long _llmCalls;
    private long _sayAgainCount;
    private long _llmFailures;
    private long _unusableTranscripts;

    /// <summary>
    /// Total number of transcripts processed.
    /// </summary>
    public long TotalTranscripts => Interlocked.Read(ref _totalTranscripts);

    /// <summary>
    /// Number of transcripts matched by procedural intents.
    /// </summary>
    public long ProceduralHits => Interlocked.Read(ref _proceduralHits);

    /// <summary>
    /// Number of LLM calls made.
    /// </summary>
    public long LlmCalls => Interlocked.Read(ref _llmCalls);

    /// <summary>
    /// Number of "say again" responses.
    /// </summary>
    public long SayAgainCount => Interlocked.Read(ref _sayAgainCount);

    /// <summary>
    /// Number of LLM call failures.
    /// </summary>
    public long LlmFailures => Interlocked.Read(ref _llmFailures);

    /// <summary>
    /// Number of unusable transcripts.
    /// </summary>
    public long UnusableTranscripts => Interlocked.Read(ref _unusableTranscripts);

    /// <summary>
    /// Increment total transcripts counter.
    /// </summary>
    public void IncrementTotalTranscripts()
    {
        Interlocked.Increment(ref _totalTranscripts);
    }

    /// <summary>
    /// Increment procedural hits counter.
    /// </summary>
    public void IncrementProceduralHits()
    {
        Interlocked.Increment(ref _proceduralHits);
    }

    /// <summary>
    /// Increment LLM calls counter.
    /// </summary>
    public void IncrementLlmCalls()
    {
        Interlocked.Increment(ref _llmCalls);
    }

    /// <summary>
    /// Increment say again counter.
    /// </summary>
    public void IncrementSayAgain()
    {
        Interlocked.Increment(ref _sayAgainCount);
    }

    /// <summary>
    /// Increment LLM failures counter.
    /// </summary>
    public void IncrementLlmFailures()
    {
        Interlocked.Increment(ref _llmFailures);
    }

    /// <summary>
    /// Increment unusable transcripts counter.
    /// </summary>
    public void IncrementUnusableTranscripts()
    {
        Interlocked.Increment(ref _unusableTranscripts);
    }

    /// <summary>
    /// Get a snapshot of current metrics.
    /// </summary>
    public RoutingMetricsSnapshot GetSnapshot()
    {
        return new RoutingMetricsSnapshot
        {
            TotalTranscripts = TotalTranscripts,
            ProceduralHits = ProceduralHits,
            LlmCalls = LlmCalls,
            SayAgainCount = SayAgainCount,
            LlmFailures = LlmFailures,
            UnusableTranscripts = UnusableTranscripts
        };
    }

    /// <summary>
    /// Reset all counters to zero.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _totalTranscripts, 0);
        Interlocked.Exchange(ref _proceduralHits, 0);
        Interlocked.Exchange(ref _llmCalls, 0);
        Interlocked.Exchange(ref _sayAgainCount, 0);
        Interlocked.Exchange(ref _llmFailures, 0);
        Interlocked.Exchange(ref _unusableTranscripts, 0);
    }
}

/// <summary>
/// Immutable snapshot of routing metrics.
/// </summary>
public sealed class RoutingMetricsSnapshot
{
    public long TotalTranscripts { get; init; }
    public long ProceduralHits { get; init; }
    public long LlmCalls { get; init; }
    public long SayAgainCount { get; init; }
    public long LlmFailures { get; init; }
    public long UnusableTranscripts { get; init; }

    public double ProceduralHitRate => TotalTranscripts > 0 ? (double)ProceduralHits / TotalTranscripts : 0.0;
    public double LlmCallRate => TotalTranscripts > 0 ? (double)LlmCalls / TotalTranscripts : 0.0;
    public double SayAgainRate => TotalTranscripts > 0 ? (double)SayAgainCount / TotalTranscripts : 0.0;
}

