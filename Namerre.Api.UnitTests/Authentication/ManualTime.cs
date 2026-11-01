namespace Namerre.Api.UnitTests.Authentication;

/// <summary>A clock that only moves when told to.</summary>
internal sealed class ManualTime(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset now = now;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}
