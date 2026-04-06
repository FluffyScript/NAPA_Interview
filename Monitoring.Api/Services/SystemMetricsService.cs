using System.Globalization;
using Monitoring.Api.Models;
using Prometheus;

namespace Monitoring.Api.Services;

/// <summary>
/// Singleton background service that polls a node_exporter instance every 5 seconds
/// to collect CPU and memory metrics for the database host.
///
/// CPU:    Computed from delta of node_cpu_seconds_total counters over a 1-minute
///         sliding window (12 samples at 5 s each). This matches Grafana's
///         rate(node_cpu_seconds_total[1m]) so both surfaces show the same number.
///         Usage% = (1 − idle_delta / total_delta) × 100.
///
/// Memory: node_memory_MemTotal_bytes and node_memory_MemAvailable_bytes.
///
/// The latest sample is stored in <see cref="GetSnapshot"/> for the /system HTTP endpoint.
/// Prometheus gauges are updated on each sample so Prometheus scrapes are always current.
/// </summary>
public sealed class SystemMetricsService : BackgroundService
{
    // --- Prometheus instruments (static — survive DI scope changes) ---

    private static readonly Gauge DbHostCpuPercent = Metrics.CreateGauge(
        Constants.Metrics.Names.DbHostCpuPercent,
        Constants.Metrics.Descriptions.DbHostCpuPercent);

    private static readonly Gauge DbHostMemoryTotalBytes = Metrics.CreateGauge(
        Constants.Metrics.Names.DbHostMemoryTotalBytes,
        Constants.Metrics.Descriptions.DbHostMemoryTotalBytes);

    private static readonly Gauge DbHostMemoryAvailableBytes = Metrics.CreateGauge(
        Constants.Metrics.Names.DbHostMemoryAvailableBytes,
        Constants.Metrics.Descriptions.DbHostMemoryAvailableBytes);

    // ---

    /// <summary>
    /// The CPU sliding window holds this many samples. At a 5-second poll interval
    /// 12 samples ≈ 60 seconds, matching Grafana's <c>rate(...[1m])</c>.
    /// </summary>
    internal const int CpuWindowSize = 12;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _nodeExporterUrl;
    private readonly ILogger<SystemMetricsService> _logger;

    // Sliding window of CPU counter snapshots — oldest at the front, newest at the back.
    // CPU % is always computed as the delta between the oldest and newest entries,
    // giving a ~60-second rolling average that aligns with Grafana.
    private readonly Queue<Dictionary<string, Dictionary<string, double>>> _cpuWindow = new();

    // Latest snapshot — written by background loop, read by HTTP handler
    private volatile SystemMetricsSnapshot? _snapshot;

