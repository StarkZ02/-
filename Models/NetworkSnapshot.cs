namespace MemoryMonitorBall.Models;

public sealed record NetworkSnapshot(
    bool IsOnline,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond);
