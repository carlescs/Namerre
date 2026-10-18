namespace Namerre.Infrastructure.Configuration;

public sealed class NamerreOptions
{
    public const string SectionName = "Namerre";

    public string? RootPath { get; set; }
    public string? DataPath { get; set; }
    public AuthenticationOptions Authentication { get; set; } = new();
    public TmdbOptions Tmdb { get; set; } = new();
}

public sealed class AuthenticationOptions
{
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
}

public sealed class TmdbOptions
{
    public string? ReadAccessToken { get; set; }
    public string? Language { get; set; } = "en";
}
