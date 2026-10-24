using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

public sealed record DeleteRenameRunCommand(string Id) : IRequest<IReadOnlyList<string>>;
