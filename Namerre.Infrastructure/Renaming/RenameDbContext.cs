using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Namerre.Infrastructure.Renaming;

public sealed class RenameDbContext(DbContextOptions<RenameDbContext> options) : DbContext(options)
{
    public DbSet<RenameDocument> Documents => Set<RenameDocument>();
    public DbSet<DurableRenameJob> Jobs => Set<DurableRenameJob>();
    public DbSet<RenameOperation> Operations => Set<RenameOperation>();
    public DbSet<RenameSetting> Settings => Set<RenameSetting>();
    public DbSet<AdministratorAccount> Administrators => Set<AdministratorAccount>();

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
        modelBuilder.Entity<AdministratorAccount>(entity =>
        {
            entity.ToTable("Administrators", table =>
                table.HasCheckConstraint("CK_Administrators_Singleton", "\"Id\" = 1"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Username).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PasswordHash).HasMaxLength(256).IsRequired();
        });
    }
}

public sealed class AdministratorAccount
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public sealed class RenameDocument
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Json { get; set; } = "";
    /// <summary>The run this run was created from (rollbacks); saved so links survive without parsing JSON.</summary>
    public string? ParentId { get; set; }
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
