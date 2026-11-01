using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Namerre.Application.Renaming;
using Namerre.Authentication;
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

    [Fact]
    public async Task ConnectionsOfSessionsRevokedByAPasswordChangeStopReceivingChanges()
    {
        const string username = "installed-admin", password = "old-password", newPassword = "new-password";
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "A");
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:Authentication:Username"] = "",
                ["Namerre:Authentication:PasswordHash"] = "",
                ["Namerre:RootPath"] = Root,
                ["Namerre:DataPath"] = Path.Combine(workspace, "data")
            })));
        CookieContainer revokedCookies = new(), currentCookies = new();
        using var revoked = CookieClient(factory, revokedCookies);
        using var current = CookieClient(factory, currentCookies);
        await AuthWebApplicationFactory.RefreshCsrf(revoked);
        using (var setup = await revoked.PostAsJsonAsync("/api/auth/setup", new
        {
            token = factory.Services.GetRequiredService<UserAccounts>().SetupToken, username, password
        }))
            setup.EnsureSuccessStatusCode();
        foreach (var client in new[] { revoked, current })
        {
            await AuthWebApplicationFactory.RefreshCsrf(client);
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
            login.EnsureSuccessStatusCode();
            await AuthWebApplicationFactory.RefreshCsrf(client);
        }

        // This connection is established before the password change and outlives it.
        await using var revokedHub = ConnectHub(factory, revokedCookies);
        var revokedChanges = new List<RenameChange>();
        revokedHub.On<RenameChange>("renameChanged", change => { lock (revokedChanges) revokedChanges.Add(change); });
        await revokedHub.StartAsync();

        using (var change = await current.PostAsJsonAsync("/api/auth/password",
            new { currentPassword = password, newPassword }))
            Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);
        await AuthWebApplicationFactory.RefreshCsrf(current);

        await using var currentHub = ConnectHub(factory, currentCookies);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        currentHub.On<RenameChange>("renameChanged", change =>
        {
            if (change is { Kind: "Plan", Status: "Ready" }) ready.TrySetResult(change.Id);
        });
        await currentHub.StartAsync();

        var request = new PipelineRequest([new FileInput("0", "alpha.txt")],
            [new PluginInstance("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);
        using var prepare = await current.PostAsJsonAsync("/api/rename-plans", request);
        Assert.Equal(HttpStatusCode.Accepted, prepare.StatusCode);
        var accepted = (await prepare.Content.ReadFromJsonAsync<JobAccepted>())!;

        Assert.Equal(accepted.Id, await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        lock (revokedChanges) Assert.Empty(revokedChanges);
        using var revokedRequest = await revoked.GetAsync("/api/rename-plans/" + accepted.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRequest.StatusCode);
    }

    [Fact]
    public async Task SingleSignOnSessionsReceiveChanges()
    {
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "A");
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:RootPath"] = Root,
                ["Namerre:DataPath"] = Path.Combine(workspace, "data"),
                ["Namerre:Authentication:Oidc:Authority"] = "https://keycloak.test/realms/media",
                ["Namerre:Authentication:Oidc:ClientId"] = "namerre",
                ["Namerre:Authentication:Oidc:RequiredRole"] = "namerre-admin"
            })));
        var oidc = factory.Services.GetRequiredService<OidcLogin>();
        var (user, _, _) = factory.Services.GetRequiredService<UserAccounts>()
            .SignInExternal(new ExternalIdentity(oidc.Issuer, "subject-1", "alice"), autoProvision: true);
        var principal = UserSession.Create(user!, UserSession.SingleSignOnMethod, oidc.Stamp, DateTimeOffset.UtcNow);
        var ticketFormat = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).TicketDataFormat;
        var cookies = new CookieContainer();
        cookies.Add(new Uri("https://localhost"), new Cookie("__Host-namerre-session", ticketFormat.Protect(
            new AuthenticationTicket(principal, CookieAuthenticationDefaults.AuthenticationScheme))) { Secure = true });
        using var client = CookieClient(factory, cookies);
        await AuthWebApplicationFactory.RefreshCsrf(client);

        await using var connection = ConnectHub(factory, cookies);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<RenameChange>("renameChanged", change =>
        {
            if (change is { Kind: "Plan", Status: "Ready" }) ready.TrySetResult(change.Id);
        });
        await connection.StartAsync();

        var request = new PipelineRequest([new FileInput("0", "alpha.txt")],
            [new PluginInstance("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);
        using var prepare = await client.PostAsJsonAsync("/api/rename-plans", request);
        Assert.Equal(HttpStatusCode.Accepted, prepare.StatusCode);
        var accepted = (await prepare.Content.ReadFromJsonAsync<JobAccepted>())!;

        Assert.Equal(accepted.Id, await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ChangesOnlyReachTheOwnerAndAdministrators()
    {
        File.WriteAllText(Path.Combine(Root, "alpha.txt"), "A");
        using var factory = new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:Authentication:Username"] = "",
                ["Namerre:Authentication:PasswordHash"] = "",
                ["Namerre:RootPath"] = Root,
                ["Namerre:DataPath"] = Path.Combine(workspace, "data")
            })));
        CookieContainer adminCookies = new(), ownerCookies = new(), otherCookies = new();
        using var admin = CookieClient(factory, adminCookies);
        await AuthWebApplicationFactory.RefreshCsrf(admin);
        using (var setup = await admin.PostAsJsonAsync("/api/auth/setup", new
        {
            token = factory.Services.GetRequiredService<UserAccounts>().SetupToken,
            username = "installed-admin", password = "installed-admin-password"
        }))
            setup.EnsureSuccessStatusCode();
        await SignIn(admin, "installed-admin", "installed-admin-password");
        foreach (var username in new[] { "frank", "grace" })
            using (var created = await admin.PostAsJsonAsync("/api/users", new { username, password = username + "-long-password" }))
                created.EnsureSuccessStatusCode();
        using var owner = CookieClient(factory, ownerCookies);
        await SignIn(owner, "frank", "frank-long-password");
        using var other = CookieClient(factory, otherCookies);
        await SignIn(other, "grace", "grace-long-password");

        await using var adminHub = ConnectHub(factory, adminCookies);
        await using var ownerHub = ConnectHub(factory, ownerCookies);
        await using var otherHub = ConnectHub(factory, otherCookies);
        var adminChanges = Collect(adminHub);
        var ownerChanges = Collect(ownerHub);
        var otherChanges = Collect(otherHub);
        await Task.WhenAll(adminHub.StartAsync(), ownerHub.StartAsync(), otherHub.StartAsync());

        var request = new PipelineRequest([new FileInput("0", "alpha.txt")],
            [new PluginInstance("upper", "case", 1, true, JsonSerializer.SerializeToElement(new { mode = "upper" }))]);
        using var prepare = await owner.PostAsJsonAsync("/api/rename-plans", request);
        Assert.Equal(HttpStatusCode.Accepted, prepare.StatusCode);
        var plan = (await prepare.Content.ReadFromJsonAsync<JobAccepted>())!.Id;
        // A change without an owner, such as a watch folder's, is for administrators only.
        factory.Services.GetRequiredService<IRenameNotifier>().Changed(new("Run", "ownerless", "Completed"));

        await WaitFor(adminChanges, changes => changes.Any(c => c is { Kind: "Plan", Status: "Ready" } && c.Id == plan)
            && changes.Any(c => c.Id == "ownerless"));
        await WaitFor(ownerChanges, changes => changes.Any(c => c is { Kind: "Plan", Status: "Ready" } && c.Id == plan));
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        lock (ownerChanges) Assert.DoesNotContain(ownerChanges, c => c.Id == "ownerless");
        lock (otherChanges) Assert.Empty(otherChanges);
    }

    private static async Task SignIn(HttpClient client, string username, string password)
    {
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using (var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password }))
            login.EnsureSuccessStatusCode();
        await AuthWebApplicationFactory.RefreshCsrf(client);
    }

    private static List<RenameChange> Collect(HubConnection connection)
    {
        var changes = new List<RenameChange>();
        connection.On<RenameChange>("renameChanged", change => { lock (changes) changes.Add(change); });
        return changes;
    }

    private static async Task WaitFor(List<RenameChange> changes, Func<List<RenameChange>, bool> done)
    {
        for (var i = 0; i < 200; i++)
        {
            lock (changes)
                if (done(changes)) return;
            await Task.Delay(50);
        }
        throw new TimeoutException();
    }

    private static HttpClient CookieClient(WebApplicationFactory<Program> factory, CookieContainer cookies)
        => new(new CookieContainerHandler(cookies) { InnerHandler = factory.Server.CreateHandler() })
        {
            BaseAddress = new Uri("https://localhost")
        };

    private static HubConnection ConnectHub(WebApplicationFactory<Program> factory, CookieContainer cookies)
        => new HubConnectionBuilder()
            .WithUrl("https://localhost/api/hubs/rename", options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ =>
                    new CookieContainerHandler(cookies) { InnerHandler = factory.Server.CreateHandler() };
            })
            .Build();

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        var parent = Path.GetDirectoryName(workspace)!;
        if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
    }
}
