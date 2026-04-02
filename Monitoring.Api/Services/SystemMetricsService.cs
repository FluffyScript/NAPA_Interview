using Monitoring.Api.Models;
using Prometheus;
using System.Runtime.InteropServices;

namespace Monitoring.Api.Services;

/// <summary>
/// Singleton background service that samples process CPU and memory every 5 seconds.
///
/// CPU:    delta of Process.TotalProcessorTime / (wall-clock delta × ProcessorCount).
///         Cross-platform — works identically on Linux and Windows.
///
/// Memory: Process.WorkingSet64 for process RSS (cross-platform).
///         System total and available RAM:
///           Linux   → /proc/meminfo (MemTotal / MemAvailable).
///           Windows → GCMemoryInfo.TotalAvailableMemoryBytes for total;
///                     available is not exposed without P/Invoke so it is reported as null.
///
/// The latest sample is stored in <see cref="GetSnapshot"/> for the /system HTTP endpoint.
/// Prometheus gauges are updated on each sample so Prometheus scrapes are always current.
/// </summary>
public sealed class SystemMetricsService : BackgroundService
{
    // --- Prometheus instruments (static — survive DI scope changes) ---

    private static readonly Gauge ProcessCpuPercent = Metrics.CreateGauge(
        Constants.Metrics.Names.ProcessCpuPercent,
        Constants.Metrics.Descriptions.ProcessCpuPercent);

    private static readonly Gauge ProcessMemoryBytes = Metrics.CreateGauge(
        Constants.Metrics.Names.ProcessMemoryBytes,
        Constants.Metrics.Descriptions.ProcessMemoryBytes);

    private static readonly Gauge SystemMemoryTotalBytes = Metrics.CreateGauge(
        Constants.Metrics.Names.SystemMemoryTotalBytes,
        Constants.Metrics.Descriptions.SystemMemoryTotalBytes);

    private static readonly Gauge SystemMemoryAvailableBytes = Metrics.CreateGauge(
        Constants.Metrics.Names.SystemMemoryAvailableBytes,
        Constants.Metrics.Descriptions.SystemMemoryAvailableBytes);

    // ---

    private readonly ILogger<SystemMetricsService> _logger;
    private readonly bool _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    private readonly string _platform = RuntimeInformation.OSDescription;

    // CPU delta state
    private TimeSpan _lastCpuTime = TimeSpan.Zero;
    private DateTime _lastSampleTime = DateTime.MinValue;

    // Latest snapshot — written by background loop, read by HTTP handler
    private volatile SystemMetricsSnapshot? _snapshot;

