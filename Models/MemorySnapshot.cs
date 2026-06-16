namespace MemoryMonitorBall.Models;

public sealed record MemorySnapshot(
    ulong TotalBytes,
    ulong AvailableBytes,
    ulong UsedBytes,
    double UsedPercent);
