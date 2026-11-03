using Namerre.Application.Authentication;

namespace Namerre.Testing;

/// <summary>A non-durable <see cref="ISessionRegistry"/>. Also linked into Namerre.Api.UnitTests.</summary>
internal sealed class InMemorySessionRegistry : ISessionRegistry
{
    private readonly HashSet<string> revoked = [];
    private readonly Dictionary<long, long> epochs = [];

    public bool IsRevoked(string sessionId) => revoked.Contains(sessionId);

    public void Revoke(string sessionId) => revoked.Add(sessionId);

    public long Epoch(long userId) => epochs.GetValueOrDefault(userId);

    public long AdvanceEpoch(long userId) => epochs[userId] = Epoch(userId) + 1;
}
