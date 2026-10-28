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

    // A cheap check that a Docker engine endpoint exists; Testcontainers reports anything subtler itself. Covers the
    // default socket, Docker Desktop for macOS without the default-socket option, rootless Docker and a Docker context
    // (D5.6 review). A stale socket with no daemon makes the tests fail rather than skip, which is the louder outcome.
    private static bool DockerLooksAvailable()
    {
        if (Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 }
            || Environment.GetEnvironmentVariable("DOCKER_CONTEXT") is { Length: > 0 })
        {
            return true;
        }

        if (OperatingSystem.IsWindows())
        {
            return File.Exists(@"\\.\pipe\docker_engine") || File.Exists(@"\\.\pipe\dockerDesktopLinuxEngine");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string?[] sockets =
        [
            "/var/run/docker.sock",
            Path.Combine(home, ".docker", "run", "docker.sock"),
            runtimeDir is null ? null : Path.Combine(runtimeDir, "docker.sock"),
        ];
        return sockets.Any(s => s is not null && File.Exists(s));
    }
}
