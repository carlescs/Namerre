using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Namerre.Application.Folders;
using Namerre.Application.Renaming.Pipeline;
using Namerre.Application.Renaming;
using Namerre.Application.Renaming.Metadata;
using Namerre.Application.Renaming.Plans;
using Namerre.Application.Renaming.Plugins;
using Namerre.Application.Renaming.Presets;
using Namerre.Application.Renaming.Runs;
using Namerre.Application.WatchFolders;
using Namerre.Infrastructure.Configuration;
using Namerre.Infrastructure.Diagnostics;
using Namerre.Infrastructure.Renaming;

namespace Namerre.Infrastructure.WatchFolders;

/// <summary>
/// Renames new files in the configured watch folders with their preset. Each scan first advances the folder's
/// batch in flight (executes its prepared plan, then records the run's outcome). Without one, it lists the folder,
/// waits until a file's size and modification time have stayed the same for <see cref="WatchFolderSettings.StableFor"/>,
/// previews the stable files, and prepares a normal plan from the ones that can be renamed, so journaling, history and
/// rollback work as for any run. A file that fails is retried with exponential back-off and ignored after
/// <see cref="WatchFolderSettings.MaxAttempts"/> until it changes. TMDB steps only run with an explicit
/// <see cref="WatchFolderSettings.TmdbAutoConfirm"/> threshold: a suggestion below it fails the file.
/// </summary>
public sealed class WatchFolderService(IOptions<NamerreOptions> options, RenameStore store, RenameService renames,
    PipelineEngine pipeline, PresetService presets, IPresetStore presetStore, TmdbSuggester suggester,
    IFolderBrowser folders, IRenameFileSystem fileSystem, ILogger<WatchFolderService> logger, TimeProvider? clock = null)
    : IWatchFolders
{
    public const string OriginPrefix = "watch:";
    public const string Queued = "Queued";
    public const string Done = "Done";
    public const string Failed = "Failed";
    public const string Ignored = "Ignored";
    /// <summary>Longest wait between attempts of a failing file.</summary>
    public static readonly TimeSpan MaxBackOff = TimeSpan.FromHours(24);
    private const int MaxProblemsListed = 100;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, FolderState> states = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim scanning = new(1);

    private IReadOnlyList<WatchFolderSettings> Settings
        => options.Value.WatchFolders.Select(folder => folder.ToSettings()).ToArray();

    /// <summary>Scans every folder whose interval has passed; the worker calls this every few seconds.</summary>
    public async Task Scan(CancellationToken cancellationToken)
    {
        if (!await scanning.WaitAsync(0, cancellationToken)) return;
        try
        {
            foreach (var settings in Settings)
            {
                var state = State(settings.Name);
                var now = time.GetUtcNow();
                if (state.LastScanAt is { } last && now - last < settings.Interval) continue;
                state.LastScanAt = now;
                System.Diagnostics.Activity? activity = null;
                try
                {
                    if (Paused(settings.Name)) continue;
                    activity = NamerreTelemetry.Source.StartActivity("watch.scan");
                    activity?.SetTag("namerre.watch.folder", settings.Name);
                    var previous = state.Error;
                    state.Error = await ScanFolder(settings, state, cancellationToken);
                    if (state.Error is not null && state.Error != previous)
                        logger.LogWarning("Watch folder {Folder} cannot run: {Problem}", settings.Name, state.Error);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (RenameException exception)
                {
                    // Busy TMDB, a full queue or pending recovery: nothing is held against the files; try next interval.
                    state.Error = exception.Message;
                    activity.Fail(exception);
                    logger.LogInformation("Watch folder {Folder} postponed: {Code}", settings.Name, exception.Code);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or FolderBrowserException or DbUpdateException)
                {
                    state.Error = exception is FolderBrowserException browser
                        ? browser.Message : "The folder or the database could not be read. The next scan tries again.";
                    activity.Fail(exception);
                    logger.LogWarning(exception, "Watch folder {Folder} scan failed", settings.Name);
                }
                finally { activity?.Dispose(); }
            }
        }
        finally { scanning.Release(); }
    }

    public IReadOnlyList<WatchFolderStatus> List() => Settings.Select(Status).ToArray();

    public WatchFolderStatus SetPaused(string name, bool paused)
    {
        var settings = Find(name);
        store.Use(db =>
        {
            var key = PausedKey(settings.Name);
            var row = db.Settings.SingleOrDefault(s => s.Key == key);
            if (paused && row is null) db.Settings.Add(new RenameSetting { Key = key, Value = "true" });
            if (!paused && row is not null) db.Settings.Remove(row);
            return db.SaveChanges();
        });
        // Resuming scans right away instead of waiting out the interval.
        if (!paused) State(settings.Name).LastScanAt = null;
        return Status(settings);
    }

    public WatchFolderStatus Retry(string name, string path)
    {
        var settings = Find(name);
        var removed = store.Use(db => db.WatchedFiles
            .Where(f => f.Folder == settings.Name && f.Path == path && (f.Status == Failed || f.Status == Ignored))
            .ExecuteDelete());
        if (removed == 0) throw new RenameException("NotFound", "This file has no failure to retry.", 404);
        State(settings.Name).LastScanAt = null;
        return Status(settings);
    }

    private async Task<string?> ScanFolder(WatchFolderSettings settings, FolderState state, CancellationToken cancellationToken)
    {
        // One batch at a time per folder: the next one starts when this one has finished.
        if (await Advance(settings, cancellationToken)) return null;
        if (presetStore.Find(settings.Preset) is not { } record)
            return $"Preset {settings.Preset} does not exist. Create it or change the watch folder's Preset.";
        var preset = presets.Get(record.Id);
        if (preset.UnknownSteps.Count > 0)
            return $"Preset {preset.Name} has steps this server no longer knows. Edit and save it again.";
        var tmdb = preset.Plugins.FirstOrDefault(p => p.Enabled && p.TypeId == "tmdb");
        if (tmdb is not null && (settings.TmdbAutoConfirm is null || settings.TmdbMediaType is null))
            return $"Preset {preset.Name} has a TMDB step, which needs explicit confirmation. Set TmdbMediaType and "
                + "TmdbAutoConfirm on the watch folder to accept suggestions above a confidence, or remove the step.";
        var candidates = await Stable(settings, state, cancellationToken);
        System.Diagnostics.Activity.Current?.SetTag("namerre.watch.candidates", candidates.Count);
        if (candidates.Count == 0) return null;
        await Process(settings, preset, tmdb, candidates, cancellationToken);
        return null;
    }

    /// <summary>Moves the batch in flight forward; true while one is still running.</summary>
    private async Task<bool> Advance(WatchFolderSettings settings, CancellationToken cancellationToken)
    {
        var queued = store.Use(db => db.WatchedFiles.AsNoTracking()
            .Where(f => f.Folder == settings.Name && f.Status == Queued).ToList());
        if (queued.Count == 0) return false;
        foreach (var batch in queued.GroupBy(f => (f.PlanId, f.RunId)))
        {
            var rows = batch.ToList();
            if (batch.Key.RunId is null)
            {
                PlanView plan;
                try { plan = store.Get<PlanView>(batch.Key.PlanId!, "Plan"); }
                catch (RenameException exception) when (exception.Code == "NotFound")
                {
                    Fail(settings, rows, "The prepared plan was deleted.");
                    continue;
                }
                if (plan.Status == "Failed")
                    Fail(settings, rows, plan.Issues.FirstOrDefault()?.Message ?? "Preparation failed.");
                else if (plan.Status == "Ready")
                {
                    // The plan ID doubles as the operation ID, so a retried execution never starts a second run.
                    var accepted = await renames.Execute(plan.Id,
                        new ExecuteRequest(plan.Version, Guid.ParseExact(plan.Id, "N").ToString()), cancellationToken);
                    Update(settings, rows, row => row.RunId = accepted.Id);
                }
                continue;
            }
            RunView run;
            try { run = store.Get<RunView>(batch.Key.RunId, "Run"); }
            catch (RenameException exception) when (exception.Code == "NotFound")
            {
                Fail(settings, rows, "The run was deleted.");
                continue;
            }
            if (run.Status == "Completed") Finish(settings, rows, run);
            else if (run.Status is "RecoveryRequired" or "Recovered" or "RecoveryQueued" or "Recovering")
                Fail(settings, rows, run.Error ?? "The run was interrupted. Inspect it in the run history.");
        }
        return store.Use(db => db.WatchedFiles.Any(f => f.Folder == settings.Name && f.Status == Queued));
    }

    /// <summary>Files whose fingerprint has been stable long enough and that are due, in path order.</summary>
    private async Task<List<(string Path, long Size, long Ticks)>> Stable(WatchFolderSettings settings, FolderState state,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = store.Use(db => db.WatchedFiles.AsNoTracking().Where(f => f.Folder == settings.Name)
            .ToDictionary(f => f.Path, StringComparer.Ordinal));
        var result = new List<(string, long, long)>();
        await foreach (var file in Files(settings.Path, settings.Recursive, cancellationToken))
        {
            seen.Add(file.Path);
            var fingerprint = (file.Size, file.Ticks);
            if (!state.Observed.TryGetValue(file.Path, out var observed) || observed.Fingerprint != fingerprint)
            {
                state.Observed[file.Path] = (fingerprint, now);
                continue;
            }
            if (now - observed.Since < settings.StableFor) continue;
            if (rows.TryGetValue(file.Path, out var row) && row.Size == file.Size && row.ModifiedTicks == file.Ticks
                && (row.Status is Done or Ignored or Queued || (row.Status == Failed && row.NextAttemptAt > now)))
                continue;
            result.Add((file.Path, file.Size, file.Ticks));
        }
        foreach (var path in state.Observed.Keys.Where(path => !seen.Contains(path)).ToArray())
            state.Observed.Remove(path);
        return result.OrderBy(f => f.Item1, StringComparer.Ordinal).Take(pipeline.Limits.MaxFilesPerRun).ToList();
    }

    private async IAsyncEnumerable<(string Path, long Size, long Ticks)> Files(string folder, bool recursive,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var pending = new Stack<string>([folder]);
        while (pending.TryPop(out var current))
        {
            // The browser skips links, junctions and Namerre's own staging files.
            var listing = await folders.Browse(current, cancellationToken);
            foreach (var file in listing.Files.Where(file => !HiddenEntries.IsHidden(file.Name)))
                yield return ((listing.Path.Length == 0 ? "" : listing.Path + "/") + file.Name, file.Length,
                    DateTime.SpecifyKind(file.LastModifiedUtc, DateTimeKind.Utc).Ticks);
            if (!recursive) continue;
            foreach (var directory in listing.Directories.Where(d => !HiddenEntries.IsHidden(d.Name)).Reverse())
                pending.Push(directory.Path);
        }
    }

    private async Task Process(WatchFolderSettings settings, PresetView preset, PluginInstance? tmdb,
        List<(string Path, long Size, long Ticks)> candidates, CancellationToken cancellationToken)
    {
        var fingerprints = candidates.ToDictionary(c => c.Path, c => (c.Size, c.Ticks), StringComparer.Ordinal);
        var request = presets.Pipeline(preset, candidates.Select(c => new FileInput(c.Path, c.Path)).ToArray(),
            settings.IncludeCompanions);
        var preview = await pipeline.Preview(request, cancellationToken);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tmdb is not null)
        {
            var hash = TmdbPlugin.OptionsHash(tmdb.Options);
            var language = tmdb.Options.TryGetProperty("language", out var value)
                && value.ValueKind == System.Text.Json.JsonValueKind.String && value.GetString() is { Length: > 0 } text
                    ? text : "en-US";
            var selections = new List<MetadataSelection>();
            foreach (var file in preview.Files)
            {
                var step = file.Steps.FirstOrDefault(s => s.InstanceId == tmdb.InstanceId
                    && s.Error is { Code: "MetadataRequired" });
                if (step is null) continue;
                // TMDB being busy or rate-limited propagates, so the batch waits for the next scan without failures.
                var suggestion = await suggester.Suggest(new(settings.TmdbMediaType!, file.Id, step.InstanceId,
                    step.InputPath, hash, language), cancellationToken);
                if (suggestion.Match?.ConfirmationToken is { } token && suggestion.Confidence >= settings.TmdbAutoConfirm)
                    selections.Add(new(file.Id, step.InstanceId, step.InputPath, hash, token));
                else
                    failures[file.Id] = suggestion.Match is null
                        ? suggestion.Problem ?? "No TMDB match was found."
                        : $"The TMDB suggestion ({suggestion.Match.Title}, {Percent(suggestion.Confidence)}) is below the "
                            + $"{Percent(settings.TmdbAutoConfirm!.Value)} auto-confirm threshold. Rename it by hand.";
            }
            if (selections.Count > 0)
            {
                request = request with { Selections = selections };
                preview = await pipeline.Preview(request, cancellationToken);
            }
        }
        foreach (var file in preview.Files.Where(f => f.Issues.Count > 0 && !failures.ContainsKey(f.Id)))
            failures[file.Id] = file.Issues[0].Message;
        // A video and its related files go together: one failing member holds back the group.
        foreach (var group in preview.Files.GroupBy(f => f.CompanionOf ?? f.Id).Where(g => g.Any(f => failures.ContainsKey(f.Id))))
            foreach (var file in group.Where(f => !failures.ContainsKey(f.Id)))
                failures[file.Id] = "Waits for a related file that cannot be renamed.";
        var ready = preview.Files.Where(f => !failures.ContainsKey(f.Id)).ToArray();
        var unchanged = ready.All(f => f.Unchanged);
        var now = time.GetUtcNow();
        store.Use(db =>
        {
            foreach (var (path, error) in failures) FailRow(db, settings, path, fingerprints[path], error, now);
            // Names the preset leaves as they are need no run.
            if (unchanged)
                foreach (var file in ready) Upsert(db, settings.Name, file.Id, fingerprints[file.Id], Done, now);
            return db.SaveChanges();
        });
        if (failures.Count > 0)
            logger.LogInformation("Watch folder {Folder}: {Count} files cannot be renamed", settings.Name, failures.Count);
        if (unchanged) Count(settings, "unchanged", ready.Length);
        if (ready.Length == 0 || unchanged) return;
        var ids = ready.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var subset = request with
        {
            Files = request.Files.Where(f => ids.Contains(f.Id)).ToArray(),
            Selections = (request.Selections ?? []).Where(s => ids.Contains(s.FileId)).ToArray()
        };
        var accepted = await renames.Prepare(subset, OriginPrefix + settings.Name, cancellationToken);
        store.Use(db =>
        {
            foreach (var id in ids)
            {
                var row = Upsert(db, settings.Name, id, fingerprints[id], Queued, now);
                row.PlanId = accepted.Id;
                row.RunId = null;
            }
            return db.SaveChanges();
        });
        Count(settings, "prepared", ids.Count);
        logger.LogInformation("Watch folder {Folder} prepared plan {Plan} for {Count} files", settings.Name, accepted.Id, ids.Count);
    }

    private void Finish(WatchFolderSettings settings, List<WatchedFileRow> rows, RunView run)
    {
        var now = time.GetUtcNow();
        store.Use(db =>
        {
            foreach (var row in rows)
            {
                // The renamed file is recorded under its new path, so a target inside the folder is not taken again.
                var target = run.Files.FirstOrDefault(f => f.Id == row.Path)?.TargetPath ?? row.Path;
                (long, long) fingerprint = (row.Size, row.ModifiedTicks);
                try
                {
                    var facts = fileSystem.Facts(target);
                    fingerprint = (facts.Size, facts.ModifiedUtc.UtcTicks);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                    or RenameException or ArgumentException) { }
                if (target != row.Path)
                    db.WatchedFiles.Where(f => f.Folder == row.Folder && f.Path == row.Path).ExecuteDelete();
                var done = Upsert(db, settings.Name, target, fingerprint, Done, now);
                done.PlanId = run.PlanId;
                done.RunId = run.Id;
            }
            return db.SaveChanges();
        });
        Count(settings, "completed", rows.Count);
        logger.LogInformation("Watch folder {Folder} renamed {Count} files in run {RunId}", settings.Name, rows.Count, run.Id);
    }

    private void Fail(WatchFolderSettings settings, List<WatchedFileRow> rows, string error)
    {
        var now = time.GetUtcNow();
        store.Use(db =>
        {
            foreach (var row in rows) FailRow(db, settings, row.Path, (row.Size, row.ModifiedTicks), error, now);
            return db.SaveChanges();
        });
        logger.LogWarning("Watch folder {Folder}: a batch of {Count} files failed: {Error}", settings.Name, rows.Count, error);
    }

    private void Update(WatchFolderSettings settings, List<WatchedFileRow> rows, Action<WatchedFileRow> change)
        => store.Use(db =>
        {
            var paths = rows.Select(r => r.Path).ToArray();
            foreach (var row in db.WatchedFiles.Where(f => f.Folder == settings.Name && paths.Contains(f.Path))) change(row);
            return db.SaveChanges();
        });

    private void FailRow(RenameDbContext db, WatchFolderSettings settings, string path,
        (long Size, long Ticks) fingerprint, string error, DateTimeOffset now)
    {
        var existing = db.WatchedFiles.SingleOrDefault(f => f.Folder == settings.Name && f.Path == path);
        // A changed file starts counting again.
        var attempts = (existing is not null && existing.Size == fingerprint.Size
            && existing.ModifiedTicks == fingerprint.Ticks ? existing.Attempts : 0) + 1;
        var row = Upsert(db, settings.Name, path, fingerprint, attempts >= settings.MaxAttempts ? Ignored : Failed, now);
        row.Attempts = attempts;
        row.Error = error.Length > 500 ? error[..500] : error;
        row.PlanId = null;
        row.RunId = null;
        row.NextAttemptAt = row.Status == Ignored ? null : now + BackOff(settings.Interval, attempts);
        Count(settings, row.Status == Ignored ? "ignored" : "failed", 1);
        if (row.Status == Ignored)
            logger.LogWarning("Watch folder {Folder} ignores {Path} after {Attempts} failed attempts until it changes",
                settings.Name, path, attempts);
    }

    private static void Count(WatchFolderSettings settings, string outcome, int files)
        => NamerreTelemetry.WatchFiles.Add(files, new KeyValuePair<string, object?>("namerre.watch.folder", settings.Name),
            new KeyValuePair<string, object?>("namerre.watch.outcome", outcome));

    private static string Percent(double value)
        => Math.Round(value * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

    /// <summary>The interval after the first failure, doubling with each further one, at most <see cref="MaxBackOff"/>.</summary>
    public static TimeSpan BackOff(TimeSpan interval, int attempts)
        => TimeSpan.FromTicks((long)Math.Min(MaxBackOff.Ticks, interval.Ticks * Math.Pow(2, Math.Max(0, attempts - 1))));

    private static WatchedFileRow Upsert(RenameDbContext db, string folder, string path, (long Size, long Ticks) fingerprint,
        string status, DateTimeOffset now)
    {
        var row = db.WatchedFiles.Local.FirstOrDefault(f => f.Folder == folder && f.Path == path)
            ?? db.WatchedFiles.SingleOrDefault(f => f.Folder == folder && f.Path == path);
        if (row is null)
        {
            row = new WatchedFileRow { Folder = folder, Path = path };
            db.WatchedFiles.Add(row);
        }
        row.Size = fingerprint.Size;
        row.ModifiedTicks = fingerprint.Ticks;
        row.Status = status;
        row.UpdatedAt = now;
        if (status is Done or Queued)
        {
            row.Attempts = 0;
            row.Error = null;
            row.NextAttemptAt = null;
        }
        return row;
    }

    private WatchFolderStatus Status(WatchFolderSettings settings)
    {
        var state = State(settings.Name);
        var (inFlight, completed, problems) = store.Use(db =>
        {
            var files = db.WatchedFiles.AsNoTracking().Where(f => f.Folder == settings.Name);
            return (files.Count(f => f.Status == Queued), files.Count(f => f.Status == Done),
                files.Where(f => f.Status == Failed || f.Status == Ignored).AsEnumerable()
                    .OrderByDescending(f => f.UpdatedAt).Take(MaxProblemsListed)
                    .Select(f => new WatchedFileProblem(f.Path, f.Status, f.Attempts, f.Error, f.NextAttemptAt, f.UpdatedAt))
                    .ToArray());
        });
        return new(settings.Name, settings.Path, settings.Preset, Paused(settings.Name), state.LastScanAt, inFlight,
            completed, state.Error, problems, settings.TmdbAutoConfirm);
    }

    private bool Paused(string name)
    {
        var key = PausedKey(name);
        return store.Use(db => db.Settings.AsNoTracking().Any(s => s.Key == key));
    }

    private WatchFolderSettings Find(string name)
        => Settings.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new RenameException("NotFound", "The requested resource was not found.", 404);

    private FolderState State(string name) => states.GetOrAdd(name, _ => new FolderState());

    private static string PausedKey(string name) => "watch-paused:" + name.ToLowerInvariant();

    private sealed class FolderState
    {
        public DateTimeOffset? LastScanAt { get; set; }
        public string? Error { get; set; }
        /// <summary>Last fingerprint of each listed file and since when it has been unchanged.</summary>
        public Dictionary<string, ((long Size, long Ticks) Fingerprint, DateTimeOffset Since)> Observed { get; }
            = new(StringComparer.Ordinal);
    }
}
