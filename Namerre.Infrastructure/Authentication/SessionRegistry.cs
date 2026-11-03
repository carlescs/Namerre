using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Namerre.Application.Authentication;
using Namerre.Infrastructure.Renaming;

namespace Namerre.Infrastructure.Authentication;

/// <summary>
/// EF Core storage for ended sessions, in the same SQLite database as the users. This process owns the database
/// exclusively (see <see cref="RenameStore"/>), so both tables are loaded once and then served from memory; every
/// change is written through. Revoked sessions are dropped once their cookie would have expired anyway.
/// Without a data directory nothing can be persisted and revocations last until the process stops.
/// </summary>
public sealed class SessionRegistry(RenameStore store, ILogger<SessionRegistry> logger, TimeProvider? time = null)
    : ISessionRegistry
{
    private readonly object gate = new();
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, long> revoked = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<long, long> epochs = new();
    private volatile bool loaded;

    public bool IsRevoked(string sessionId)
    {
        EnsureLoaded();
        return revoked.ContainsKey(sessionId);
    }

    public long Epoch(long userId)
    {
        EnsureLoaded();
        return epochs.GetValueOrDefault(userId);
    }

    public void Revoke(string sessionId)
    {
        EnsureLoaded();
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var expiresAt = now + (long)SessionPolicy.Lifetime.TotalSeconds + 1;
        lock (gate)
        {
            // Effective at once, whatever happens to the write below.
            revoked[sessionId] = expiresAt;
            foreach (var (id, expiry) in revoked)
                if (expiry <= now)
                    revoked.TryRemove(id, out _);
            if (!store.IsConfigured)
                return;
            try
            {
                Run(db =>
                {
                    db.RevokedSessions.Where(r => r.ExpiresAtUnix <= now).ExecuteDelete();
                    if (db.RevokedSessions.Any(r => r.SessionId == sessionId))
                        return;
                    db.RevokedSessions.Add(new RevokedSessionRow { SessionId = sessionId, ExpiresAtUnix = expiresAt });
                    db.SaveChanges();
                });
            }
            catch (UserStorageException exception)
            {
                // The sign-out itself must not fail; the session stays ended until the process restarts.
                logger.LogError(exception, "Unable to save an ended session. It stays ended until Namerre restarts.");
            }
        }
    }

    public long AdvanceEpoch(long userId)
    {
        EnsureLoaded();
        lock (gate)
        {
            var next = epochs.GetValueOrDefault(userId) + 1;
            if (store.IsConfigured)
                Run(db =>
                {
                    if (db.SessionEpochs.Where(e => e.UserId == userId)
                            .ExecuteUpdate(setters => setters.SetProperty(e => e.Epoch, next)) == 0)
                    {
                        db.SessionEpochs.Add(new SessionEpochRow { UserId = userId, Epoch = next });
                        db.SaveChanges();
                    }
                });
            // Only after the write succeeded, so a failed request leaves every session as it was.
            epochs[userId] = next;
            return next;
        }
    }

    private void EnsureLoaded()
    {
        if (loaded)
            return;
        lock (gate)
        {
            if (loaded)
                return;
            if (store.IsConfigured)
            {
                var now = clock.GetUtcNow().ToUnixTimeSeconds();
                Run(db =>
                {
                    db.RevokedSessions.Where(r => r.ExpiresAtUnix <= now).ExecuteDelete();
                    foreach (var row in db.RevokedSessions.AsNoTracking())
                        revoked[row.SessionId] = row.ExpiresAtUnix;
                    foreach (var row in db.SessionEpochs.AsNoTracking())
                        epochs[row.UserId] = row.Epoch;
                });
            }
            loaded = true;
        }
    }

    private void Run(Action<RenameDbContext> action)
    {
        try
        {
            store.Initialize();
            using var db = store.Open();
            action(db);
        }
        catch (Exception exception) when (exception is SqliteException or DbUpdateException or
            InvalidOperationException or Application.Renaming.RenameException)
        {
            throw new UserStorageException("Unable to save sessions to the database.", exception);
        }
    }
}
