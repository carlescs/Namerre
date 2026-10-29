namespace Namerre.Application.Renaming.Plugins;

/// <param name="Folder">Root-relative folder with a trailing slash, or empty at the root.</param>
/// <param name="Stem">File name without its extension.</param>
/// <param name="Extension">Extension including its dot, or empty; plugins must not change it.</param>
public sealed record PathParts(string Folder, string Stem, string Extension);
