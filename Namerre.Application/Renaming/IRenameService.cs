using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Previews;
using Namerre.Application.Renaming.Recovery;
using Namerre.Application.Renaming.Rollback;
using Namerre.Application.Renaming.Runs;

namespace Namerre.Application.Renaming;

public interface IRenameService
{
    Task<PreviewResult> Preview(PipelineRequest request, CancellationToken cancellationToken);
    Task<JobAccepted> Prepare(PipelineRequest request, CancellationToken cancellationToken);
    Task<PlanView> GetPlan(string id, CancellationToken cancellationToken);
    Task<JobAccepted> Execute(string id, ExecuteRequest request, CancellationToken cancellationToken);
    Task<RunPage> GetRuns(int page, int pageSize, CancellationToken cancellationToken);
    Task<RunView> GetRun(string id, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> DeleteRun(string id, CancellationToken cancellationToken);
    Task<SelectionPreview> RollbackPreview(string id, SelectionRequest request, CancellationToken cancellationToken);
    Task<JobAccepted> Rollback(string id, SelectionRequest request, CancellationToken cancellationToken);
    Task<RecoveryPreview> PreviewRecovery(string id, CancellationToken cancellationToken);
    Task<JobAccepted> Recover(string id, RecoveryRequest request, CancellationToken cancellationToken);
}
