using System.Threading.RateLimiting;

namespace Namerre.Authentication;

/// <summary>
/// Takes a permit from the client's own limiter first and from the shared limiter second. A client that is over its
/// own budget is rejected without touching the shared limiter, so it cannot use up the budget of other clients.
/// The shared limiter is owned elsewhere and is not disposed here.
/// </summary>
internal sealed class ClientThenSharedLimiter(RateLimiter client, RateLimiter shared) : RateLimiter
{
    public override TimeSpan? IdleDuration => client.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => client.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var lease = client.AttemptAcquire(permitCount);
        if (!lease.IsAcquired)
            return lease;
        var sharedLease = shared.AttemptAcquire(permitCount);
        if (!sharedLease.IsAcquired)
        {
            lease.Dispose();
            return sharedLease;
        }
        return new ChainedLease(lease, sharedLease);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount,
        CancellationToken cancellationToken)
    {
        var lease = await client.AcquireAsync(permitCount, cancellationToken);
        if (!lease.IsAcquired)
            return lease;
        var sharedLease = await shared.AcquireAsync(permitCount, cancellationToken);
        if (!sharedLease.IsAcquired)
        {
            lease.Dispose();
            return sharedLease;
        }
        return new ChainedLease(lease, sharedLease);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            client.Dispose();
        base.Dispose(disposing);
    }

    private sealed class ChainedLease(RateLimitLease first, RateLimitLease second) : RateLimitLease
    {
        public override bool IsAcquired => true;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                first.Dispose();
                second.Dispose();
            }
        }
    }
}
