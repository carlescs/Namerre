using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Namerre.Application.Authentication;
using Xunit;

namespace Namerre.Api.Tests;

public sealed class PasskeyApiTests : IDisposable
{
    private const string ClientAddressHeader = "X-Test-Client-Address";
    private static int nextClient;
    private const string AdminPassword = "installed-admin-password";
    private const string UserPassword = "carol-first-password";
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"namerre-passkeys-{Guid.NewGuid():N}");
    private readonly SoftwareAuthenticator authenticator = new("https://localhost");
    private string Root => Path.Combine(workspace, "media");
    private string Data => Path.Combine(workspace, "data");

    public PasskeyApiTests() => Directory.CreateDirectory(Root);

    [Fact]
    public async Task UsersRegisterAPasskeyAndSignInWithIt()
    {
        using var factory = Factory();
        using var admin = await Installed(factory);
        var carolId = await CreateUser(admin, "carol");
        using var carol = await SignIn(factory, "carol", UserPassword);

        // Registering needs a confirmed session; a password sign-in moments ago is one.
        using var options = await carol.PostAsJsonAsync("/api/auth/passkeys/options", new { });
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var registration = await options.Content.ReadFromJsonAsync<JsonElement>();
        var answer = authenticator.Create(registration.GetProperty("options"));
        var ticket = registration.GetProperty("ticket").GetString();
        using var registered = await carol.PostAsJsonAsync("/api/auth/passkeys", Body(ticket, answer, "Phone"));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        var passkey = Assert.Single((await registered.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray());
        Assert.Equal("Phone", passkey.GetProperty("name").GetString());
        Assert.False(passkey.TryGetProperty("publicKey", out _));
        // The registering session survives the new credential stamp, and a ticket works once.
        var session = await carol.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(1, session.GetProperty("passkeys").GetArrayLength());
        Assert.True(session.GetProperty("passkeysEnabled").GetBoolean());
        await AssertCode(await carol.PostAsJsonAsync("/api/auth/passkeys", Body(ticket, answer, "Again")),
            HttpStatusCode.Conflict, "PasskeyExpired");

        using var anonymous = Client(factory);
        await AuthWebApplicationFactory.RefreshCsrf(anonymous);
        var (signInTicket, signInAnswer) = await Challenge(anonymous);
        using var login = await anonymous.PostAsJsonAsync("/api/auth/passkey/login", Body(signInTicket, signInAnswer));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await AuthWebApplicationFactory.RefreshCsrf(anonymous);
        session = await anonymous.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal(("carol", true), (session.GetProperty("username").GetString(),
            session.GetProperty("recentSignIn").GetBoolean()));

        // A captured answer cannot be replayed.
        using var replayer = Client(factory);
        await AuthWebApplicationFactory.RefreshCsrf(replayer);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await replayer.PostAsJsonAsync("/api/auth/passkey/login", Body(signInTicket, signInAnswer))).StatusCode);

        // Renaming only changes the label: the sessions the passkey opened continue. Only the owner can rename it.
        var id = passkey.GetProperty("id").GetInt64();
        using var renamed = await carol.PatchAsJsonAsync($"/api/auth/passkeys/{id}", new { name = " Work laptop " });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Work laptop", Assert.Single((await renamed.Content.ReadFromJsonAsync<JsonElement>())
            .EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/rename-plugins")).StatusCode);
        await AssertCode(await admin.PatchAsJsonAsync($"/api/auth/passkeys/{id}", new { name = "Mine" }),
            HttpStatusCode.NotFound, "PasskeyNotFound");
        await AssertCode(await carol.PatchAsJsonAsync($"/api/auth/passkeys/{id}", new { name = "\u0007" }),
            HttpStatusCode.BadRequest, "PasskeyNameRequired");
        foreach (var blank in new[] { "", "   " })
            await AssertCode(await carol.PatchAsJsonAsync($"/api/auth/passkeys/{id}", new { name = blank }),
                HttpStatusCode.BadRequest, "PasskeyNameRequired");

        // Administrators see passkeys and can remove them, which ends the sessions they opened.
        var user = await admin.GetFromJsonAsync<JsonElement>($"/api/users/{carolId}");
        Assert.Equal("Work laptop",
            Assert.Single(user.GetProperty("passkeys").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/users/{carolId}/passkeys/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/rename-plugins")).StatusCode);

        using var removed = Client(factory);
        await AuthWebApplicationFactory.RefreshCsrf(removed);
        (signInTicket, signInAnswer) = await Challenge(removed);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await removed.PostAsJsonAsync("/api/auth/passkey/login", Body(signInTicket, signInAnswer))).StatusCode);
    }

    [Fact]
    public async Task PasskeysConfirmItIsTheUserAndOnlyTheirOwn()
    {
        using var factory = Factory();
        using var admin = await Installed(factory);
        await CreateUser(admin, "carol");
        using var carol = await SignIn(factory, "carol", UserPassword);
        await Register(carol);

        var accounts = factory.Services.GetRequiredService<UserAccounts>();
        Assert.Single(accounts.List(), u => u.Username == "carol" && u.Passkeys.Count == 1);

        var (ticket, answer) = await Challenge(admin);
        using var someoneElses = await admin.PostAsJsonAsync("/api/auth/passkey/reauthenticate", Body(ticket, answer));
        await AssertCode(someoneElses, HttpStatusCode.Forbidden, "PasskeyRejected");

        (ticket, answer) = await Challenge(carol);
        Assert.Equal(HttpStatusCode.NoContent,
            (await carol.PostAsJsonAsync("/api/auth/passkey/reauthenticate", Body(ticket, answer))).StatusCode);
    }

    [Fact]
    public async Task TicketsAreBoundToTheirPurposeAndUser()
    {
        using var factory = Factory();
        using var admin = await Installed(factory);
        await CreateUser(admin, "carol");
        await CreateUser(admin, "dave");
        using var carol = await SignIn(factory, "carol", UserPassword);
        using var dave = await SignIn(factory, "dave", UserPassword);

        var registration = await (await carol.PostAsJsonAsync("/api/auth/passkeys/options", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var answer = authenticator.Create(registration.GetProperty("options"));
        var ticket = registration.GetProperty("ticket").GetString();
        await AssertCode(await dave.PostAsJsonAsync("/api/auth/passkeys", Body(ticket, answer)),
            HttpStatusCode.Conflict, "PasskeyExpired");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await dave.PostAsJsonAsync("/api/auth/passkey/login", Body(ticket, answer))).StatusCode);
        await AssertCode(await carol.PostAsJsonAsync("/api/auth/passkeys", Body("tampered", answer)),
            HttpStatusCode.Conflict, "PasskeyExpired");
    }

    [Fact]
    public async Task PasskeysCanBeTurnedOff()
    {
        using var factory = Factory(new() { ["Namerre:Authentication:Passkeys:Enabled"] = "false" });
        using var admin = await Installed(factory);

        Assert.False((await admin.GetFromJsonAsync<JsonElement>("/api/auth/session"))
            .GetProperty("passkeysEnabled").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/auth/passkey/options", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync("/api/auth/passkeys/options", new { })).StatusCode);
    }

    public void Dispose()
    {
        authenticator.Dispose();
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }

    private async Task Register(HttpClient client)
    {
        var registration = await (await client.PostAsJsonAsync("/api/auth/passkeys/options", new { }))
            .Content.ReadFromJsonAsync<JsonElement>();
        using var registered = await client.PostAsJsonAsync("/api/auth/passkeys", Body(
            registration.GetProperty("ticket").GetString(), authenticator.Create(registration.GetProperty("options"))));
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
    }

    private async Task<(string? Ticket, string Answer)> Challenge(HttpClient client)
    {
        using var options = await client.PostAsJsonAsync("/api/auth/passkey/options", new { });
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        var challenge = await options.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("localhost", challenge.GetProperty("options").GetProperty("rpId").GetString());
        return (challenge.GetProperty("ticket").GetString(), authenticator.Get(challenge.GetProperty("options")));
    }

    private static JsonObject Body(string? ticket, string credential, string? name = null) => new()
    {
        ["ticket"] = ticket,
        ["credential"] = JsonNode.Parse(credential),
        ["name"] = name
    };

    private static async Task<long> CreateUser(HttpClient admin, string username)
    {
        using var created = await admin.PostAsJsonAsync("/api/users", new { username, password = UserPassword });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
    }

    private static async Task AssertCode(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
    }

    private static async Task<HttpClient> Installed(WebApplicationFactory<Program> factory)
    {
        using var client = Client(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        var token = factory.Services.GetRequiredService<UserAccounts>().SetupToken;
        using var setup = await client.PostAsJsonAsync("/api/auth/setup",
            new { token, username = "installed-admin", password = AdminPassword });
        Assert.Equal(HttpStatusCode.NoContent, setup.StatusCode);
        return await SignIn(factory, "installed-admin", AdminPassword);
    }

    private static async Task<HttpClient> SignIn(WebApplicationFactory<Program> factory, string username,
        string password)
    {
        var client = Client(factory);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        await AuthWebApplicationFactory.RefreshCsrf(client);
        return client;
    }

    /// <summary>A browser of its own, at its own address, so the sign-in rate limit applies to each separately.</summary>
    private static HttpClient Client(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        var index = Interlocked.Increment(ref nextClient);
        client.DefaultRequestHeaders.Add(ClientAddressHeader, $"198.18.{index / 250 % 250}.{index % 250 + 1}");
        return client;
    }

    private WebApplicationFactory<Program> Factory(Dictionary<string, string?>? settings = null)
        => new AuthWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Namerre:Authentication:Username"] = "",
                ["Namerre:Authentication:PasswordHash"] = "",
                ["Namerre:RootPath"] = Root,
                ["Namerre:DataPath"] = Data
            }).AddInMemoryCollection(settings ?? []))
            .ConfigureServices(services => services.AddTransient<IStartupFilter, ClientAddressStartupFilter>()));

    // The test server has no remote address; this stands in for the connection address.
    private sealed class ClientAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
            => app =>
            {
                app.Use((context, nextMiddleware) =>
                {
                    if (context.Request.Headers.TryGetValue(ClientAddressHeader, out var address))
                        context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                    return nextMiddleware(context);
                });
                next(app);
            };
    }
}
