namespace DocViewer.Infrastructure.Tests.Persistence.Postgres;

/// <summary>
/// A test that needs a real PostgreSQL container (D5.5). Skipped, with the reason shown, when Docker is not
/// reachable or <c>DOCVIEWER_SKIP_POSTGRES_TESTS=1</c>, so <c>dotnet test</c> still works on a machine without Docker.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DOCVIEWER_SKIP_POSTGRES_TESTS") == "1")
        {
            Skip = "DOCVIEWER_SKIP_POSTGRES_TESTS=1";
        }
        else if (!DockerLooksAvailable())
        {
            Skip = "Docker is not available; real-PostgreSQL tests need it (Testcontainers).";
        }
    }

    // A cheap check that the Docker engine endpoint exists; Testcontainers reports anything subtler itself.
    private static bool DockerLooksAvailable() =>
        Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 }
        || (OperatingSystem.IsWindows() ? File.Exists(@"\\.\pipe\docker_engine") : File.Exists("/var/run/docker.sock"));
}
