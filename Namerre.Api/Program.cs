using Namerre.Application;
using Namerre.Application.Folders;
using Namerre.Infrastructure.Folders;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Renaming;
using Namerre.Authentication;
using Namerre.Notifications;
using Namerre.Telemetry;
using Microsoft.Extensions.Options;
using Namerre.Infrastructure.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

if (args is ["--hash-password"])
{
    AdminAccount.GeneratePasswordHash();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddOptions<NamerreOptions>()
    .Bind(builder.Configuration.GetSection(NamerreOptions.SectionName));
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<NamerreOptions>>((options, settings) =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (var proxy in settings.Value.ReverseProxy.KnownProxies)
            options.KnownProxies.Add(IPAddress.Parse(proxy));
        foreach (var network in settings.Value.ReverseProxy.KnownNetworks)
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    });
builder.Services.AddSingleton(services => new BoundedFileSystem(
    services.GetRequiredService<IOptions<NamerreOptions>>().Value.RootPath,
    services.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddSingleton<IRenameFileSystem>(services => services.GetRequiredService<BoundedFileSystem>());
builder.Services.AddSingleton(services => new RenameStore(
    services.GetRequiredService<BoundedFileSystem>(),
    services.GetRequiredService<IOptions<NamerreOptions>>().Value.DataPath,
    services.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddHttpClient("tmdb", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<ITmdbMetadata>(services => new TmdbMetadata(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("tmdb"),
    services.GetRequiredService<IOptions<NamerreOptions>>(), services.GetRequiredService<RenameStore>()));
builder.Services.AddSingleton<PipelineEngine>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRenameNotifier, HubRenameNotifier>();
builder.Services.AddSingleton<RenameService>();
builder.Services.AddSingleton<IRenameService>(services => services.GetRequiredService<RenameService>());
builder.Services.AddHostedService<RenameWorker>();
builder.Services.AddSingleton<IFolderBrowser>(services => new ServerFolderBrowser(
    services.GetRequiredService<IOptions<NamerreOptions>>().Value.RootPath,
    services.GetRequiredService<IHostEnvironment>().ContentRootPath,
    services.GetRequiredService<RenameStore>().IsOwnedEntry));
builder.Services.AddControllersWithViews();
builder.Services.AddAdminAuthentication();
builder.AddOptionalTelemetry();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
_ = app.Services.GetRequiredService<AdminAccount>();

if (!string.IsNullOrWhiteSpace(app.Services.GetRequiredService<IOptions<NamerreOptions>>().Value.DataPath))
{
    app.Services.GetRequiredService<RenameStore>().Initialize();
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().RequireAuthorization();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "HTTPS is required." });
            return;
        }
    }
    await next(context);
});

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers().RequireAuthorization();
// Closing on cookie expiry keeps a long-lived socket from outliving the session that opened it.
app.MapHub<RenameHub>(RenameHub.Path, options => options.CloseOnAuthenticationExpiration = true)
    .RequireAuthorization();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
