using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Namerre.Application.Renaming;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class RenameNotificationTests : IDisposable
{
    private readonly string workspace = Path.Combine(Directory.GetCurrentDirectory(), ".notification-tests", Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(workspace, "media");

    public RenameNotificationTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task HubRequiresAuthentication()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);

        using var negotiate = await client.PostAsync("/api/hubs/rename/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Unauthorized, negotiate.StatusCode);
    }

    [Fact]
    public async Task HubRejectsPlainHttp()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = AuthWebApplicationFactory.AnonymousClient(factory);
        client.BaseAddress = new Uri("http://localhost");

        using var negotiate = await client.PostAsync("/api/hubs/rename/negotiate?negotiateVersion=1", null);

        Assert.False(negotiate.IsSuccessStatusCode);
    }

    [Fact]
    public async Task AuthenticatedClientsReceivePlanChangesFromTheWorker()
    {
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "A");
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:RootPath"] = Root, ["Namerre:DataPath"] = Path.Combine(workspace, "data")
            })));
        var cookies = new CookieContainer();
        using var client = new HttpClient(new CookieContainerHandler(cookies) { InnerHandler = factory.Server.CreateHandler() })
        {
            BaseAddress = new Uri("https://localhost")
        };
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using (var login = await client.PostAsJsonAsync("/api/auth/login",
            new { AuthWebApplicationFactory.Username, AuthWebApplicationFactory.Password }))
            login.EnsureSuccessStatusCode();
        await AuthWebApplicationFactory.RefreshCsrf(client);

        await using var connection = new HubConnectionBuilder()
            .WithUrl("https://localhost/api/hubs/rename", options =>
            {
                // TestServer has no socket listener; long polling exercises the same authorization and hub.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ =>
                    new CookieContainerHandler(cookies) { InnerHandler = factory.Server.CreateHandler() };
            })
            .Build();
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = new List<RenameChange>();
        connection.On<RenameChange>("renameChanged", change =>
        {
            lock (changes) changes.Add(change);
            if (change is { Kind: "Plan", Status: "Ready" }) ready.TrySetResult(change.Id);
        });
        await connection.StartAsync();

        var request = new PipelineRequest([new FileInput("0", "alpha.txt")],
            [new PluginInstance("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);
        using var prepare = await client.PostAsJsonAsync("/api/rename-plans", request);
        Assert.Equal(HttpStatusCode.Accepted, prepare.StatusCode);
        var accepted = (await prepare.Content.ReadFromJsonAsync<JobAccepted>())!;

        Assert.Equal(accepted.Id, await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        lock (changes) Assert.Contains(new RenameChange("Plan", accepted.Id, "Preparing"), changes);
    }

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        var parent = Path.GetDirectoryName(workspace)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }
}
