using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DocViewer.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef migrations</c>. Migrations target PostgreSQL (D-019); the connection
/// string is a placeholder because generating a migration never connects.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DocViewerDbContext>
{
    public DocViewerDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<DocViewerDbContext>()
            .UseNpgsql("Host=localhost;Database=docviewer;Username=docviewer;Password=design-time")
            .Options);
}
