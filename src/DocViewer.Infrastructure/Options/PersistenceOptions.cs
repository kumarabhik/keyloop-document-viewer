using System.ComponentModel.DataAnnotations;

namespace DocViewer.Infrastructure.Options;

/// <summary>Snapshot and audit persistence settings (D-003, D-011).</summary>
public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    /// <summary>SQLite for zero-setup local runs and tests; PostgreSQL in Docker/production (D-019).</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>Snapshots older than this are not served as stale fallback (assumption A7).</summary>
    [Range(0.1, 720)]
    public double SnapshotMaxAgeHours { get; set; } = 24;

    /// <summary>Salt for hashing VINs in the audit table. Never log or commit a real value.</summary>
    [Required]
    [MinLength(8)]
    public string VinHashSalt { get; set; } = string.Empty;

    public TimeSpan SnapshotMaxAge => TimeSpan.FromHours(SnapshotMaxAgeHours);
}
