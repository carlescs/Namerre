using CMediator.Abstractions;

namespace Namerre.Application.Folders;

public sealed record GetFolderSelectionQuery(
    string Path, bool IncludeSubfolders, bool IncludeHidden = true)
    : IRequest<IReadOnlyList<FolderListing>>;
