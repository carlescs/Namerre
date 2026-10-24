using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Previews;

public sealed record PreviewRenameQuery(PipelineRequest Pipeline) : IRequest<PreviewResult>;
