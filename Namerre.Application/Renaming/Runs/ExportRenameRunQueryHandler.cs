using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

internal sealed class ExportRenameRunQueryHandler(IRenameService service)
    : IRequestHandler<ExportRenameRunQuery, RunExport>
{
    public async Task<RunExport> Handle(ExportRenameRunQuery request, CancellationToken cancellationToken)
    {
        var format = request.Format?.ToLowerInvariant();
        if (format is not ("csv" or "json"))
            throw new RenameException("InvalidExportFormat", "Export as csv or json.");
        var run = await service.GetRun(request.Id, cancellationToken);
        var rollbacks = new List<RunView>();
        foreach (var reference in run.RollbackRuns ?? [])
        {
            if (reference.Kind == "Rollback") rollbacks.Add(await service.GetRun(reference.Id, cancellationToken));
        }
        return RunExportWriter.Write(run, rollbacks, format);
    }
}
