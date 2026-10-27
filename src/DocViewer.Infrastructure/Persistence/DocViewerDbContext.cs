using Microsoft.EntityFrameworkCore;

namespace DocViewer.Infrastructure.Persistence;

public sealed class DocViewerDbContext(DbContextOptions<DocViewerDbContext> options) : DbContext(options)
{
    public DbSet<SourceSnapshotEntity> SourceSnapshots => Set<SourceSnapshotEntity>();

    public DbSet<SearchAuditEntity> SearchAudits => Set<SearchAuditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        var jsonColumnType = Database.IsNpgsql() ? "jsonb" : null;

        modelBuilder.Entity<SourceSnapshotEntity>(e =>
        {
            e.ToTable("source_snapshot");
            e.HasKey(x => new { x.Vin, x.Source });
            e.Property(x => x.Vin).HasColumnName("vin").HasMaxLength(17).IsFixedLength();
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(16);
            e.Property(x => x.DocumentsJson).HasColumnName("documents").HasColumnType(jsonColumnType);
            e.Property(x => x.FetchedAt).HasColumnName("fetched_at");
        });

        modelBuilder.Entity<SearchAuditEntity>(e =>
        {
            e.ToTable("search_audit");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(32);
            e.Property(x => x.VinHash).HasColumnName("vin_hash").HasMaxLength(64).IsFixedLength();
            e.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(16);
            e.Property(x => x.CacheHit).HasColumnName("cache_hit");
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            e.Property(x => x.SourceStatusJson).HasColumnName("source_status").HasColumnType(jsonColumnType);
            e.Property(x => x.DocumentCount).HasColumnName("doc_count");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_search_audit_created_at");
        });
    }
}
