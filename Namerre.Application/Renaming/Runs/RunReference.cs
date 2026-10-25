namespace Namerre.Application.Renaming.Runs;

public sealed record RunReference(string Id, string Kind, string Status, DateTimeOffset CreatedAt);
