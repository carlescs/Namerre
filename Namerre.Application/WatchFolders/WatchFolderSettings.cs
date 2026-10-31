namespace Namerre.Application.WatchFolders;

/// <summary>A configured watch folder (<c>Namerre:WatchFolders</c>).</summary>
/// <param name="Path">Root-relative folder whose new files are renamed.</param>
/// <param name="Preset">Name of the server-side preset whose steps run.</param>
/// <param name="StableFor">How long a file's size and modification time must stay unchanged before it is taken.</param>
/// <param name="TmdbMediaType"><c>movie</c> or <c>tv</c>: what the preset's TMDB step looks up.</param>
/// <param name="TmdbAutoConfirm">Accepts a TMDB suggestion at or above this confidence (0.5–1) without a person;
/// null refuses presets with an enabled TMDB step, which then need explicit confirmation.</param>
/// <param name="MaxAttempts">Failures before a file is ignored until it changes.</param>
public sealed record WatchFolderSettings(string Name, string Path, string Preset, TimeSpan Interval, TimeSpan StableFor,
    bool Recursive = false, bool IncludeCompanions = true, string? TmdbMediaType = null, double? TmdbAutoConfirm = null,
    int MaxAttempts = 5);
