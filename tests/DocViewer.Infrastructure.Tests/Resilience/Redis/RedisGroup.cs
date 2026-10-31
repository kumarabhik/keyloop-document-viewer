namespace DocViewer.Infrastructure.Tests.Resilience.Redis;

/// <summary>Shares one container and runs its tests one at a time (they flush the same Redis).</summary>
[CollectionDefinition(Name)]
public sealed class RedisGroup : ICollectionFixture<RedisContainer>
{
    public const string Name = "Redis";
}
