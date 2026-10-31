using Namerre.Application.Renaming.Runs;

namespace Namerre.Application.Renaming;

/// <summary>
/// Tells configured media servers or a webhook that a run finished. Implementations must not block or throw into
/// the caller: delivery is best effort and never part of the durable journal.
/// </summary>
public interface ILibraryNotifier
{
    void RunFinished(RunView run);
}
