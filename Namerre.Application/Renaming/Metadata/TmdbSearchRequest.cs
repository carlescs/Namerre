namespace Namerre.Application.Renaming.Metadata;

public sealed record TmdbSearchRequest(string MediaType, string Query, string Language = "en-US",
    int? ShowId = null, int? Season = null, int? Episode = null, int? Id = null,
    string? FileId = null, string? InstanceId = null, string? InputPath = null, string? OptionsHash = null);
