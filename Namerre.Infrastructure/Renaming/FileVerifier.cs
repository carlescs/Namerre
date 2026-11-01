using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;

namespace Namerre.Infrastructure.Renaming;

/// <summary>Checks that a file still matches the fingerprint recorded when its plan was prepared.</summary>
internal sealed class FileVerifier(IRenameFileSystem fileSystem)
{
    public async Task Verify(string path, string expectedHash, CancellationToken cancellationToken, string? fileId = null)
    {
        try
        {
            if (await Fingerprint(path, expectedHash, cancellationToken) != expectedHash)
                throw new RenameException("HashMismatch", IsStamp(expectedHash)
                    ? "File size or modification time changed since preparation. It was not moved."
                    : "File content changed since preparation. It was not moved.", fileId: fileId);
        }
        catch (Exception exception)
        {
            if (fileId is not null) exception.Data["fileId"] = fileId;
            throw;
        }
    }

    /// <summary>Recomputes the fingerprint kind recorded at preparation: a content hash or a size/time stamp.</summary>
    public async Task<string> Fingerprint(string path, string expected, CancellationToken cancellationToken)
        => IsStamp(expected) ? fileSystem.Stamp(path).Hash : (await fileSystem.Hash(path, cancellationToken)).Hash;

    private static bool IsStamp(string fingerprint) => fingerprint.StartsWith(BoundedFileSystem.StampPrefix, StringComparison.Ordinal);
}
