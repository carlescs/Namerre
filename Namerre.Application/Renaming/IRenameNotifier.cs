namespace Namerre.Application.Renaming;

/// <summary>
/// Pushes change signals to connected clients. Implementations must not block or throw into the caller:
/// notification is best effort and never part of the durable journal.
/// </summary>
public interface IRenameNotifier
{
    void Changed(RenameChange change);
}
