using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Plans;

public sealed record PrepareRenameCommand(PipelineRequest Pipeline) : IRequest<JobAccepted>;
