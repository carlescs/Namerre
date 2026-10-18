using Namerre.Application;
using Namerre.Application.Folders;
using Namerre.Infrastructure.Folders;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Renaming;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddSingleton(services => new BoundedFileSystem(
    services.GetRequiredService<IConfiguration>()["FileBrowser:RootPath"],
    services.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddSingleton<IRenameFileSystem>(services => services.GetRequiredService<BoundedFileSystem>());
builder.Services.AddSingleton(services => new RenameStore(
    services.GetRequiredService<BoundedFileSystem>(),
    services.GetRequiredService<IConfiguration>()["Rename:DataPath"],
    services.GetRequiredService<IHostEnvironment>().ContentRootPath));
builder.Services.AddHttpClient("tmdb", client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<ITmdbMetadata>(services => new TmdbMetadata(
    services.GetRequiredService<IHttpClientFactory>().CreateClient("tmdb"),
    services.GetRequiredService<IConfiguration>(), services.GetRequiredService<RenameStore>()));
builder.Services.AddSingleton<PipelineEngine>();
builder.Services.AddSingleton<RenameService>();
builder.Services.AddSingleton<IRenameService>(services => services.GetRequiredService<RenameService>());
builder.Services.AddHostedService<RenameWorker>();
builder.Services.AddSingleton<IFolderBrowser>(services => new ServerFolderBrowser(
    services.GetRequiredService<IConfiguration>()["FileBrowser:RootPath"],
    services.GetRequiredService<IHostEnvironment>().ContentRootPath,
    services.GetRequiredService<RenameStore>().IsOwnedEntry));
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

if (!string.IsNullOrWhiteSpace(app.Configuration["Rename:DataPath"]))
{
    app.Services.GetRequiredService<RenameStore>().Initialize();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
