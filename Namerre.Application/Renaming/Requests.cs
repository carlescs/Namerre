using CMediator.Abstractions;

namespace Namerre.Application.Renaming;

public sealed record GetRenamePluginsQuery : IRequest<IReadOnlyList<PluginDescriptor>>;
public sealed record PreviewRenameQuery(PipelineRequest Pipeline) : IRequest<PreviewResult>;
public sealed record PrepareRenameCommand(PipelineRequest Pipeline) : IRequest<JobAccepted>;
public sealed record GetRenamePlanQuery(string Id) : IRequest<PlanView>;
public sealed record ExecuteRenameCommand(string Id, ExecuteRequest Request) : IRequest<JobAccepted>;
public sealed record GetRenameRunsQuery(int Page, int PageSize) : IRequest<RunPage>;
public sealed record GetRenameRunQuery(string Id) : IRequest<RunView>;
public sealed record PreviewRollbackQuery(string Id, SelectionRequest Selection) : IRequest<SelectionPreview>;
public sealed record RollbackRenameCommand(string Id, SelectionRequest Selection) : IRequest<JobAccepted>;
public sealed record PreviewRecoveryQuery(string Id) : IRequest<RecoveryPreview>;
public sealed record RecoverRenameCommand(string Id, RecoveryRequest Request) : IRequest<JobAccepted>;
public sealed record SearchTmdbQuery(TmdbSearchRequest Request) : IRequest<TmdbSearchResult>;

internal sealed class GetRenamePluginsQueryHandler : IRequestHandler<GetRenamePluginsQuery, IReadOnlyList<PluginDescriptor>>
{
    public Task<IReadOnlyList<PluginDescriptor>> Handle(GetRenamePluginsQuery request, CancellationToken cancellationToken)
        => Task.FromResult(PipelineEngine.Catalog);
}
internal sealed class PreviewRenameQueryHandler(IRenameService service) : IRequestHandler<PreviewRenameQuery, PreviewResult>
{
    public Task<PreviewResult> Handle(PreviewRenameQuery request, CancellationToken cancellationToken)
        => service.Preview(request.Pipeline, cancellationToken);
}
internal sealed class PrepareRenameCommandHandler(IRenameService service) : IRequestHandler<PrepareRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(PrepareRenameCommand request, CancellationToken cancellationToken)
        => service.Prepare(request.Pipeline, cancellationToken);
}
internal sealed class GetRenamePlanQueryHandler(IRenameService service) : IRequestHandler<GetRenamePlanQuery, PlanView>
{
    public Task<PlanView> Handle(GetRenamePlanQuery request, CancellationToken cancellationToken)
        => service.GetPlan(request.Id, cancellationToken);
}
internal sealed class ExecuteRenameCommandHandler(IRenameService service) : IRequestHandler<ExecuteRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(ExecuteRenameCommand request, CancellationToken cancellationToken)
        => service.Execute(request.Id, request.Request, cancellationToken);
}
internal sealed class GetRenameRunsQueryHandler(IRenameService service) : IRequestHandler<GetRenameRunsQuery, RunPage>
{
    public Task<RunPage> Handle(GetRenameRunsQuery request, CancellationToken cancellationToken)
        => service.GetRuns(request.Page, request.PageSize, cancellationToken);
}
internal sealed class GetRenameRunQueryHandler(IRenameService service) : IRequestHandler<GetRenameRunQuery, RunView>
{
    public Task<RunView> Handle(GetRenameRunQuery request, CancellationToken cancellationToken)
        => service.GetRun(request.Id, cancellationToken);
}
internal sealed class PreviewRollbackQueryHandler(IRenameService service) : IRequestHandler<PreviewRollbackQuery, SelectionPreview>
{
    public Task<SelectionPreview> Handle(PreviewRollbackQuery request, CancellationToken cancellationToken)
        => service.RollbackPreview(request.Id, request.Selection, cancellationToken);
}
internal sealed class RollbackRenameCommandHandler(IRenameService service) : IRequestHandler<RollbackRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(RollbackRenameCommand request, CancellationToken cancellationToken)
        => service.Rollback(request.Id, request.Selection, cancellationToken);
}
internal sealed class PreviewRecoveryQueryHandler(IRenameService service) : IRequestHandler<PreviewRecoveryQuery, RecoveryPreview>
{
    public Task<RecoveryPreview> Handle(PreviewRecoveryQuery request, CancellationToken cancellationToken)
        => service.PreviewRecovery(request.Id, cancellationToken);
}
internal sealed class RecoverRenameCommandHandler(IRenameService service) : IRequestHandler<RecoverRenameCommand, JobAccepted>
{
    public Task<JobAccepted> Handle(RecoverRenameCommand request, CancellationToken cancellationToken)
        => service.Recover(request.Id, request.Request, cancellationToken);
}
internal sealed class SearchTmdbQueryHandler(ITmdbMetadata metadata) : IRequestHandler<SearchTmdbQuery, TmdbSearchResult>
{
    public Task<TmdbSearchResult> Handle(SearchTmdbQuery request, CancellationToken cancellationToken)
        => metadata.Search(request.Request, cancellationToken);
}
