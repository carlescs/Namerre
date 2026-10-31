using CMediator.Abstractions;

namespace Namerre.Application.Renaming.Runs;

/// <param name="Format"><c>csv</c> or <c>json</c>.</param>
public sealed record ExportRenameRunQuery(string Id, string Format) : IRequest<RunExport>;
