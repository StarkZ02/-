namespace MemoryMonitorBall.Models;

public sealed record ProcessNetworkUsage(
    int TcpConnectionCount,
    int EstablishedConnectionCount,
    int ListeningConnectionCount,
    int UdpEndpointCount,
    double DownloadBytesPerSecond,
    double UploadBytesPerSecond)
{
    public int TotalConnectionCount => TcpConnectionCount + UdpEndpointCount;

    public string UsageText
    {
        get
        {
            if (TotalConnectionCount == 0 && DownloadBytesPerSecond <= 0 && UploadBytesPerSecond <= 0)
            {
                return "\u672a\u8054\u7f51";
            }

            return $"\u2193 {FormatRate(DownloadBytesPerSecond)}  \u2191 {FormatRate(UploadBytesPerSecond)}";
        }
    }

    public double TotalBytesPerSecond => DownloadBytesPerSecond + UploadBytesPerSecond;

    private static string FormatRate(double bytesPerSecond)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var value = Math.Max(bytesPerSecond, 0);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}
