namespace Namerre.Application.WatchFolders;

/// <summary>Traces and counts watch-folder work without tying the rules to a telemetry library.</summary>
public interface IWatchFolderDiagnostics
{
    IWatchScan StartScan(string folder);
    void Files(string folder, WatchOutcome outcome, int count);
}

/// <summary>One folder scan; dispose it when the scan ends.</summary>
public interface IWatchScan : IDisposable
{
    void Candidates(int count);
    void Fail(Exception exception);
}

/// <summary>What happened to files in a scan, as counted by <see cref="IWatchFolderDiagnostics.Files"/>.</summary>
public enum WatchOutcome { Prepared, Unchanged, Completed, Failed, Ignored }
