using DocViewer.Infrastructure.Tests.Persistence.Postgres;

namespace DocViewer.Infrastructure.Tests.Resilience.Redis;

/// <summary>A test that needs a real Redis (D-032); skipped with a reason when Docker is not reachable.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DOCVIEWER_SKIP_POSTGRES_TESTS") == "1")
        {
            Skip = "DOCVIEWER_SKIP_POSTGRES_TESTS=1 (skips every container test)";
        }
        else if (!PostgresFactAttribute.DockerLooksAvailable())
        {
            Skip = "Docker is not available; real-Redis tests need it (Testcontainers).";
        }
    }
}
