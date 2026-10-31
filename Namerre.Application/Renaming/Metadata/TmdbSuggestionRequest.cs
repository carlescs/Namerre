namespace Namerre.Application.Renaming.Metadata;

/// <summary>
/// Asks for a proposed match for one TMDB step of one file. The server reads <see cref="InputPath"/> (the step's
/// input, as the preview traced it), searches TMDB and verifies the best candidate under this binding.
/// </summary>
public sealed record TmdbSuggestionRequest(string MediaType, string FileId, string InstanceId, string InputPath,
    string OptionsHash, string Language = "en-US");
