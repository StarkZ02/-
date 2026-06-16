namespace MemoryMonitorBall.Models;

public sealed class ProcessInfo
{
    public required int ProcessId { get; init; }

    public required string Name { get; init; }

    public required long WorkingSetBytes { get; init; }

    public required string StatusText { get; init; }

    public required ProcessNetworkUsage NetworkUsage { get; init; }

    public required bool CanTerminate { get; init; }

    public string MemoryText => FormatBytes((ulong)Math.Max(WorkingSetBytes, 0));

    public string NetworkUsageText => NetworkUsage.UsageText;

    public string DisplayName => $"{Name} ({ProcessId})";

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
