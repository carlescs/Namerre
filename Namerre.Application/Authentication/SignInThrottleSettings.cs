namespace Namerre.Application.Authentication;

/// <summary>
/// How <see cref="SignInThrottle"/> slows down guessing against one account. Configuration
/// (<c>Namerre:Authentication:SignInThrottle</c>) may change the defaults within the ranges <see cref="IsValid"/>
/// checks; out-of-range values fail startup.
/// </summary>
public sealed record SignInThrottleSettings
{
    public const int DefaultFreeFailures = 5;
    public const int DefaultFirstDelaySeconds = 1;
    public const int DefaultMaximumDelaySeconds = 30;
    public const int DefaultFailureMemoryMinutes = 15;
    public const int DefaultMaximumTracked = 10_000;
    public const int CeilingFreeFailures = 100;
    public const int CeilingDelaySeconds = 300;
    public const int CeilingFailureMemoryMinutes = 24 * 60;
    public const int MinimumTracked = 100;
    public const int CeilingTracked = 1_000_000;

    public static SignInThrottleSettings Default { get; } = new();

    /// <summary>False turns the per-account slowdown off; the per-address limits still apply.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Failed sign-ins on an account before attempts on it are spaced out.</summary>
    public int FreeFailures { get; init; } = DefaultFreeFailures;
    /// <summary>The spacing after the first failure beyond the free ones; it doubles with each further one.</summary>
    public TimeSpan FirstDelay { get; init; } = TimeSpan.FromSeconds(DefaultFirstDelaySeconds);
    /// <summary>The largest spacing; an attempt that would wait longer is refused.</summary>
    public TimeSpan MaximumDelay { get; init; } = TimeSpan.FromSeconds(DefaultMaximumDelaySeconds);
    /// <summary>Failures are forgotten after this long without another one.</summary>
    public TimeSpan FailureMemory { get; init; } = TimeSpan.FromMinutes(DefaultFailureMemoryMinutes);
    /// <summary>Most accounts (and devices) tracked at once; the least recently failed makes room.</summary>
    public int MaximumTracked { get; init; } = DefaultMaximumTracked;

    /// <summary>
    /// Whether every value is in range: 1 to <see cref="CeilingFreeFailures"/> free failures, delays from one second
    /// to <see cref="CeilingDelaySeconds"/> with the first no larger than the maximum, a failure memory from one
    /// minute to a day, and <see cref="MinimumTracked"/> to <see cref="CeilingTracked"/> tracked keys.
    /// </summary>
    public bool IsValid => FreeFailures is >= 1 and <= CeilingFreeFailures &&
        FirstDelay >= TimeSpan.FromSeconds(1) && FirstDelay <= MaximumDelay &&
        MaximumDelay <= TimeSpan.FromSeconds(CeilingDelaySeconds) &&
        FailureMemory >= TimeSpan.FromMinutes(1) &&
        FailureMemory <= TimeSpan.FromMinutes(CeilingFailureMemoryMinutes) &&
        MaximumTracked is >= MinimumTracked and <= CeilingTracked;
}
