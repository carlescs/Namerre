namespace Namerre.Application.Renaming.Runs;

public sealed record JournalEntry(long Sequence, string Kind, string FileId, string FromPath, string ToPath,
    string Status);