    public SystemMetricsService(ILogger<SystemMetricsService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Establish a CPU baseline before the first sample
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        _lastCpuTime = proc.TotalProcessorTime;
        _lastSampleTime = DateTime.UtcNow;

        _logger.LogInformation(
            "SystemMetricsService started. Platform: {Platform}, IsLinux: {IsLinux}",
            _platform, _isLinux);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

            try
            {
                Sample();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "System metrics sample failed; will retry next interval.");
            }
        }

        _logger.LogInformation("SystemMetricsService stopped.");
    }

    /// <summary>Returns the most recent sample, or <c>null</c> if none has been taken yet.</summary>
    public SystemMetricsDto? GetSnapshot()
    {
        var s = _snapshot;
        if (s is null) return null;

        long? usedBytes       = s.SystemMemoryTotalBytes > 0 && s.SystemMemoryAvailableBytes.HasValue
            ? s.SystemMemoryTotalBytes - s.SystemMemoryAvailableBytes.Value
            : null;
        double? usagePct      = usedBytes.HasValue && s.SystemMemoryTotalBytes > 0
            ? (double)usedBytes.Value / s.SystemMemoryTotalBytes * 100.0
            : null;

        return new SystemMetricsDto(
            ProcessCpuPercent:          s.CpuPercent,
            ProcessCpu:                 $"{s.CpuPercent:F1}%  ({Environment.ProcessorCount} logical cores)",
            ProcessMemoryBytes:         s.ProcessMemoryBytes,
            ProcessMemory:              FormatBytes(s.ProcessMemoryBytes),
            SystemMemoryTotalBytes:     s.SystemMemoryTotalBytes,
            SystemMemoryTotal:          s.SystemMemoryTotalBytes > 0 ? FormatBytes(s.SystemMemoryTotalBytes) : "N/A",
            SystemMemoryAvailableBytes: s.SystemMemoryAvailableBytes,
            SystemMemoryAvailable:      s.SystemMemoryAvailableBytes.HasValue ? FormatBytes(s.SystemMemoryAvailableBytes.Value) : null,
            SystemMemoryUsedBytes:      usedBytes,
            SystemMemoryUsed:           usedBytes.HasValue ? FormatBytes(usedBytes.Value) : null,
            SystemMemoryUsage:          usagePct.HasValue ? $"{usagePct.Value:F1}% used" : null,
            ProcessorCount:             Environment.ProcessorCount,
            Platform:                   _platform,
            Timestamp:                  s.Timestamp
        );
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824L) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576L)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024L)         return $"{bytes / 1_024.0:F1} KB";
        return $"{bytes} B";
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void Sample()
    {
        var proc = System.Diagnostics.Process.GetCurrentProcess();
        proc.Refresh();

        // --- CPU ---
        var now     = DateTime.UtcNow;
        var cpuUsed = proc.TotalProcessorTime - _lastCpuTime;
        var elapsed = now - _lastSampleTime;

        var cpuPercent = elapsed.TotalMilliseconds > 0
            ? cpuUsed.TotalMilliseconds / (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100.0
            : 0.0;

        _lastCpuTime    = proc.TotalProcessorTime;
        _lastSampleTime = now;

        // --- Process memory ---
        long processRss = proc.WorkingSet64;

        // --- System memory ---
        long sysTotal     = 0;
        long? sysAvailable = null;

        if (_isLinux)
        {
            (sysTotal, sysAvailable) = ReadProcMeminfo();
        }
        else
        {
            // GCMemoryInfo.TotalAvailableMemoryBytes is the installed physical RAM
            // (or the container memory limit if running in one).
            var gcInfo = GC.GetGCMemoryInfo();
            sysTotal = gcInfo.TotalAvailableMemoryBytes;
            // Available RAM on Windows requires P/Invoke (GlobalMemoryStatusEx).
            // We skip that dependency; the field will be null/absent in the response.
        }

        // --- Update Prometheus ---
        ProcessCpuPercent.Set(cpuPercent);
        ProcessMemoryBytes.Set(processRss);
        if (sysTotal > 0)     SystemMemoryTotalBytes.Set(sysTotal);
        if (sysAvailable > 0) SystemMemoryAvailableBytes.Set(sysAvailable.Value);

        _snapshot = new SystemMetricsSnapshot(cpuPercent, processRss, sysTotal, sysAvailable, now);

        _logger.LogDebug(
            "System sample — cpu={Cpu:F1}%, rss={Rss}B, sys_total={Total}B",
            cpuPercent, processRss, sysTotal);
    }

    /// <summary>Reads MemTotal and MemAvailable from /proc/meminfo (Linux only).</summary>
    private static (long total, long available) ReadProcMeminfo()
    {
        long total = 0, available = 0;

        foreach (var line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                total = ParseProcMeminfoKb(line) * 1024;
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                available = ParseProcMeminfoKb(line) * 1024;

            if (total > 0 && available > 0) break;
        }

        return (total, available);
    }

    // Parses lines like "MemTotal:       16386052 kB" → 16386052
    private static long ParseProcMeminfoKb(string line)
    {
        var span = line.AsSpan();
        var colon = span.IndexOf(':');
        if (colon < 0) return 0;

        span = span[(colon + 1)..].Trim();
        var space = span.IndexOf(' ');
        var digits = space >= 0 ? span[..space] : span;

        return long.TryParse(digits, out var val) ? val : 0;
    }
}

/// <summary>Internal snapshot — avoids re-boxing the volatile field on every read.</summary>
internal sealed record SystemMetricsSnapshot(
    double CpuPercent,
    long   ProcessMemoryBytes,
    long   SystemMemoryTotalBytes,
    long?  SystemMemoryAvailableBytes,
    DateTime Timestamp
);
