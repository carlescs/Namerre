using System.Net;
using Namerre.Authentication;
using Xunit;

namespace Namerre.Api.UnitTests.Authentication;

public sealed class AuthenticationSetupTests
{
    [Theory]
    [InlineData(null, "unknown")]
    [InlineData("192.0.2.10", "192.0.2.10")]
    [InlineData("::ffff:192.0.2.10", "192.0.2.10")]
    [InlineData("2001:db8:1:2:3:4:5:6", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:ffff::1", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:3::1", "2001:db8:1:3::/64")]
    public void LoginRateLimitsPartitionClientsByAddressAndIPv6Slash64(string? address, string expected)
        => Assert.Equal(expected,
            AuthenticationSetup.ClientPartition(address is null ? null : IPAddress.Parse(address)));
}
