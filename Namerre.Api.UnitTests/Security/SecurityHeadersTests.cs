using Namerre.Security;
using Xunit;

namespace Namerre.Api.UnitTests.Security;

public sealed class SecurityHeadersTests
{
    [Fact]
    public void PolicyAllowsOnlySameOriginScriptsWithoutAnIndex()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy.Build("");

        Assert.Contains("script-src 'self';", policy);
        Assert.Contains("default-src 'self'", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("base-uri 'self'", policy);
        // Only styles may be inline; scripts never.
        Assert.DoesNotContain("unsafe-inline", policy.Split("style-src")[0]);
    }

    [Fact]
    public void InlineScriptsAreAllowedByTheirHashAndExternalScriptsAreNot()
    {
        var policy = SecurityHeaders.ContentSecurityPolicy.Build(
            "<head><script>alert(1)</script></head><body><script src=\"main.js\" type=\"module\"></script>" +
            "<SCRIPT>alert(1)</SCRIPT ></body>");

        // Hash values as a browser reports them for these script bodies.
        Assert.Contains("script-src 'self' 'sha256-bhHHL3z2vDgxUt0W3dWQOrprscmda2Y5pLsLg4GF+pI=';", policy);
    }

    [Theory]
    [InlineData("<script>\n  a();\n  b();\n</script>")]
    [InlineData("<script>\r\n  a();\r\n  b();\r\n</script>")]
    [InlineData("<script>\r  a();\r  b();\r</script>")]
    public void LineEndingsAreNormalizedLikeBrowsersDo(string html)
        => Assert.Contains("'sha256-YNh7g92k9zwQBLcCu/TOcBf0GxhzmkGFZz7FrhSnIGg='",
            SecurityHeaders.ContentSecurityPolicy.Build(html));
}
