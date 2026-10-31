using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Integrations;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Integrations;

public sealed class NamerreOptionsValidatorTests
{
    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(IntegrationsOptions integrations)
        => new NamerreOptionsValidator().Validate(null, new NamerreOptions { Integrations = integrations });

    [Fact]
    public void DefaultsAreValid() => Assert.True(Validate(new()).Succeeded);

    [Fact]
    public void CompleteTargetsAreValid()
        => Assert.True(Validate(new()
        {
            Jellyfin = new() { Url = "http://jelly:8096", ApiKey = "k" },
            Emby = new() { Url = "https://emby/", ApiKey = "k" },
            Plex = new() { Url = "http://plex:32400", Token = "t" },
            Webhook = new() { Url = "https://hooks.example/x", IncludePaths = true }
        }).Succeeded);

    [Theory]
    [InlineData("not a url")]
    [InlineData("/relative")]
    [InlineData("ftp://host/x")]
    public void UrlsMustBeAbsoluteHttpOrHttps(string url)
    {
        Assert.False(Validate(new() { Jellyfin = new() { Url = url, ApiKey = "k" } }).Succeeded);
        Assert.False(Validate(new() { Webhook = new() { Url = url } }).Succeeded);
    }

    [Fact]
    public void AKeyIsRequiredWhenTheUrlIsSet()
    {
        Assert.False(Validate(new() { Jellyfin = new() { Url = "http://j" } }).Succeeded);
        Assert.False(Validate(new() { Emby = new() { Url = "http://e" } }).Succeeded);
        Assert.False(Validate(new() { Plex = new() { Url = "http://p" } }).Succeeded);
    }

    [Fact]
    public void AKeyWithoutAUrlIsRejected()
    {
        Assert.False(Validate(new() { Jellyfin = new() { ApiKey = "k" } }).Succeeded);
        Assert.False(Validate(new() { Emby = new() { ApiKey = "k" } }).Succeeded);
        Assert.False(Validate(new() { Plex = new() { Token = "t" } }).Succeeded);
    }

    [Fact]
    public void FailureMessagesNameTheSettingButNeverTheSecret()
    {
        var result = Validate(new() { Plex = new() { Token = "very-secret" } }); // betterleaks:allow

        Assert.Contains("Namerre:Integrations:Plex:Url", Assert.Single(result.Failures!));
        Assert.DoesNotContain("very-secret", string.Join(' ', result.Failures!));
    }
}
