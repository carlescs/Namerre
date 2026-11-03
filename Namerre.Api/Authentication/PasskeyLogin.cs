using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Configuration;

namespace Namerre.Authentication;

/// <summary>
/// Hosting side of passkeys: which relying party a request is for, and the tickets that carry a WebAuthn ceremony's
/// state through the browser. A ticket is protected and time-limited, bound to its purpose (and, for registration,
/// to the user version that asked), and works once, in this process only.
/// </summary>
public sealed class PasskeyLogin
{
    public const string SignInPurpose = "sign-in";
    public const string RegisterPurpose = "register";
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(5);
    // Bounded by the login rate limit in practice; the cap keeps memory bounded regardless.
    private const int MaximumUsedTickets = 10_000;
    private readonly ITimeLimitedDataProtector protector;
    private readonly TimeProvider time;
    private readonly ConcurrentDictionary<string, DateTimeOffset> used = new(StringComparer.Ordinal);
    private readonly PasskeyRelyingParty? configured;

    public PasskeyLogin(IOptions<NamerreOptions> options, IDataProtectionProvider dataProtection,
        TimeProvider? time = null)
    {
        var settings = options.Value.Authentication.Passkeys;
        Enabled = settings.Enabled;
        this.time = time ?? TimeProvider.System;
        // A random subpurpose ties tickets to this process: the used-ticket set lives in memory, so after a restart a
        // ticket that was already redeemed must not verify again. Namerre runs as one process per database.
        protector = dataProtection.CreateProtector("Namerre.Authentication.Passkeys",
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToTimeLimitedDataProtector();
        if (string.IsNullOrWhiteSpace(settings.Origin))
            return;
        if (!Uri.TryCreate(settings.Origin.Trim(), UriKind.Absolute, out var origin) ||
            origin.Scheme != Uri.UriSchemeHttps && !(origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback) ||
            origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) ||
            !string.IsNullOrEmpty(origin.UserInfo) || origin.HostNameType != UriHostNameType.Dns)
            throw new InvalidOperationException(
                "Namerre:Authentication:Passkeys:Origin must be an HTTPS origin with a host name, e.g. https://namerre.example.com.");
        configured = new PasskeyRelyingParty(origin.IdnHost, origin.GetLeftPart(UriPartial.Authority));
    }

    public bool Enabled { get; }

    /// <summary>The configured relying party, or the request's own scheme and host.</summary>
    public PasskeyRelyingParty RelyingParty(HttpRequest request)
        => configured ?? new PasskeyRelyingParty(request.Host.Host, $"{request.Scheme}://{request.Host.Value}");

    /// <summary>Protects a ceremony's state for <paramref name="purpose"/>, optionally bound to a user version.</summary>
    public string Issue(string purpose, string state, UserRecord? user = null)
        => protector.Protect(JsonSerializer.Serialize(new Ticket(purpose, user?.Id, user?.Stamp, state,
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16)))), TicketLifetime);

    /// <summary>
    /// Reads a ticket issued for <paramref name="purpose"/> and marks it used; null when it is invalid, expired, for
    /// another purpose, or used already.
    /// </summary>
    public (string State, long? UserId, string? Stamp)? Redeem(string? ticket, string purpose)
    {
        if (string.IsNullOrEmpty(ticket))
            return null;
        Ticket? read;
        try
        {
            read = JsonSerializer.Deserialize<Ticket>(protector.Unprotect(ticket, out _));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
        if (read is null || read.Purpose != purpose || string.IsNullOrEmpty(read.State) || !TryUse(read.Nonce))
            return null;
        return (read.State, read.UserId, read.Stamp);
    }

    private bool TryUse(string nonce)
    {
        var now = time.GetUtcNow();
        if (used.Count >= MaximumUsedTickets)
            foreach (var (key, expires) in used)
                if (expires <= now)
                    used.TryRemove(key, out _);
        // Fail closed rather than forget tickets that could still be replayed.
        return used.Count < MaximumUsedTickets && used.TryAdd(nonce, now + TicketLifetime);
    }

    private sealed record Ticket(string Purpose, long? UserId, string? Stamp, string State, string Nonce);
}
