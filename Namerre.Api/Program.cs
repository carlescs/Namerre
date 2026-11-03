using Namerre;
using Namerre.Application;
using Namerre.Application.Authentication;
using Namerre.Authentication;
using Namerre.Hosting;
using Namerre.Infrastructure;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Renaming;
using Namerre.Notifications;
using Namerre.Security;
using Namerre.Telemetry;
using Microsoft.Extensions.Options;

if (args is ["--hash-password"])
{
    Passwords.GeneratePasswordHash();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi();
builder.AddOptionalTelemetry();

var app = builder.Build();
_ = app.Services.GetRequiredService<UserAccounts>();
_ = app.Services.GetRequiredService<OidcLogin>();
_ = app.Services.GetRequiredService<PublicOrigin>();
var passkeyLogin = app.Services.GetRequiredService<PasskeyLogin>();
if (passkeyLogin.Enabled && !passkeyLogin.Pinned)
    app.Logger.LogWarning("Passkeys are only available at localhost until Namerre:PublicOrigin (or Namerre:Authentication:Passkeys:Origin) is set to the public HTTPS origin.");

var namerre = app.Services.GetRequiredService<IOptions<NamerreOptions>>().Value;
if (!string.IsNullOrWhiteSpace(namerre.DataPath))
{
    if (string.IsNullOrWhiteSpace(namerre.DataProtection.Certificate))
        app.Logger.Log(app.Environment.IsDevelopment() ? LogLevel.Information : LogLevel.Warning,
            "Data Protection keys in the 'keys' folder of the data directory are stored unencrypted, so anyone who can "
            + "read a copy of that directory (a backup, for example) can forge session cookies. Protect the data "
            + "directory like a credential, or encrypt the keys with Namerre:DataProtection:Certificate.");
    app.Services.GetRequiredService<RenameStore>().Initialize();
}

// Configure the HTTP request pipeline.
app.UseForwardedHeaders();
app.UseSecurityHeaders();

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
app.UseStaticFiles(StaticFileCaching.Options);

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
app.MapFallbackToFile("index.html", StaticFileCaching.Options);

app.Run();

public partial class Program;
