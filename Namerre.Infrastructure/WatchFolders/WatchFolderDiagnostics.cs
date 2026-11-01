using System.Diagnostics;
using Namerre.Application.WatchFolders;
using Namerre.Infrastructure.Diagnostics;

namespace Namerre.Infrastructure.WatchFolders;

/// <summary><see cref="IWatchFolderDiagnostics"/> over Namerre's <see cref="NamerreTelemetry"/>.</summary>
public sealed class WatchFolderDiagnostics : IWatchFolderDiagnostics
{
    public IWatchScan StartScan(string folder)
    {
        var activity = NamerreTelemetry.Source.StartActivity("watch.scan");
        activity?.SetTag("namerre.watch.folder", folder);
        return new Scan(activity);
    }

    public void Files(string folder, WatchOutcome outcome, int count)
        => NamerreTelemetry.WatchFiles.Add(count, new KeyValuePair<string, object?>("namerre.watch.folder", folder),
            new KeyValuePair<string, object?>("namerre.watch.outcome", outcome.ToString().ToLowerInvariant()));

    private sealed class Scan(Activity? activity) : IWatchScan
    {
        public void Candidates(int count) => activity?.SetTag("namerre.watch.candidates", count);

        public void Fail(Exception exception) => activity.Fail(exception);

        public void Dispose() => activity?.Dispose();
    }
}
