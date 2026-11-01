using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Namerre.Infrastructure.Renaming;

public sealed class RenameDbContext(DbContextOptions<RenameDbContext> options) : DbContext(options)
{
    public DbSet<RenameDocument> Documents => Set<RenameDocument>();
    public DbSet<DurableRenameJob> Jobs => Set<DurableRenameJob>();
    public DbSet<RenameOperation> Operations => Set<RenameOperation>();
    public DbSet<RenameSetting> Settings => Set<RenameSetting>();
    public DbSet<UserRow> Users => Set<UserRow>();
    public DbSet<UserLoginRow> UserLogins => Set<UserLoginRow>();
    public DbSet<RenamePresetRow> Presets => Set<RenamePresetRow>();
    public DbSet<WatchedFileRow> WatchedFiles => Set<WatchedFileRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RenameDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);
            entity.Property(e => e.Kind).HasMaxLength(40);
            entity.Property(e => e.Status).HasMaxLength(40);
            entity.Property(e => e.ParentId).HasMaxLength(100);
            entity.Property(e => e.Revision).IsConcurrencyToken();
            entity.HasIndex(e => new { e.Kind, e.Order });
            entity.HasIndex(e => new { e.Kind, e.OwnerId, e.Order });
            entity.HasIndex(e => e.ParentId);
        });
        modelBuilder.Entity<DurableRenameJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasMaxLength(40);
            entity.Property(e => e.Status).HasMaxLength(40);
            entity.Property(e => e.Revision).IsConcurrencyToken();
            entity.HasIndex(e => new { e.Status, e.Order });
            entity.HasOne<RenameDocument>().WithMany().HasForeignKey(e => e.Target).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RenameOperation>(entity =>
        {
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasMaxLength(300);
            entity.HasOne<RenameDocument>().WithMany().HasForeignKey(e => e.Target).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<RenameSetting>().HasKey(e => e.Key);
        modelBuilder.Entity<UserRow>(entity =>
        {
            entity.ToTable("Users", table =>
                table.HasCheckConstraint("CK_Users_Role", "\"Role\" IN ('Admin', 'User')"));
            entity.HasKey(e => e.Id);
            // NOCASE keeps usernames unique ignoring (ASCII) case.
            entity.Property(e => e.Username).HasMaxLength(128).IsRequired().UseCollation("NOCASE");
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.PasswordHash).HasMaxLength(256);
            entity.Property(e => e.Role).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Stamp).HasMaxLength(64).IsRequired();
            entity.HasMany(e => e.Logins).WithOne().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserLoginRow>(entity =>
        {
            entity.ToTable("UserLogins");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Issuer).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(256).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(256).IsRequired();
            // One identity belongs to at most one local user.
            entity.HasIndex(e => new { e.Issuer, e.Subject }).IsUnique();
        });
        modelBuilder.Entity<RenamePresetRow>(entity =>
        {
            entity.ToTable("Presets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(100);
            // NOCASE keeps names unique ignoring (ASCII) case, matching the service's check.
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired().UseCollation("NOCASE");
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Json).IsRequired();
            entity.Property(e => e.Revision).IsConcurrencyToken();
        });
        modelBuilder.Entity<WatchedFileRow>(entity =>
        {
            entity.HasKey(e => new { e.Folder, e.Path });
            entity.Property(e => e.Folder).HasMaxLength(50);
            entity.Property(e => e.Path).HasMaxLength(2048);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Error).HasMaxLength(500);
            entity.Property(e => e.PlanId).HasMaxLength(100);
            entity.Property(e => e.RunId).HasMaxLength(100);
            entity.HasIndex(e => new { e.Folder, e.Status });
        });
    }
}

/// <summary>A local user; see <c>Application.Authentication.UserRecord</c>.</summary>
public sealed class UserRow
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string? PasswordHash { get; set; }
    public string Role { get; set; } = "";
    public bool Disabled { get; set; }
    public string Stamp { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSignInAt { get; set; }
    public List<UserLoginRow> Logins { get; set; } = [];
}

/// <summary>A single sign-on identity (issuer and subject) linked to a <see cref="UserRow"/>.</summary>
public sealed class UserLoginRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
}

/// <summary>A pipeline preset; <see cref="Json"/> holds its steps and run options.</summary>
public sealed class RenamePresetRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Revision { get; set; }
}

/// <summary>
/// What a watch folder did with one file, keyed by its root-relative path and fingerprinted by size and modification
/// time, so a changed file is taken again. <see cref="Status"/> is <c>Queued</c> (in a plan or run), <c>Done</c>,
/// <c>Failed</c> (retried at <see cref="NextAttemptAt"/>) or <c>Ignored</c>.
/// </summary>
public sealed class WatchedFileRow
{
    public string Folder { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public long ModifiedTicks { get; set; }
    public string Status { get; set; } = "";
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? Error { get; set; }
    public string? PlanId { get; set; }
    public string? RunId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class RenameDocument
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Json { get; set; } = "";
    /// <summary>The run this run was created from (rollbacks); saved so links survive without parsing JSON.</summary>
    public string? ParentId { get; set; }
    /// <summary>
    /// The user whose history this is: the user who prepared a plan, and the owner of a run's plan. Null for
    /// watch folders and history from before users, which only administrators see.
    /// </summary>
    public long? OwnerId { get; set; }
    public long Order { get; set; }
    public long Revision { get; set; }
}

public sealed class DurableRenameJob
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Target { get; set; } = "";
    public string Status { get; set; } = "";
    public long Order { get; set; }
    public long Revision { get; set; }
}

public sealed class RenameOperation
{
    public string Key { get; set; } = "";
    public string Target { get; set; } = "";
}

public sealed class RenameSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class RenameDbContextFactory : IDesignTimeDbContextFactory<RenameDbContext>
{
    public RenameDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<RenameDbContext>()
        .UseSqlite("Data Source=design-only.sqlite3;Pooling=False;Foreign Keys=True").Options);
}
