using Microsoft.AspNetCore.StaticFiles;

namespace Namerre.Hosting;

/// <summary>
/// Static file options for the Angular build. Files whose names carry no content hash (the app shell, the
/// web app manifest and the service worker with its manifest) must be revalidated on every request, or an
/// installed app could keep running an outdated shell after the server is upgraded.
/// </summary>
internal static class StaticFileCaching
{
    private static readonly HashSet<string> Revalidated = new(StringComparer.OrdinalIgnoreCase)
    {
        "index.html", "manifest.webmanifest", "ngsw-worker.js", "ngsw.json", "safety-worker.js", "worker-basic.min.js",
    };

    public static StaticFileOptions Options { get; } = CreateOptions();

    private static StaticFileOptions CreateOptions()
    {
        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
        return new StaticFileOptions
        {
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                if (Revalidated.Contains(context.File.Name))
                    context.Context.Response.Headers.CacheControl = "no-cache";
            },
        };
    }
}
