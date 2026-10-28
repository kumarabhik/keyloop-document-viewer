using DocViewer.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DocViewer.Infrastructure.Tests.Persistence.Postgres;

/// <summary>
/// One PostgreSQL container for all tests in <see cref="PostgresGroup"/>, schema created by the committed EF
/// migrations exactly as the API does in Docker (D-019). Same image as docker-compose.yml.
/// </summary>
public sealed class PostgresContainer : IAsyncLifetime, IDbContextFactory<DocViewerDbContext>
{
    private PostgreSqlContainer? _container;
    private DbContextOptions<DocViewerDbContext>? _options;

    public async Task InitializeAsync()
    {
        if (new PostgresFactAttribute().Skip is not null)
        {
            return; // every test in the group is skipped; do not try to start Docker
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        _options = new DbContextOptionsBuilder<DocViewerDbContext>().UseNpgsql(_container.GetConnectionString()).Options;
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public DocViewerDbContext CreateDbContext() =>
        new(_options ?? throw new InvalidOperationException("The PostgreSQL container was not started."));

    /// <summary>Empties both tables so each test starts clean without a new container.</summary>
    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE source_snapshot, search_audit RESTART IDENTITY");
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
