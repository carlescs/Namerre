using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Namerre.Application.Renaming;
using Namerre.Domain.Renaming;

namespace Namerre.Infrastructure.Renaming;

public sealed class RenameStore(BoundedFileSystem fileSystem, string? dataPath, string basePath,
    Action<string>? boundary = null) : IDisposable
{
    private readonly object gate = new();
    private FileStream? ownership;
    private FileStream? rootOwnership;
    private DbContextOptions<RenameDbContext>? options;
    private string? binding;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Initialize()
    {
        lock (gate)
        {
            if (options is not null)
            {
                CheckBinding();
                return;
            }
            if (string.IsNullOrWhiteSpace(dataPath))
                throw new RenameException("StorageUnavailable", "Configure Namerre:DataPath outside the browsable root.", 503);
            var root = fileSystem.Root;
            var data = Path.GetFullPath(dataPath, basePath);
            if (Under(data, root) || Under(root, data))
                throw new RenameException("StorageInsideRoot", "Rename data and media directories must be separate, non-nested directories.", 503);
            BoundedFileSystem.CheckAncestors(data);
            Directory.CreateDirectory(data);
            BoundedFileSystem.CheckAncestors(data);
            var ownerPath = Path.Combine(data, "owner.lock");
            var rootOwnerPath = Path.Combine(root, ".namerre-owner");
            var databasePath = Path.Combine(data, "renaming.sqlite3");
            foreach (var path in new[] { ownerPath, rootOwnerPath, databasePath, databasePath + "-wal", databasePath + "-shm" })
                BoundedFileSystem.CheckAncestors(path);
            var operation = "acquiring the data directory lock";
            try
            {
                ownership = new FileStream(ownerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                operation = "acquiring the media root lock";
                rootOwnership = new FileStream(rootOwnerPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                operation = "checking the media root identity";
                binding = fileSystem.RootBinding;
                operation = "opening and migrating the SQLite database";
                options = new DbContextOptionsBuilder<RenameDbContext>()
                    .UseSqlite($"Data Source=\"{databasePath.Replace("\"", "\"\"")}\";Pooling=False;Foreign Keys=True;Default Timeout=5")
                    .Options;
                using var db = Open();
                db.Database.Migrate();
                if (db.Database.GetPendingMigrations().Any()
                    || db.Database.GetAppliedMigrations().Except(db.Database.GetMigrations()).Any())
                    throw new RenameException("SchemaVersion", "The database schema could not be migrated.", 503);
                operation = "checking the stored root mapping";
                var previous = db.Settings.SingleOrDefault(s => s.Key == "root");
                if (previous is not null && previous.Value != binding)
                    throw new RenameException("RootChanged", "This database belongs to a different root mapping. Restore that mapping before recovery.", 503);
                if (previous is null)
                {
                    db.Settings.Add(new RenameSetting { Key = "root", Value = binding });
                    db.SaveChanges();
                }
                operation = "classifying interrupted jobs";
                ClassifyInterrupted(db);
            }
            catch (Exception exception)
            {
                ownership?.Dispose();
                rootOwnership?.Dispose();
                ownership = null;
                rootOwnership = null;
                options = null;
                if (exception is RenameException) throw;
                throw new RenameException("StorageUnavailable",
                    $"Durable storage or exclusive ownership is unavailable while {operation}.", 503,
                    innerException: exception);
            }
        }
    }

    private void CheckBinding()
    {
        if (ownership is null || rootOwnership is null || binding != fileSystem.RootBinding)
            throw new RenameException("RootChanged", "The root mapping or exclusive ownership is unavailable.", 503);
    }

    private static bool Under(string candidate, string parent)
        => candidate.TrimEnd(Path.DirectorySeparatorChar).Equals(parent.TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || candidate.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    public T Get<T>(string id, string kind)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            return Read<T>(db, id, kind);
        }
    }

    public IReadOnlyList<T> List<T>(string kind, string[]? statuses = null, int skip = 0, int? take = null)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            var query = db.Documents.AsNoTracking().Where(d => d.Kind == kind);
            if (statuses is not null) query = query.Where(d => statuses.Contains(d.Status));
            var ordered = query.OrderByDescending(d => d.Order).ThenBy(d => d.Id).Skip(skip);
            if (take is not null) ordered = ordered.Take(take.Value);
            return ordered.Select(d => d.Json).ToList().Select(json => JsonSerializer.Deserialize<T>(json, Json)!).ToArray();
        }
    }

    public void Save<T>(string id, string kind, string status, T value)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            Write(db, id, kind, status, value);
            boundary?.Invoke($"Save:{kind}:{status}:Before");
            db.SaveChanges();
            boundary?.Invoke($"Save:{kind}:{status}:After");
        }
    }

    public void CompleteRun(RunView run, string parentFileStatus)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            using var transaction = db.Database.BeginTransaction();
            Write(db, run.Id, "Run", run.Status, run);
            if (run.ParentRunId is not null)
            {
                var parent = Read<RunView>(db, run.ParentRunId, "Run");
                var ids = run.Files.Select(f => f.Id).ToHashSet();
                parent = parent with { Files = parent.Files.Select(f => ids.Contains(f.Id)
                    ? f with { Status = parentFileStatus } : f).ToArray() };
                Write(db, parent.Id, "Run", parent.Status, parent);
            }
            boundary?.Invoke("Finalize:Before");
            db.SaveChanges();
            transaction.Commit();
            boundary?.Invoke("Finalize:After");
        }
    }

    public void Enqueue<T>(string id, string kind, string status, T value, string jobKind, string? operationKey = null)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            using var transaction = db.Database.BeginTransaction();
            if (db.Jobs.Count(j => j.Status == "Queued") >= 32)
                throw new RenameException("QueueFull", "The durable job queue is full. Try again later.", 429);
            Write(db, id, kind, status, value);
            db.Jobs.Add(new DurableRenameJob { Id = id, Kind = jobKind, Target = id, Status = "Queued",
                Order = DateTime.UtcNow.Ticks, Revision = 1 });
            if (operationKey is not null) db.Operations.Add(new RenameOperation { Key = operationKey, Target = id });
            boundary?.Invoke($"Enqueue:{kind}:Before");
            db.SaveChanges();
            transaction.Commit();
            boundary?.Invoke($"Enqueue:{kind}:After");
        }
    }

    /// <summary>
    /// Deletes a settled run record together with its rollback descendants, their jobs and idempotency keys,
    /// and any plan no remaining run references. The filesystem is never touched.
    /// </summary>
    public IReadOnlyList<string> DeleteRun(string id)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            using var transaction = db.Database.BeginTransaction();
            var root = Read<RunView>(db, id, "Run");
            var runs = db.Documents.AsNoTracking().Where(d => d.Kind == "Run").Select(d => d.Json).ToList()
                .Select(json => JsonSerializer.Deserialize<RunView>(json, Json)!).ToArray();
            var deleted = new List<RunView> { root };
            for (var i = 0; i < deleted.Count; i++)
                deleted.AddRange(runs.Where(r => r.ParentRunId == deleted[i].Id));
            if (deleted.Any(r => !RunTransitions.IsSettled(r.Status)))
                throw new RenameException("RunNotSettled",
                    "Only completed or recovered runs without pending rollbacks or recovery can be deleted.", 409);
            var runIds = deleted.Select(r => r.Id).ToHashSet();
            var planIds = deleted.Select(r => r.PlanId)
                .Where(plan => !runs.Any(r => !runIds.Contains(r.Id) && r.PlanId == plan)).ToHashSet();
            var ids = runIds.Concat(planIds).ToArray();
            db.Operations.RemoveRange(db.Operations.Where(o => ids.Contains(o.Target)));
            db.Jobs.RemoveRange(db.Jobs.Where(j => ids.Contains(j.Target)));
            db.Documents.RemoveRange(db.Documents.Where(d => ids.Contains(d.Id)));
            boundary?.Invoke("Delete:Before");
            db.SaveChanges();
            transaction.Commit();
            boundary?.Invoke("Delete:After");
            return deleted.Select(r => r.Id).ToArray();
        }
    }

    public string? Operation(string key)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            return db.Operations.AsNoTracking().Where(o => o.Key == key).Select(o => o.Target).SingleOrDefault();
        }
    }

    public (string Id, string Kind)? TakeJob()
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            using var transaction = db.Database.BeginTransaction();
            var job = db.Jobs.Where(j => j.Status == "Queued").OrderBy(j => j.Order).FirstOrDefault();
            if (job is null) return null;
            job.Status = "Running";
            job.Revision++;
            db.SaveChanges();
            transaction.Commit();
            return (job.Id, job.Kind);
        }
    }

    public void FinishJob(string id)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            var job = db.Jobs.Single(j => j.Id == id);
            job.Status = "Completed";
            job.Revision++;
            db.SaveChanges();
        }
    }

    public void RequeueRecovery(RunView run, string operationKey)
    {
        Initialize();
        lock (gate)
        {
            using var db = Open();
            using var transaction = db.Database.BeginTransaction();
            if (db.Jobs.Count(j => j.Status == "Queued") >= 32)
                throw new RenameException("QueueFull", "The durable job queue is full. Try again later.", 429);
            Write(db, run.Id, "Run", run.Status, run);
            var job = db.Jobs.Single(j => j.Id == run.Id);
            job.Kind = "Recover";
            job.Status = "Queued";
            job.Revision++;
            db.Operations.Add(new RenameOperation { Key = operationKey, Target = run.Id });
            db.SaveChanges();
            transaction.Commit();
        }
    }

    private static void ClassifyInterrupted(RenameDbContext db)
    {
        using var transaction = db.Database.BeginTransaction();
        var jobs = db.Jobs.Where(j => (j.Status == "Running" || j.Status == "Queued")
            && (j.Kind != "Prepare" || j.Status == "Running")).ToList();
        foreach (var job in jobs)
        {
            if (job.Kind == "Prepare")
            {
                var plan = Read<PlanView>(db, job.Id, "Plan");
                Write(db, job.Id, "Plan", "Failed", plan with { Status = "Failed",
                    Issues = [new("Interrupted", "Preparation was interrupted. Prepare a new plan.")] });
            }
            else
            {
                var run = Read<RunView>(db, job.Id, "Run");
                Write(db, job.Id, "Run", "RecoveryRequired", run with
                {
                    Status = "RecoveryRequired", Error = "Interrupted job. Inspect evidence and explicitly recover.",
                    Files = run.Files.Select(f => f with { Status = "Uncertain" }).ToArray()
                });
            }
            job.Status = "Interrupted";
            job.Revision++;
        }
        db.SaveChanges();
        transaction.Commit();
    }

    internal RenameDbContext Open()
    {
        CheckBinding();
        return new RenameDbContext(options!);
    }

    private static T Read<T>(RenameDbContext db, string id, string kind)
    {
        var document = db.Documents.AsNoTracking().SingleOrDefault(d => d.Id == id && d.Kind == kind);
        return document is not null ? JsonSerializer.Deserialize<T>(document.Json, Json)!
            : throw new RenameException("NotFound", "The requested resource was not found.", 404);
    }

    private static void Write<T>(RenameDbContext db, string id, string kind, string status, T value)
    {
        var document = db.Documents.Local.FirstOrDefault(d => d.Id == id)
            ?? db.Documents.SingleOrDefault(d => d.Id == id);
        if (document is null)
        {
            document = new RenameDocument { Id = id, Kind = kind, Order = DateTime.UtcNow.Ticks };
            db.Documents.Add(document);
        }
        else if (document.Kind != kind) throw new RenameException("StorageConflict", "The resource kind does not match.", 409);
        else if (kind == "Run" && !RunTransitions.Allows(document.Status, status))
            throw new RenameException("InvalidTransition", "The run state does not permit this operation.", 409);
        document.Status = status;
        document.Json = JsonSerializer.Serialize(value, Json);
        document.Revision++;
    }

    public bool IsOwnedEntry(string fullPath)
    {
        if (rootOwnership is not null && fullPath == Path.Combine(fileSystem.Root, ".namerre-owner")) return true;
        if (options is null || !Path.GetFileName(fullPath).StartsWith(".namerre-stage-", StringComparison.Ordinal))
            return false;
        var path = Path.GetRelativePath(fileSystem.Root, fullPath).Replace('\\', '/');
        return List<RunView>("Run", ["Queued", "Running", "RecoveryRequired", "RecoveryQueued", "Recovering"]).Any(r => r.Files.Any(f => f.StagingPath == path)
            || r.Journal.Any(j => j.Kind != "CreateDirectory" && (j.FromPath == path || j.ToPath == path)));
    }

    public void Dispose()
    {
        lock (gate)
        {
            rootOwnership?.Dispose();
            ownership?.Dispose();
            rootOwnership = null;
            ownership = null;
            options = null;
        }
    }
}
