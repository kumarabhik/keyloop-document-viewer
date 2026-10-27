using System.Diagnostics.Metrics;
using DocViewer.Application.Observability;
using DocViewer.Application.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace DocViewer.Infrastructure.Tests.Observability;

/// <summary>A real <see cref="DocViewerMetrics"/> on its own meter factory, so tests can collect what it records.</summary>
internal sealed class TestMetrics : IDisposable
{
    private readonly ServiceProvider _provider = new ServiceCollection().AddMetrics().BuildServiceProvider();

    public TestMetrics(ISourceHealth? health = null) =>
        Metrics = new DocViewerMetrics(Factory, health ?? new NoHealth());

    public IMeterFactory Factory => _provider.GetRequiredService<IMeterFactory>();

    public DocViewerMetrics Metrics { get; }

    public MetricCollector<T> Collect<T>(string instrument) where T : struct =>
        new(Factory, DocViewerMetrics.MeterName, instrument);

    public void Dispose() => _provider.Dispose();

    private sealed class NoHealth : ISourceHealth
    {
        public IReadOnlyList<SourceHealth> GetHealth() => [];
    }
}
