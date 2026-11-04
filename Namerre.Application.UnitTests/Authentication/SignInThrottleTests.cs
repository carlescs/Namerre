using Microsoft.Extensions.Time.Testing;
using Namerre.Application.Authentication;
using Xunit;

namespace Namerre.Application.UnitTests.Authentication;

public sealed class SignInThrottleTests
{
    private static readonly SignInThrottleSettings Defaults = SignInThrottleSettings.Default;
    private static readonly string Alice = SignInThrottle.AccountKey("alice");
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly SignInThrottle throttle;

    public SignInThrottleTests() => throttle = new SignInThrottle(time: time);

    [Fact]
    public void TheFirstFailuresAreFree()
    {
        for (var attempt = 0; attempt < Defaults.FreeFailures; attempt++)
        {
            Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
            throttle.RecordFailure(Alice);
        }
    }

    [Fact]
    public void FurtherAttemptsAreSpacedOutByADoublingDelay()
    {
        Fail(Alice, Defaults.FreeFailures);

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
        Fail(Alice, Defaults.FreeFailures + 10);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
        Assert.Equal(Defaults.MaximumDelay, throttle.Reserve(Alice));

        Assert.Null(throttle.Reserve(Alice));
        Assert.Null(throttle.Reserve(Alice));

        time.Advance(Defaults.MaximumDelay);
        Assert.Equal(Defaults.MaximumDelay, throttle.Reserve(Alice));
    }

    [Fact]
    public void ACaseVariantOfTheUsernameSharesTheAccountsTurns()
    {
        Fail(SignInThrottle.AccountKey(" ALICE "), Defaults.FreeFailures + 1);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));

        Assert.Equal(TimeSpan.FromSeconds(2), throttle.Reserve(Alice));
    }

    [Fact]
    public void AKnownDeviceIsNotSlowedDownByFailuresElsewhere()
    {
        Fail(Alice, Defaults.FreeFailures + 10);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));

        Assert.Equal(TimeSpan.Zero, throttle.Reserve(SignInThrottle.DeviceKey(7, "laptop")));
    }

    [Fact]
    public void FailuresAreForgottenAfterAQuietPeriodOrASuccess()
    {
        Fail(Alice, Defaults.FreeFailures + 2);
        time.Advance(Defaults.FailureMemory + TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));

        var bob = SignInThrottle.AccountKey("bob");
        Fail(bob, Defaults.FreeFailures + 2);
        throttle.RecordSuccess(bob);
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(bob));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(bob));
    }

    [Fact]
    public void TheLeastRecentlyFailedKeyMakesRoom()
    {
        Fail(Alice, Defaults.FreeFailures + 1);
        time.Advance(TimeSpan.FromSeconds(1));
        for (var index = 0; index < Defaults.MaximumTracked; index++)
            throttle.RecordFailure(SignInThrottle.AccountKey("guess" + index));

        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
        Assert.Equal(TimeSpan.Zero, throttle.Reserve(Alice));
    }

    [Fact]
    public void TheSettingsChangeWhenAndHowMuchAttemptsAreSlowedDown()
    {
        var custom = new SignInThrottle(new SignInThrottleSettings
        {
            FreeFailures = 2,
            FirstDelay = TimeSpan.FromSeconds(5),
            MaximumDelay = TimeSpan.FromSeconds(10)
        }, time);
        custom.RecordFailure(Alice);
        custom.RecordFailure(Alice);

        Assert.Equal(TimeSpan.Zero, custom.Reserve(Alice));
        Assert.Equal(TimeSpan.FromSeconds(5), custom.Reserve(Alice));
        Assert.Equal(TimeSpan.FromSeconds(10), custom.Reserve(Alice));
        Assert.Null(custom.Reserve(Alice));
    }

    [Fact]
    public void ADisabledThrottleNeverSlowsDown()
    {
        var disabled = new SignInThrottle(new SignInThrottleSettings { Enabled = false }, time);
        for (var attempt = 0; attempt < 50; attempt++)
        {
            Assert.Equal(TimeSpan.Zero, disabled.Reserve(Alice));
            disabled.RecordFailure(Alice);
        }
    }

    [Fact]
    public void SettingsOutsideTheirRangesAreInvalid()
    {
        Assert.True(Defaults.IsValid);
        Assert.False((Defaults with { FreeFailures = 0 }).IsValid);
        Assert.False((Defaults with { FreeFailures = SignInThrottleSettings.CeilingFreeFailures + 1 }).IsValid);
        Assert.False((Defaults with { FirstDelay = TimeSpan.Zero }).IsValid);
        Assert.False((Defaults with { FirstDelay = TimeSpan.FromSeconds(31) }).IsValid);
        Assert.False((Defaults with
        {
            MaximumDelay = TimeSpan.FromSeconds(SignInThrottleSettings.CeilingDelaySeconds + 1)
        }).IsValid);
        Assert.False((Defaults with { FailureMemory = TimeSpan.FromSeconds(30) }).IsValid);
        Assert.False((Defaults with { MaximumTracked = SignInThrottleSettings.MinimumTracked - 1 }).IsValid);
    }

    private void Fail(string key, int failures)
    {
        for (var attempt = 0; attempt < failures; attempt++)
            throttle.RecordFailure(key);
    }
}
