using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Api.Services;
using NSubstitute;

namespace Monitoring.Api.Tests.Services;

public sealed class SystemMetricsServiceTests
{
    // ── Sample node_exporter responses ──────────────────────────────────────

    private const string SampleMetrics1 = """
        # HELP node_cpu_seconds_total Seconds the CPUs spent in each mode.
        # TYPE node_cpu_seconds_total counter
        node_cpu_seconds_total{cpu="0",mode="idle"} 5000.0
        node_cpu_seconds_total{cpu="0",mode="system"} 300.0
        node_cpu_seconds_total{cpu="0",mode="user"} 700.0
        node_cpu_seconds_total{cpu="1",mode="idle"} 4800.0
        node_cpu_seconds_total{cpu="1",mode="system"} 400.0
        node_cpu_seconds_total{cpu="1",mode="user"} 800.0
        # HELP node_memory_MemTotal_bytes Memory information field MemTotal_bytes.
        # TYPE node_memory_MemTotal_bytes gauge
        node_memory_MemTotal_bytes 1.7179869184e+10
        # HELP node_memory_MemAvailable_bytes Memory information field MemAvailable_bytes.
        # TYPE node_memory_MemAvailable_bytes gauge
        node_memory_MemAvailable_bytes 8.589934592e+09
        """;

    // Second sample — idle increased by 90/100 per core ⇒ 10% CPU usage
    private const string SampleMetrics2 = """
        # HELP node_cpu_seconds_total Seconds the CPUs spent in each mode.
        # TYPE node_cpu_seconds_total counter
        node_cpu_seconds_total{cpu="0",mode="idle"} 5090.0
        node_cpu_seconds_total{cpu="0",mode="system"} 305.0
        node_cpu_seconds_total{cpu="0",mode="user"} 705.0
        node_cpu_seconds_total{cpu="1",mode="idle"} 4890.0
        node_cpu_seconds_total{cpu="1",mode="system"} 405.0
        node_cpu_seconds_total{cpu="1",mode="user"} 805.0
        # HELP node_memory_MemTotal_bytes Memory information field MemTotal_bytes.
        # TYPE node_memory_MemTotal_bytes gauge
        node_memory_MemTotal_bytes 1.7179869184e+10
        # HELP node_memory_MemAvailable_bytes Memory information field MemAvailable_bytes.
        # TYPE node_memory_MemAvailable_bytes gauge
        node_memory_MemAvailable_bytes 8.589934592e+09
        """;

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SystemMetricsService CreateService(params string[] responses)
    {
        int callIndex = 0;
        var handler = new FakeHandler(() =>
        {
            int idx = Math.Min(callIndex, responses.Length - 1);
            callIndex++;
            return responses[idx];
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://fake:9100") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monitoring:NodeExporterUrl"] = "http://fake:9100/metrics"
            })
            .Build();

        return new SystemMetricsService(factory, config, NullLogger<SystemMetricsService>.Instance);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<string> _responseFactory;
        public FakeHandler(Func<string> responseFactory) => _responseFactory = responseFactory;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_responseFactory())
            });
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_ReturnsNull_BeforeFirstSample()
    {
        var service = CreateService(SampleMetrics1);

        Assert.Null(service.GetSnapshot());
    }

    [Fact]
    public async Task GetSnapshot_ReturnsDto_AfterServiceRuns()
    {
        var service = CreateService(SampleMetrics1, SampleMetrics2);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        // Wait for two samples (service samples then waits 5s, so ~6s should be enough)
        await Task.Delay(TimeSpan.FromSeconds(7));

        var snapshot = service.GetSnapshot();
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.True(snapshot.CpuPercent >= 0);
        Assert.Equal(2, snapshot.CpuCoreCount);
        Assert.Equal("node_exporter", snapshot.Source);
    }

    [Fact]
    public async Task GetSnapshot_MemoryFields_AreCorrect()
    {
        var service = CreateService(SampleMetrics1, SampleMetrics2);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(7));

        var snapshot = service.GetSnapshot();
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(snapshot);
        // ~16 GB total
        Assert.Equal(17_179_869_184L, snapshot.MemoryTotalBytes);
        // ~8 GB available
        Assert.Equal(8_589_934_592L, snapshot.MemoryAvailableBytes);
        // Used = total - available
        Assert.Equal(
            snapshot.MemoryTotalBytes - snapshot.MemoryAvailableBytes,
            snapshot.MemoryUsedBytes);
        Assert.Contains("GB", snapshot.MemoryTotal);
        Assert.Contains("GB", snapshot.MemoryAvailable);
        Assert.Contains("% used", snapshot.MemoryUsage);
    }

    [Fact]
    public async Task GetSnapshot_CpuPercent_ComputedFromDelta()
    {
        var service = CreateService(SampleMetrics1, SampleMetrics2);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(7));

        var snapshot = service.GetSnapshot();
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        Assert.NotNull(snapshot);
        // Each core: idle increased by 90 out of 100 total ⇒ 10% busy
        Assert.Equal(10.0, snapshot.CpuPercent, precision: 1);
        Assert.Contains("cores", snapshot.Cpu);
    }

    [Fact]
    public async Task Service_StopsGracefully_WhenCancelled()
    {
        var service = CreateService(SampleMetrics1);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // No hanging or exceptions means the test passes
        Assert.True(true);
    }

    // ── Parser unit tests (no HTTP needed) ───────────────────────────────────

    [Fact]
    public void ParseNodeExporterMetrics_ExtractsCpuCores()
    {
        var data = SystemMetricsService.ParseNodeExporterMetrics(SampleMetrics1);

        Assert.Equal(2, data.CpuSeconds.Count);
        Assert.True(data.CpuSeconds.ContainsKey("0"));
        Assert.True(data.CpuSeconds.ContainsKey("1"));
        Assert.Equal(5000.0, data.CpuSeconds["0"]["idle"]);
        Assert.Equal(300.0, data.CpuSeconds["0"]["system"]);
    }

    [Fact]
    public void ParseNodeExporterMetrics_ExtractsMemory()
    {
        var data = SystemMetricsService.ParseNodeExporterMetrics(SampleMetrics1);

        Assert.Equal(17_179_869_184L, data.MemoryTotalBytes);
        Assert.Equal(8_589_934_592L, data.MemoryAvailableBytes);
    }

    [Fact]
    public void FormatBytes_FormatsCorrectly()
    {
        Assert.Equal("16.0 GB", SystemMetricsService.FormatBytes(17_179_869_184L));
        Assert.Equal("128.0 MB", SystemMetricsService.FormatBytes(134_217_728L));
        Assert.Equal("1.0 KB", SystemMetricsService.FormatBytes(1_024L));
        Assert.Equal("500 B", SystemMetricsService.FormatBytes(500L));
    }
}
