using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plans;

public sealed record ExecuteRenameCommand(string Id, ExecuteRequest Request) : IRequest<JobAccepted>;
