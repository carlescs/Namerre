namespace Namerre.Application.Authentication;

/// <summary>
/// Slows down password guessing against one account, from any number of addresses, without locking it: after
/// <see cref="SignInThrottleSettings.FreeFailures"/> failed sign-ins, attempts on that account are spaced out by a
/// delay that doubles with each further failure up to <see cref="SignInThrottleSettings.MaximumDelay"/>. Attempts
/// wait for their turn before the password is checked, so a guess cannot be read early from a fast answer; an
/// attempt that would wait longer than the maximum delay is refused without checking the password. A device the user signed in from before
/// presents its own key and is not slowed down by failures from elsewhere.
/// </summary>
/// <remarks>
/// Kept in memory: a restart forgets the failures, which the per-address limits cover. Unknown usernames are tracked
/// like real ones, so the answers do not reveal which accounts exist. At most
/// <see cref="SignInThrottleSettings.MaximumTracked"/> keys are kept; the least recently failed one makes room.
/// </remarks>
public sealed class SignInThrottle(SignInThrottleSettings? settings = null, TimeProvider? time = null)
{
    private readonly SignInThrottleSettings settings = settings ?? SignInThrottleSettings.Default;
    private readonly TimeProvider time = time ?? TimeProvider.System;

    public SignInThrottleSettings Settings => settings;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);

    /// <summary>The throttling key of attempts at a username from an unknown device.</summary>
    public static string AccountKey(string username) => "account:" + username.Trim().ToUpperInvariant();

    /// <summary>The throttling key of attempts from a device the user signed in from before.</summary>
    public static string DeviceKey(long userId, string deviceId) => $"device:{userId}:{deviceId}";

    /// <summary>
    /// Reserves the next turn for an attempt under <paramref name="key"/>: how long to wait before checking the
    /// password, or null when the attempt is refused because the account is being guessed at too quickly.
    /// </summary>
    public TimeSpan? Reserve(string key)
    {
        if (!settings.Enabled)
            return TimeSpan.Zero;
        var now = time.GetUtcNow();
        lock (entries)
        {
            if (Current(key, now) is not { } entry || entry.Failures < settings.FreeFailures)
                return TimeSpan.Zero;
            var start = entry.NextTurn > now ? entry.NextTurn : now;
            var wait = start - now;
            if (wait > settings.MaximumDelay)
                return null;
            entries[key] = entry with { NextTurn = start + Step(entry.Failures) };
            return wait;
        }
    }

    /// <summary>Waits for the turn <see cref="Reserve"/> gave.</summary>
    public Task Wait(TimeSpan wait, CancellationToken cancellationToken)
        => wait > TimeSpan.Zero ? Task.Delay(wait, time, cancellationToken) : Task.CompletedTask;

    public void RecordFailure(string key)
    {
        if (!settings.Enabled)
            return;
        var now = time.GetUtcNow();
        lock (entries)
        {
            var entry = Current(key, now) ?? new Entry(0, now, now);
            if (!entries.ContainsKey(key) && entries.Count >= settings.MaximumTracked)
                entries.Remove(entries.MinBy(pair => pair.Value.LastFailure).Key);
            entries[key] = entry with { Failures = entry.Failures + 1, LastFailure = now };
        }
    }

    public void RecordSuccess(string key)
    {
        lock (entries)
            entries.Remove(key);
    }

    /// <summary>The key's failures, or null when it has none worth remembering.</summary>
    private Entry? Current(string key, DateTimeOffset now)
    {
        if (!entries.TryGetValue(key, out var entry))
            return null;
        if (now - entry.LastFailure <= settings.FailureMemory || entry.NextTurn > now)
            return entry;
        entries.Remove(key);
        return null;
    }

    private TimeSpan Step(int failures)
    {
        var doublings = Math.Min(failures - settings.FreeFailures, 30);
        var step = settings.FirstDelay * Math.Pow(2, doublings);
        return step > settings.MaximumDelay ? settings.MaximumDelay : step;
    }

    private sealed record Entry(int Failures, DateTimeOffset LastFailure, DateTimeOffset NextTurn);
}
