using CMediator.Abstractions;

namespace Namerre.Application.Folders;

public sealed record GetFolderSelectionQuery(string Path, bool IncludeSubfolders)
    : IRequest<IReadOnlyList<FolderListing>>;
