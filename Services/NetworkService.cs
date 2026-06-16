using System.Net.NetworkInformation;
using MemoryMonitorBall.Models;

namespace MemoryMonitorBall.Services;

public sealed class NetworkService
{
    private long? _lastReceivedBytes;
    private long? _lastSentBytes;
    private DateTimeOffset? _lastSampleTime;

    public NetworkSnapshot GetSnapshot()
    {
        var counters = GetCounters();
        var now = DateTimeOffset.UtcNow;
        var isOnline = counters.OnlineInterfaceCount > 0 && NetworkInterface.GetIsNetworkAvailable();

        double downloadRate = 0;
        double uploadRate = 0;

        if (_lastReceivedBytes.HasValue && _lastSentBytes.HasValue && _lastSampleTime.HasValue)
        {
            var seconds = Math.Max((now - _lastSampleTime.Value).TotalSeconds, 0.001);
            var receivedDelta = Math.Max(counters.ReceivedBytes - _lastReceivedBytes.Value, 0);
            var sentDelta = Math.Max(counters.SentBytes - _lastSentBytes.Value, 0);

            downloadRate = receivedDelta / seconds;
            uploadRate = sentDelta / seconds;
        }

        _lastReceivedBytes = counters.ReceivedBytes;
        _lastSentBytes = counters.SentBytes;
        _lastSampleTime = now;

        return new NetworkSnapshot(isOnline, downloadRate, uploadRate);
    }

    private static NetworkCounters GetCounters()
    {
        long receivedBytes = 0;
        long sentBytes = 0;
        var onlineInterfaces = 0;

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            try
            {
                var statistics = networkInterface.GetIPv4Statistics();
                receivedBytes += statistics.BytesReceived;
                sentBytes += statistics.BytesSent;
                onlineInterfaces++;
            }
            catch
            {
                // Some virtual adapters can throw while being queried. Ignore them and keep the UI alive.
            }
        }

        return new NetworkCounters(receivedBytes, sentBytes, onlineInterfaces);
    }

    private sealed record NetworkCounters(long ReceivedBytes, long SentBytes, int OnlineInterfaceCount);
}
