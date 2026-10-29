using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Resilience pipeline for the TMDB HTTP client; strategies run outermost first.</summary>
public static class TmdbResilience
{
    public const string PipelineName = "tmdb";
    public const int ConcurrentLookups = 4;
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);

    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline) => pipeline
        // One permit covers a lookup and all of its retries; extra lookups are rejected rather than queued.
        .AddConcurrencyLimiter(ConcurrentLookups, queueLimit: 0)
        .AddTimeout(TotalTimeout)
        .AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 2,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(500),
            // Retry-After delays are not capped by the strategy, so they are ignored, and rate limits are not retried:
            // the user sees TMDB's own wait time instead of a lookup that stalls until the overall timeout.
            ShouldRetryAfterHeader = false,
            ShouldHandle = arguments => ValueTask.FromResult(
                arguments.Outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
                && HttpClientResiliencePredicates.IsTransient(arguments.Outcome))
        })
        .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            // Sized for a single administrator's traffic; the library default needs 100 calls per window.
            FailureRatio = 0.5,
            MinimumThroughput = 4,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30)
        })
        .AddTimeout(AttemptTimeout);
}
