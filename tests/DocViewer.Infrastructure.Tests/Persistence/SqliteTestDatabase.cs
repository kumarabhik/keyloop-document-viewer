using DocViewer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocViewer.Infrastructure.Tests.Persistence;

/// <summary>
/// In-memory SQLite database that lives as long as this object (the connection must stay open).
/// Hands out fresh <see cref="DocViewerDbContext"/> instances like the production factory does.
/// </summary>
internal sealed class SqliteTestDatabase : IDbContextFactory<DocViewerDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DbContextOptions<DocViewerDbContext> _options;

    public SqliteTestDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<DocViewerDbContext>().UseSqlite(_connection).Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public DocViewerDbContext CreateDbContext() => new(_options);

    /// <summary>Simulates a database outage for every subsequent operation.</summary>
    public void Break() => _connection.Close();

    public void Dispose() => _connection.Dispose();
}
