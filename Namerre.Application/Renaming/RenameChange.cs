namespace Namerre.Application.Renaming;

/// <summary>
/// Signals that a durable record changed. <see cref="Kind"/> is <c>Plan</c> or <c>Run</c>; a deleted run
/// reports <c>Deleted</c>. Clients re-read the record instead of trusting this payload.
/// </summary>
public sealed record RenameChange(string Kind, string Id, string Status);