    public SystemMetricsService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<SystemMetricsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _nodeExporterUrl = configuration[Constants.Config.NodeExporterUrl]
            ?? "http://localhost:9100/metrics";
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SystemMetricsService started. node_exporter URL: {Url}", _nodeExporterUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SampleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "node_exporter sample failed; will retry next interval.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("SystemMetricsService stopped.");
    }

    /// <summary>Returns the most recent sample, or <c>null</c> if none has been taken yet.</summary>
    public SystemMetricsDto? GetSnapshot()
    {
        var s = _snapshot;
        if (s is null) return null;

        long usedBytes = s.MemoryTotalBytes - s.MemoryAvailableBytes;
        double usagePct = s.MemoryTotalBytes > 0
            ? (double)usedBytes / s.MemoryTotalBytes * 100.0
            : 0.0;

        return new SystemMetricsDto(
            CpuPercent:          s.CpuPercent,
            Cpu:                 $"{s.CpuPercent:F1}%  ({s.CpuCoreCount} cores)",
            CpuCoreCount:        s.CpuCoreCount,
            MemoryTotalBytes:    s.MemoryTotalBytes,
            MemoryTotal:         FormatBytes(s.MemoryTotalBytes),
            MemoryAvailableBytes: s.MemoryAvailableBytes,
            MemoryAvailable:     FormatBytes(s.MemoryAvailableBytes),
            MemoryUsedBytes:     usedBytes,
            MemoryUsed:          FormatBytes(usedBytes),
            MemoryUsage:         $"{usagePct:F1}% used",
            Source:              "node_exporter",
            Timestamp:           s.Timestamp
        );
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024L)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task SampleAsync(CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var response = await client.GetStringAsync(_nodeExporterUrl, ct);

        var parsed = ParseNodeExporterMetrics(response);

        // --- CPU (sliding window) ---
        _cpuWindow.Enqueue(parsed.CpuSeconds);
        while (_cpuWindow.Count > CpuWindowSize)
            _cpuWindow.Dequeue();

        double cpuPercent = ComputeCpuPercent();
        int coreCount = parsed.CpuSeconds.Count;

        // --- Memory ---
        long memTotal = parsed.MemoryTotalBytes;
        long memAvailable = parsed.MemoryAvailableBytes;

        // --- Update Prometheus ---
        DbHostCpuPercent.Set(cpuPercent);
        if (memTotal > 0)     DbHostMemoryTotalBytes.Set(memTotal);
        if (memAvailable > 0) DbHostMemoryAvailableBytes.Set(memAvailable);

        _snapshot = new SystemMetricsSnapshot(cpuPercent, coreCount, memTotal, memAvailable, DateTimeOffset.UtcNow);

        _logger.LogDebug(
            "node_exporter sample — cpu={Cpu:F1}%, cores={Cores}, mem_total={Total}B, mem_avail={Avail}B",
            cpuPercent, coreCount, memTotal, memAvailable);
    }

    /// <summary>
    /// Computes overall CPU usage % from the delta between the oldest and newest
    /// entries in the sliding window (up to ~60 seconds apart).
    /// This matches Grafana's <c>rate(node_cpu_seconds_total{mode="idle"}[1m])</c>.
    /// Returns 0 when the window contains fewer than 2 samples.
    /// </summary>
    private double ComputeCpuPercent()
    {
        if (_cpuWindow.Count < 2)
            return 0.0;

        var oldest = _cpuWindow.Peek();
        var newest = _cpuWindow.Last();

        double totalDelta = 0;
        double idleDelta = 0;

        foreach (var (cpu, modes) in newest)
        {
            if (!oldest.TryGetValue(cpu, out var oldModes))
                continue;

            foreach (var (mode, value) in modes)
            {
                double prev = oldModes.GetValueOrDefault(mode, 0);
                double delta = value - prev;
                totalDelta += delta;
                if (mode == "idle")
                    idleDelta += delta;
            }
        }

        if (totalDelta <= 0) return 0.0;

        return (1.0 - idleDelta / totalDelta) * 100.0;
    }

    // ── Prometheus text format parser (lightweight, no library needed) ─────

    internal static NodeExporterData ParseNodeExporterMetrics(string metricsText)
    {
        // cpu label → (mode → cumulative seconds)
        var cpuSeconds = new Dictionary<string, Dictionary<string, double>>();
        long memTotal = 0;
        long memAvailable = 0;

        foreach (var line in metricsText.AsSpan().EnumerateLines())
        {
            if (line.StartsWith("#") || line.IsWhiteSpace())
                continue;

            if (line.StartsWith("node_cpu_seconds_total{"))
            {
                ParseCpuLine(line, cpuSeconds);
            }
            else if (line.StartsWith("node_memory_MemTotal_bytes "))
            {
                memTotal = ParseGaugeValueAsLong(line);
            }
            else if (line.StartsWith("node_memory_MemAvailable_bytes "))
            {
                memAvailable = ParseGaugeValueAsLong(line);
            }
        }

        return new NodeExporterData(cpuSeconds, memTotal, memAvailable);
    }

    /// <summary>
    /// Parses a line like:
    ///   node_cpu_seconds_total{cpu="0",mode="idle"} 5000.0
    /// </summary>
    private static void ParseCpuLine(ReadOnlySpan<char> line, Dictionary<string, Dictionary<string, double>> cpuSeconds)
    {
        // Extract labels between { and }
        int braceOpen = line.IndexOf('{');
        int braceClose = line.IndexOf('}');
        if (braceOpen < 0 || braceClose < 0) return;

        var labels = line[(braceOpen + 1)..braceClose];
        string? cpu = ExtractLabel(labels, "cpu");
        string? mode = ExtractLabel(labels, "mode");
        if (cpu is null || mode is null) return;

        // Parse the value after the closing brace + space
        var valuePart = line[(braceClose + 1)..].Trim();
        if (!double.TryParse(valuePart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return;

        if (!cpuSeconds.TryGetValue(cpu, out var modes))
        {
            modes = new Dictionary<string, double>();
            cpuSeconds[cpu] = modes;
        }
        modes[mode] = value;
    }

    /// <summary>
    /// Extracts a label value from a Prometheus label set.
    /// e.g. ExtractLabel("cpu=\"0\",mode=\"idle\"", "mode") → "idle"
    /// </summary>
    private static string? ExtractLabel(ReadOnlySpan<char> labels, string key)
    {
        var searchKey = $"{key}=\"";
        int start = labels.IndexOf(searchKey.AsSpan());
        if (start < 0) return null;

        start += searchKey.Length;
        var rest = labels[start..];
        int end = rest.IndexOf('"');
        if (end < 0) return null;

        return rest[..end].ToString();
    }

    /// <summary>
    /// Parses a gauge line like:
    ///   node_memory_MemTotal_bytes 1.7179869184e+10
    /// Returns the value as a long (bytes).
    /// </summary>
    private static long ParseGaugeValueAsLong(ReadOnlySpan<char> line)
    {
        int space = line.LastIndexOf(' ');
        if (space < 0) return 0;

        var valuePart = line[(space + 1)..].Trim();
        if (!double.TryParse(valuePart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return 0;

        return (long)value;
    }
}

/// <summary>Internal snapshot — avoids re-boxing the volatile field on every read.</summary>
internal sealed record SystemMetricsSnapshot(
    double CpuPercent,
    int    CpuCoreCount,
    long   MemoryTotalBytes,
    long   MemoryAvailableBytes,
    DateTimeOffset Timestamp
);

/// <summary>Parsed result from a single node_exporter /metrics scrape.</summary>
internal sealed record NodeExporterData(
    Dictionary<string, Dictionary<string, double>> CpuSeconds,
    long MemoryTotalBytes,
    long MemoryAvailableBytes
);
