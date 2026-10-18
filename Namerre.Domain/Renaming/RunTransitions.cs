namespace Namerre.Domain.Renaming;

public static class RunTransitions
{
    public static bool Allows(string from, string to) => from == to || (from, to) switch
    {
        ("Queued", "Running") => true,
        ("Running", "Completed") => true,
        ("Queued" or "Running" or "Completed" or "RecoveryQueued" or "Recovering" or "Recovered", "RecoveryRequired") => true,
        ("RecoveryRequired", "RecoveryQueued") => true,
        ("RecoveryQueued", "Recovering") => true,
        ("Recovering", "Recovered") => true,
        _ => false
    };
}
