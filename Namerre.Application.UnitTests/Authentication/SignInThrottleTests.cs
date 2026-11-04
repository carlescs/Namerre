using Microsoft.Extensions.Time.Testing;
using Namerre.Application.Authentication;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class SignInThrottleTests
{
    private static readonly string Alice = SignInThrottle.AccountKey("alice");
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly SignInThrottle throttle;

    public SignInThrottleTests() => throttle = new SignInThrottle(time);

    [Fact]
    public void TheFirstFailuresAreFree()
    {
        for (var attempt = 0; attempt < SignInThrottle.FreeFailures; attempt++)
        {
            Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
            throttle.RecordFailure(Alice);
        }
    }

    [Fact]
    public void FurtherAttemptsAreSpacedOutByADoublingDelay()
    {
        Fail(Alice, SignInThrottle.FreeFailures);

        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
        throttle.RecordFailure(Alice);
        // The next turn is a second after the previous one; then two seconds after that.
        Assert.Equal(TimeSpan.FromSeconds(1), throttle.Reserve(Alice));
        throttle.RecordFailure(Alice);
        Assert.Equal(TimeSpan.FromSeconds(3), throttle.Reserve(Alice));
    }

    [Fact]
    public void AttemptsThatWouldWaitTooLongAreRefusedWithoutReservingATurn()
    {
        Fail(Alice, SignInThrottle.FreeFailures + 10);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
        Assert.Equal(SignInThrottle.MaximumDelay, throttle.Reserve(Alice));

        Assert.Null(throttle.Reserve(Alice));
        Assert.Null(throttle.Reserve(Alice));

        time.Advance(SignInThrottle.MaximumDelay);
        Assert.Equal(SignInThrottle.MaximumDelay, throttle.Reserve(Alice));
    }

    [Fact]
    public void ACaseVariantOfTheUsernameSharesTheAccountsTurns()
    {
        Fail(SignInThrottle.AccountKey(" ALICE "), SignInThrottle.FreeFailures + 1);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));

        Assert.Equal(TimeSpan.FromSeconds(2), throttle.Reserve(Alice));
    }

    [Fact]
    public void AKnownDeviceIsNotSlowedDownByFailuresElsewhere()
    {
        Fail(Alice, SignInThrottle.FreeFailures + 10);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));

        Assert.Equal(TimeSpan.Zero, throttle.Reserve(SignInThrottle.DeviceKey(7, "laptop")));
    }

    [Fact]
    public void FailuresAreForgottenAfterAQuietPeriodOrASuccess()
    {
        Fail(Alice, SignInThrottle.FreeFailures + 2);
        time.Advance(SignInThrottle.FailureMemory + TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));

        var bob = SignInThrottle.AccountKey("bob");
        Fail(bob, SignInThrottle.FreeFailures + 2);
        throttle.RecordSuccess(bob);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(bob));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(bob));
    }

    [Fact]
    public void TheLeastRecentlyFailedKeyMakesRoom()
    {
        Fail(Alice, SignInThrottle.FreeFailures + 1);
        time.Advance(TimeSpan.FromSeconds(1));
        for (var index = 0; index < SignInThrottle.MaximumTracked; index++)
            throttle.RecordFailure(SignInThrottle.AccountKey("guess" + index));

        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
    }

    private void Fail(string key, int failures)
    {
        for (var attempt = 0; attempt < failures; attempt++)
            throttle.RecordFailure(key);
    }
}
