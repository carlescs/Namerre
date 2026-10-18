using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Namerre.Application.Folders;
using Namerre.Infrastructure.Folders;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class FolderBrowsingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"namerre-{Guid.NewGuid():N}");

    public FolderBrowsingTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "Movies", "Nested"));
        Directory.CreateDirectory(Path.Combine(root, "Empty"));
        File.WriteAllText(Path.Combine(root, "notes.txt"), "hello");
        File.WriteAllText(Path.Combine(root, "Movies", "movie.mkv"), "data");
    }

    [Fact]
    public async Task ListsRootAndSelectedFolderThroughMediator()
    {
        using var factory = CreateFactory(root);
        using var client = CreateClient(factory);

        var listing = await client.GetFromJsonAsync<FolderListing>("/api/folders");

        Assert.NotNull(listing);
        Assert.Equal("", listing.Path);
        Assert.Null(listing.ParentPath);
        Assert.Equal(["Empty", "Movies"], listing.Directories.Select(entry => entry.Name));
        var file = Assert.Single(listing.Files);
        Assert.Equal("notes.txt", file.Name);
        Assert.Equal(5, file.Length);
        Assert.Equal(DateTimeKind.Utc, file.LastModifiedUtc.Kind);

        var selected = await client.GetFromJsonAsync<FolderListing>("/api/folders?path=Movies");

        Assert.NotNull(selected);
        Assert.Equal("Movies", selected.Path);
        Assert.Equal("", selected.ParentPath);
        Assert.Equal("Movies/Nested", Assert.Single(selected.Directories).Path);
        Assert.Equal("movie.mkv", Assert.Single(selected.Files).Name);

        var empty = await client.GetFromJsonAsync<FolderListing>("/api/folders?path=Empty");
        Assert.NotNull(empty);
        Assert.Empty(empty.Directories);
        Assert.Empty(empty.Files);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("Movies", false)]
    [InlineData("Movies", true)]
    public async Task SelectsFilesWithOptionalSubfolders(string path, bool includeSubfolders)
    {
        File.WriteAllText(Path.Combine(root, "Movies", "Nested", "notes.txt"), "nested");
        using var factory = CreateFactory(root);
        using var client = CreateClient(factory);

        var folders = await client.GetFromJsonAsync<FolderListing[]>(
            $"/api/folders/selection?path={path}&includeSubfolders={includeSubfolders}");

        Assert.NotNull(folders);
        var expectedPaths = (path, includeSubfolders) switch
        {
            ("", true) => new[] { "", "Empty", "Movies", "Movies/Nested" },
            ("Movies", true) => ["Movies", "Movies/Nested"],
            _ => [path]
        };
        Assert.Equal(expectedPaths, folders.Select(folder => folder.Path));
        if (includeSubfolders)
        {
            var nested = Assert.Single(folders, folder => folder.Path == "Movies/Nested");
            Assert.Equal("notes.txt", Assert.Single(nested.Files).Name);
        }
        else
        {
            Assert.Equal(path.Length == 0 ? "notes.txt" : "movie.mkv",
                Assert.Single(Assert.Single(folders).Files).Name);
        }
    }

    [Fact]
    public async Task SelectionDefaultsToCurrentFolderOnly()
    {
        using var factory = CreateFactory(root);
        using var client = CreateClient(factory);

        var folders = await client.GetFromJsonAsync<FolderListing[]>("/api/folders/selection");

        Assert.NotNull(folders);
        Assert.Equal("", Assert.Single(folders).Path);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("Movies/../../outside")]
    [InlineData("..\\outside")]
    [InlineData("/etc")]
    [InlineData("C:\\Windows")]
    [InlineData("\\\\server\\share")]
    [InlineData("notes.txt:stream")]
    [InlineData("Movies/\0")]
    public async Task RejectsPathsOutsideRoot(string path)
    {
        using var factory = CreateFactory(root);
        using var client = CreateClient(factory);

        foreach (var endpoint in new[] { "/api/folders", "/api/folders/selection" })
        {
            using var response = await client.GetAsync(
                $"{endpoint}?path={Uri.EscapeDataString(path)}&includeSubfolders=true");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.DoesNotContain(root, await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("notes.txt")]
    public async Task MissingOrNonDirectoryPathsReturnNotFound(string path)
    {
        using var factory = CreateFactory(root);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/folders?path={path}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnconfiguredRootReturnsExplicitUnavailableError()
    {
        using var factory = CreateFactory("");
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/folders");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("FileBrowser:RootPath", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MissingConfiguredRootReturnsUnavailableWithoutLeakingItsPath()
    {
        using var factory = CreateFactory(Path.Combine(root, "missing"));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync("/api/folders");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(root, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LinksAreNotListedAndCannotBeTraversed()
    {
        var link = Path.Combine(root, "Link");
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c mklink /J \"{link}\" \"{Path.GetTempPath().TrimEnd('\\')}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else
        {
            Directory.CreateSymbolicLink(link, Path.GetTempPath());
        }

        try
        {
            using var factory = CreateFactory(root);
            using var client = CreateClient(factory);
            var listing = await client.GetFromJsonAsync<FolderListing>("/api/folders");
            Assert.NotNull(listing);
            Assert.DoesNotContain(listing.Directories, entry => entry.Name == "Link");
            using var response = await client.GetAsync("/api/folders?path=Link");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            var selection = await client.GetFromJsonAsync<FolderListing[]>(
                "/api/folders/selection?includeSubfolders=true");
            Assert.NotNull(selection);
            Assert.DoesNotContain(selection, folder => folder.Path.Contains("Link"));
            Assert.All(selection, folder =>
                Assert.DoesNotContain(folder.Directories, directory => directory.Name == "Link"));
            using var selectionResponse = await client.GetAsync(
                "/api/folders/selection?path=Link&includeSubfolders=true");
            Assert.Equal(HttpStatusCode.Forbidden, selectionResponse.StatusCode);
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        var browser = new ServerFolderBrowser(root, root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => browser.Browse("", cancellation.Token));
    }

    private static WebApplicationFactory<Program> CreateFactory(string rootPath)
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FileBrowser:RootPath"] = rootPath
                })));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

    public void Dispose() => Directory.Delete(root, recursive: true);
}
