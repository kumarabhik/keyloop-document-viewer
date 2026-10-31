using DocViewer.Domain;
using StackExchange.Redis;

namespace DocViewer.Infrastructure.Resilience;

/// <summary>
/// Breaker state shared by every replica through Redis (D-032). Per source, three keys under one hash tag, so a Redis
/// Cluster keeps them on one node: <c>:open</c> (open-until, Unix ms), <c>:window</c> (a hash of bucket counters) and
/// <c>:probe</c> (the half-open lease, <c>SET NX PX</c>).
/// </summary>
internal sealed class RedisCircuitStateStore(RedisConnection redis) : ICircuitStateStore
{
    /// <summary>
    /// A closed circuit has no <c>:open</c> key. An opened one keeps it this long after its break ends, so the next call
    /// can still find it half-open; with no traffic for that long the circuit simply closes.
    /// </summary>
    internal static readonly TimeSpan OpenStateRetention = TimeSpan.FromMinutes(10);

    // Increment the bucket, drop buckets older than the window, return the window totals: one round trip, atomic.
    private const string RecordScript = """
        redis.call('HINCRBY', KEYS[1], ARGV[1] .. ':' .. ARGV[2], 1)
        redis.call('PEXPIRE', KEYS[1], ARGV[4])
        local all = redis.call('HGETALL', KEYS[1])
        local oldest = tonumber(ARGV[3])
        local s, f = 0, 0
        for i = 1, #all, 2 do
          local bucket, kind = string.match(all[i], '^(%d+):(%a)$')
          if bucket == nil or tonumber(bucket) < oldest then
            redis.call('HDEL', KEYS[1], all[i])
          elseif kind == 's' then
            s = s + tonumber(all[i + 1])
          else
            f = f + tonumber(all[i + 1])
          end
        end
        return { s, f }
        """;

    public async Task<DateTimeOffset?> GetOpenUntilAsync(SourceSystem source)
    {
        var db = await redis.GetDatabaseAsync();
        var value = await db.StringGetAsync(Key(source, "open"));
        // A missing key must be checked first: RedisValue.Null.TryParse(out long) succeeds with 0, which read as
        // "open until 1970", i.e. half-open, so the first failure opened the circuit (caught by SharedCircuitRedisTests).
        return !value.IsNull && value.TryParse(out long untilMs) ? DateTimeOffset.FromUnixTimeMilliseconds(untilMs) : null;
    }

    public async Task<WindowCounts> RecordAsync(SourceSystem source, bool failed, DateTimeOffset now, CircuitWindow window)
    {
        var db = await redis.GetDatabaseAsync();
        var result = (RedisResult[]?)await db.ScriptEvaluateAsync(
            RecordScript,
            [Key(source, "window")],
            [window.BucketOf(now), failed ? "f" : "s", window.OldestBucket(now), (long)window.Retention.TotalMilliseconds]);
        return result is { Length: 2 } ? new WindowCounts((long)result[0], (long)result[1]) : default;
    }

    public async Task OpenAsync(SourceSystem source, DateTimeOffset until, DateTimeOffset now)
    {
        var db = await redis.GetDatabaseAsync();
        var ttl = until - now + OpenStateRetention;
        await db.StringSetAsync(Key(source, "open"), until.ToUnixTimeMilliseconds(), ttl > TimeSpan.Zero ? ttl : OpenStateRetention);
    }

    public async Task CloseAsync(SourceSystem source)
    {
        var db = await redis.GetDatabaseAsync();
        await db.KeyDeleteAsync([Key(source, "open"), Key(source, "window"), Key(source, "probe")]);
    }

    public async Task<bool> TryAcquireProbeAsync(SourceSystem source, TimeSpan lease, DateTimeOffset now)
    {
        var db = await redis.GetDatabaseAsync();
        return await db.StringSetAsync(Key(source, "probe"), now.ToUnixTimeMilliseconds(), lease, When.NotExists);
    }

    internal static string Key(SourceSystem source, string part) => $"docviewer:breaker:{{{source.ToString().ToLowerInvariant()}}}:{part}";
}
