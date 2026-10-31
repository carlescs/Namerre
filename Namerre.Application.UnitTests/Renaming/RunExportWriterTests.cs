using System.Text;
using System.Text.Json;
using Namerre.Application.Renaming.Runs;
using Xunit;

namespace Namerre.Application.UnitTests.Renaming;

public sealed class RunExportWriterTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 31, 9, 30, 0, TimeSpan.Zero);

    private static readonly RunView Run = new("run1", "plan1", null, "Rename", "Completed",
    [
        new("0", "a/one.txt", "a/One.txt", "", "hash", "RolledBack"),
        new("1", "a/=cmd.txt", "a/b,\"c\".txt", "", "hash", "Completed", "Changed, \"quoted\"", "HashMismatch",
            CompanionOf: "0")
    ], [], Created);

    private static readonly RunView Rollback = new("undo1", "plan1", "run1", "Rollback", "Completed",
        [new("0", "a/One.txt", "a/one.txt", "", "hash", "Completed")], [], Created.AddHours(1));

    [Fact]
    public void CsvIsUtf8WithByteOrderMarkCrlfRowsAndQuotedCells()
    {
        var export = RunExportWriter.Write(Run, [Rollback], "csv");

        Assert.Equal(("namerre-run-run1.csv", "text/csv; charset=utf-8"), (export.FileName, export.ContentType));
        Assert.Equal(Encoding.UTF8.GetPreamble(), export.Content[..3]);
        var rows = Encoding.UTF8.GetString(export.Content[3..]).Split("\r\n");
        Assert.Equal(
        [
            "runId,runKind,runStatus,runCreatedAt,fileId,companionOf,sourcePath,targetPath,status,errorCode,error,"
                + "rollbackRunId,rollbackStatus,rollbackCreatedAt",
            "run1,Rename,Completed,2026-10-31T09:30:00.0000000+00:00,0,,a/one.txt,a/One.txt,RolledBack,,,"
                + "undo1,Completed,2026-10-31T10:30:00.0000000+00:00",
            "run1,Rename,Completed,2026-10-31T09:30:00.0000000+00:00,1,0,a/=cmd.txt,\"a/b,\"\"c\"\".txt\",Completed,"
                + "HashMismatch,\"Changed, \"\"quoted\"\"\",,,",
            ""
        ], rows);
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("\tx", "'\tx")]
    [InlineData("=a,b", "\"'=a,b\"")]
    [InlineData("a=b", "a=b")]
    [InlineData("", "")]
    public void CellsThatSpreadsheetsWouldEvaluateAreDefused(string value, string expected)
        => Assert.Equal(expected, RunExportWriter.Cell(value));

    [Fact]
    public void JsonListsFilesWithTheirLatestRollback()
    {
        var later = Rollback with { Id = "undo2", Status = "RecoveryRequired", CreatedAt = Created.AddHours(2),
            Files = [Rollback.Files[0] with { Status = "Uncertain" }] };

        var export = RunExportWriter.Write(Run, [later, Rollback], "json");

        Assert.Equal(("namerre-run-run1.json", "application/json"), (export.FileName, export.ContentType));
        using var document = JsonDocument.Parse(export.Content);
        var root = document.RootElement;
        Assert.Equal("run1", root.GetProperty("runId").GetString());
        var files = root.GetProperty("files");
        Assert.Equal(2, files.GetArrayLength());
        var rollback = files[0].GetProperty("rollback");
        Assert.Equal(("undo2", "RecoveryRequired", "Uncertain"), (rollback.GetProperty("runId").GetString(),
            rollback.GetProperty("status").GetString(), rollback.GetProperty("fileStatus").GetString()));
        Assert.Equal(JsonValueKind.Null, files[1].GetProperty("rollback").ValueKind);
        Assert.Equal("a/b,\"c\".txt", files[1].GetProperty("targetPath").GetString());
    }
}
