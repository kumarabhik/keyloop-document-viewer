using StackExchange.Redis;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// One lazily opened Redis connection for the shared breaker state (D-032). Connecting happens on the thread pool and
/// never aborts, so a Redis that is down at start-up is retried in the background instead of failing every later call.
/// </summary>
internal sealed class RedisConnection(string configuration) : IAsyncDisposable
{
    private readonly Lazy<Task<IConnectionMultiplexer>> _connection = new(() => Task.Run(() => ConnectAsync(configuration)));

    public async Task<IDatabase> GetDatabaseAsync() => (await _connection.Value).GetDatabase();

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated && _connection.Value.IsCompletedSuccessfully)
        {
            await _connection.Value.Result.DisposeAsync();
        }
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(string configuration)
    {
        var options = ConfigurationOptions.Parse(configuration);
        options.AbortOnConnectFail = false; // a faulted first connect would otherwise be cached by the Lazy forever
        return await ConnectionMultiplexer.ConnectAsync(options);
    }
}
