using Namerre.Application.Renaming;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Turns a failure into the code and message clients may see; internal details stay in the logs.</summary>
internal static class RenameIssues
{
    public static RenameIssue Public(Exception exception) => exception is RenameException rename
        ? new(rename.Code, rename.Message) : exception is OperationCanceledException
        ? new("Interrupted", "The job stopped at a journaled boundary. Inspect recovery before continuing.")
        : new("OperationFailed", "A filesystem or durable-storage operation failed. Inspect recovery evidence; no overwrites are allowed.");
}
