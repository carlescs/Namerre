using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Namerre.Application.Renaming.Runs;

/// <summary>
/// Formats a run's files as CSV (RFC 4180, UTF-8 with a byte order mark, CRLF rows) or JSON. Each file carries the
/// most recent rollback run that included it, if any.
/// </summary>
public static class RunExportWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly string[] Columns =
    [
        "runId", "runKind", "runStatus", "runCreatedAt", "fileId", "companionOf", "sourcePath", "targetPath",
        "status", "errorCode", "error", "rollbackRunId", "rollbackStatus", "rollbackCreatedAt"
    ];

    public sealed record Rollback(string RunId, string Status, string FileStatus, DateTimeOffset CreatedAt);

    public sealed record ExportedFile(string FileId, string? CompanionOf, string SourcePath, string TargetPath,
        string Status, string? ErrorCode, string? Error, Rollback? Rollback);

    public sealed record Document(string RunId, string PlanId, string? ParentRunId, string Kind, string Status,
        DateTimeOffset CreatedAt, string? Error, IReadOnlyList<ExportedFile> Files);

    public static RunExport Write(RunView run, IReadOnlyList<RunView> rollbacks, string format)
    {
        var document = Build(run, rollbacks);
        var name = $"namerre-run-{run.Id}.{format}";
        return format == "json"
            ? new(name, "application/json", JsonSerializer.SerializeToUtf8Bytes(document, Json))
            : new(name, "text/csv; charset=utf-8", Csv(document));
    }

    public static Document Build(RunView run, IReadOnlyList<RunView> rollbacks)
    {
        var ordered = rollbacks.OrderBy(r => r.CreatedAt).ToArray();
        return new(run.Id, run.PlanId, run.ParentRunId, run.Kind, run.Status, run.CreatedAt, run.Error,
            run.Files.Select(file =>
            {
                var rollback = ordered.LastOrDefault(r => r.Files.Any(f => f.Id == file.Id));
                return new ExportedFile(file.Id, file.CompanionOf, file.SourcePath, file.TargetPath, file.Status,
                    file.ErrorCode, file.Error, rollback is null ? null
                        : new Rollback(rollback.Id, rollback.Status,
                            rollback.Files.First(f => f.Id == file.Id).Status, rollback.CreatedAt));
            }).ToArray());
    }

    private static byte[] Csv(Document document)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Columns);
        foreach (var file in document.Files)
        {
            AppendRow(csv, [
                document.RunId, document.Kind, document.Status, Time(document.CreatedAt), file.FileId,
                file.CompanionOf, file.SourcePath, file.TargetPath, file.Status, file.ErrorCode, file.Error,
                file.Rollback?.RunId, file.Rollback?.Status, file.Rollback is null ? null : Time(file.Rollback.CreatedAt)
            ]);
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static void AppendRow(StringBuilder csv, IReadOnlyList<string?> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) csv.Append(',');
            csv.Append(Cell(cells[i] ?? ""));
        }
        csv.Append("\r\n");
    }

    /// <summary>
    /// Quotes cells that need it and defuses spreadsheet formulas: a cell starting with <c>=</c>, <c>+</c>,
    /// <c>-</c>, <c>@</c>, a tab or a carriage return gets a leading apostrophe.
    /// </summary>
    internal static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Time(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
}
