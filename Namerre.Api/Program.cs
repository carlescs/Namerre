using Namerre;
using Namerre.Application;
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
    AdminAccount.GeneratePasswordHash();
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApi();
builder.AddOptionalTelemetry();

var app = builder.Build();
_ = app.Services.GetRequiredService<AdminAccount>();
_ = app.Services.GetRequiredService<OidcLogin>();

if (!string.IsNullOrWhiteSpace(app.Services.GetRequiredService<IOptions<NamerreOptions>>().Value.DataPath))
{
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
