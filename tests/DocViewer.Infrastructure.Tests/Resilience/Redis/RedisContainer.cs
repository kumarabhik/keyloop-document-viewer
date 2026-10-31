using StackExchange.Redis;
using Testcontainers.Redis;

namespace DocViewer.Infrastructure.Tests.Resilience.Redis;

/// <summary>One Redis container for every test in <see cref="RedisGroup"/>; same image as docker-compose.yml.</summary>
public sealed class RedisContainer : IAsyncLifetime
{
    private Testcontainers.Redis.RedisContainer? _container;

    /// <summary>A client connection string with the compose timeouts, as the API would use it.</summary>
    public string ConnectionString =>
        $"{_container?.GetConnectionString() ?? throw new InvalidOperationException("Redis was not started.")},abortConnect=false";

    public async Task InitializeAsync()
    {
        if (new RedisFactAttribute().Skip is not null)
        {
            return;
        }

        _container = new RedisBuilder("redis:7-alpine").Build();
        await _container.StartAsync();
    }

    /// <summary>Empties Redis so each test starts with every circuit closed.</summary>
    public async Task ResetAsync()
    {
        await using var admin = await ConnectionMultiplexer.ConnectAsync(ConnectionString + ",allowAdmin=true");
        foreach (var server in admin.GetServers())
        {
            await server.FlushAllDatabasesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
