using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Namerre.Application.WatchFolders;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class WatchFoldersApiTests : IDisposable
{
    private readonly string workspace = Path.Combine(Directory.GetCurrentDirectory(), ".watch-api-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ListsPausesAndResumesConfiguredFolders()
    {
        Directory.CreateDirectory(Path.Combine(workspace, "media", "downloads"));
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:RootPath"] = Path.Combine(workspace, "media"),
                ["Namerre:DataPath"] = Path.Combine(workspace, "data"),
                ["Namerre:WatchFolders:0:Name"] = "Downloads",
                ["Namerre:WatchFolders:0:Path"] = "downloads",
                ["Namerre:WatchFolders:0:Preset"] = "TV"
            })));
        using var client = await AuthWebApplicationFactory.AuthenticatedClient(factory);

        var folders = (await client.GetFromJsonAsync<WatchFolderStatus[]>("/api/watch-folders"))!;
        Assert.Equal(("Downloads", "downloads", "TV", false), (folders[0].Name, folders[0].Path, folders[0].Preset, folders[0].Paused));

        using var paused = await client.PostAsync("/api/watch-folders/Downloads/pause", null);
        Assert.True((await paused.Content.ReadFromJsonAsync<WatchFolderStatus>())!.Paused);
        using var resumed = await client.PostAsync("/api/watch-folders/downloads/resume", null);
        Assert.False((await resumed.Content.ReadFromJsonAsync<WatchFolderStatus>())!.Paused);

        using var unknown = await client.PostAsync("/api/watch-folders/Other/pause", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        using var retry = await client.PostAsJsonAsync("/api/watch-folders/Downloads/retry", new RetryWatchedFileRequest("downloads/x.mkv"));
        Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }
}
