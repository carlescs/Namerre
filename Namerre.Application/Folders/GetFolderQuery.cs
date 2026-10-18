using CMediator.Abstractions;

namespace Namerre.Application.Folders;

public sealed record GetFolderQuery(string Path) : IRequest<FolderListing>;
