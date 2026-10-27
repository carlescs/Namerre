using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace Namerre.Security;

/// <summary>
/// Adds browser hardening headers to every response, including a Content Security Policy that only allows
/// same-origin scripts plus the exact inline scripts of the served <c>index.html</c>.
/// </summary>
public static partial class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this WebApplication app)
    {
        var policy = new ContentSecurityPolicy(app.Environment.WebRootFileProvider);
        return app.Use(async (context, next) =>
        {
            // Applied when the response starts, so handlers that clear the response cannot drop them.
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = policy.Value;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                return Task.CompletedTask;
            });
            await next(context);
        });
    }

    /// <summary>Builds the policy, recomputing the inline script hashes whenever <c>index.html</c> changes.</summary>
    internal sealed class ContentSecurityPolicy(IFileProvider webRoot)
    {
        // A reference, so concurrent requests swap it atomically.
        private Snapshot? cached;

        public string Value
        {
            get
            {
                var index = webRoot.GetFileInfo("index.html");
                var current = cached;
                if (current is not null && index.Exists && current.Modified == index.LastModified)
                    return current.Policy;
                var policy = Build(index.Exists ? Read(index) : "");
                cached = index.Exists ? new Snapshot(index.LastModified, policy) : null;
                return policy;
            }
        }

        private sealed record Snapshot(DateTimeOffset Modified, string Policy);

        private static string Read(IFileInfo file)
        {
            using var reader = new StreamReader(file.CreateReadStream(), Encoding.UTF8);
            return reader.ReadToEnd();
        }

        internal static string Build(string indexHtml)
        {
            var hashes = InlineScripts().Matches(indexHtml)
                .Where(match => !match.Groups["attributes"].Value.Contains("src", StringComparison.OrdinalIgnoreCase))
                .Select(match => $" 'sha256-{Hash(match.Groups["body"].Value)}'")
                .Distinct();
            // Angular inserts component styles at runtime, so styles cannot be restricted to hashes.
            return "default-src 'self'; script-src 'self'" + string.Concat(hashes) +
                "; style-src 'self' 'unsafe-inline'; img-src 'self'; font-src 'self'; connect-src 'self'; " +
                "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
        }

        // Browsers normalize line endings before hashing, so a Windows build with CRLF still matches.
        private static string Hash(string script)
            => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(
                script.Replace("\r\n", "\n").Replace('\r', '\n'))));
    }

    [GeneratedRegex(@"<script(?<attributes>[^>]*)>(?<body>.*?)</script\s*>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineScripts();
}
