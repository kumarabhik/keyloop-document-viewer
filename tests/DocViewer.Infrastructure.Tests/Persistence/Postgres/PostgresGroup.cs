namespace DocViewer.Infrastructure.Tests.Persistence.Postgres;

/// <summary>Shares one container and runs its tests one at a time (they truncate the same tables).</summary>
[CollectionDefinition(Name)]
public sealed class PostgresGroup : ICollectionFixture<PostgresContainer>
{
    public const string Name = "PostgreSQL";
}
