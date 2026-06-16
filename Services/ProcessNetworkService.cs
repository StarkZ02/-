using System.Runtime.InteropServices;
using MemoryMonitorBall.Models;

namespace MemoryMonitorBall.Services;

public sealed class ProcessNetworkService
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int ErrorInsufficientBuffer = 122;
    private const int TcpConnectionEstatsData = 1;

    private Dictionary<ConnectionKey, TcpByteSample> _previousSamples = [];
    private HashSet<ConnectionKey> _enabledConnections = [];
    private DateTimeOffset? _previousSampleTime;

    public IReadOnlyDictionary<int, ProcessNetworkUsage> GetNetworkUsageByProcess()
    {
        var now = DateTimeOffset.UtcNow;
        var seconds = _previousSampleTime.HasValue
            ? Math.Max((now - _previousSampleTime.Value).TotalSeconds, 0.001)
            : 0;
        var currentSamples = new Dictionary<ConnectionKey, TcpByteSample>();
        var currentEnabledConnections = new HashSet<ConnectionKey>();
        var builder = new Dictionary<int, NetworkUsageBuilder>();

        AddTcpRows(builder, currentSamples, currentEnabledConnections, seconds, AfInet);
        AddTcpRows(builder, currentSamples, currentEnabledConnections, seconds, AfInet6);
        AddUdpRows(builder, AfInet);
        AddUdpRows(builder, AfInet6);

        _previousSamples = currentSamples;
        _enabledConnections = currentEnabledConnections;
        _previousSampleTime = now;

        return builder.ToDictionary(
            item => item.Key,
            item => new ProcessNetworkUsage(
                item.Value.TcpConnectionCount,
                item.Value.EstablishedConnectionCount,
                item.Value.ListeningConnectionCount,
                item.Value.UdpEndpointCount,
                item.Value.DownloadBytesPerSecond,
                item.Value.UploadBytesPerSecond));
    }

    private void AddTcpRows(
        Dictionary<int, NetworkUsageBuilder> builder,
        Dictionary<ConnectionKey, TcpByteSample> currentSamples,
        HashSet<ConnectionKey> currentEnabledConnections,
        double seconds,
        int addressFamily)
    {
        var bufferSize = 0;
        var result = GetExtendedTcpTable(
            IntPtr.Zero,
            ref bufferSize,
            false,
            addressFamily,
            TcpTableClass.TcpTableOwnerPidAll,
            0);

        if (result != ErrorInsufficientBuffer || bufferSize <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedTcpTable(
                buffer,
                ref bufferSize,
                false,
                addressFamily,
                TcpTableClass.TcpTableOwnerPidAll,
                0);

            if (result != 0)
            {
                return;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(uint));
            var rowSize = addressFamily == AfInet
                ? Marshal.SizeOf<TcpRowOwnerPid>()
                : Marshal.SizeOf<Tcp6RowOwnerPid>();

            for (var index = 0; index < rowCount; index++)
            {
                if (addressFamily == AfInet)
                {
                    var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rowPtr);
                    AddTcpUsage(builder, currentSamples, currentEnabledConnections, seconds, row);
                }
                else
                {
                    var row = Marshal.PtrToStructure<Tcp6RowOwnerPid>(rowPtr);
                    AddTcpUsage(builder, currentSamples, currentEnabledConnections, seconds, row);
                }

                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void AddUdpRows(Dictionary<int, NetworkUsageBuilder> builder, int addressFamily)
    {
        var bufferSize = 0;
        var result = GetExtendedUdpTable(
            IntPtr.Zero,
            ref bufferSize,
            false,
            addressFamily,
            UdpTableClass.UdpTableOwnerPid,
            0);

        if (result != ErrorInsufficientBuffer || bufferSize <= 0)
        {
            return;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedUdpTable(
                buffer,
                ref bufferSize,
                false,
                addressFamily,
                UdpTableClass.UdpTableOwnerPid,
                0);

            if (result != 0)
            {
                return;
            }

            var rowCount = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, sizeof(uint));
            var rowSize = addressFamily == AfInet
                ? Marshal.SizeOf<UdpRowOwnerPid>()
                : Marshal.SizeOf<Udp6RowOwnerPid>();

            for (var index = 0; index < rowCount; index++)
            {
                var processId = addressFamily == AfInet
                    ? (int)Marshal.PtrToStructure<UdpRowOwnerPid>(rowPtr).OwningPid
                    : (int)Marshal.PtrToStructure<Udp6RowOwnerPid>(rowPtr).OwningPid;
                var usage = GetOrCreate(builder, processId);
                usage.UdpEndpointCount++;
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void AddTcpUsage(
        Dictionary<int, NetworkUsageBuilder> builder,
        Dictionary<ConnectionKey, TcpByteSample> currentSamples,
        HashSet<ConnectionKey> currentEnabledConnections,
        double seconds,
        TcpRowOwnerPid row)
    {
        var processId = (int)row.OwningPid;
        var usage = GetOrCreate(builder, processId);
        AddTcpConnectionState(usage, row.State);

        if (row.State != TcpConnectionState.Established)
        {
            return;
        }

        var key = ConnectionKey.From(row);
        if (!_enabledConnections.Contains(key) && !TryEnableTcpStats(ref row))
        {
            return;
        }

        currentEnabledConnections.Add(key);
        if (!TryGetTcpStats(ref row, out var sample))
        {
            return;
        }

        AddSpeedSample(usage, currentSamples, key, sample, seconds);
    }

    private void AddTcpUsage(
        Dictionary<int, NetworkUsageBuilder> builder,
        Dictionary<ConnectionKey, TcpByteSample> currentSamples,
        HashSet<ConnectionKey> currentEnabledConnections,
        double seconds,
        Tcp6RowOwnerPid row)
    {
        var processId = (int)row.OwningPid;
        var usage = GetOrCreate(builder, processId);
        AddTcpConnectionState(usage, row.State);

        if (row.State != TcpConnectionState.Established)
        {
            return;
        }

        var key = ConnectionKey.From(row);
        if (!_enabledConnections.Contains(key) && !TryEnableTcpStats(ref row))
        {
            return;
        }

        currentEnabledConnections.Add(key);
        if (!TryGetTcpStats(ref row, out var sample))
        {
            return;
        }

        AddSpeedSample(usage, currentSamples, key, sample, seconds);
    }

    private void AddSpeedSample(
        NetworkUsageBuilder usage,
        Dictionary<ConnectionKey, TcpByteSample> currentSamples,
        ConnectionKey key,
        TcpByteSample sample,
        double seconds)
    {
        currentSamples[key] = sample;

        if (seconds <= 0 || !_previousSamples.TryGetValue(key, out var previous))
        {
            return;
        }

        usage.DownloadBytesPerSecond += SafeDelta(sample.DownloadBytesIn, previous.DownloadBytesIn) / seconds;
        usage.UploadBytesPerSecond += SafeDelta(sample.UploadBytesOut, previous.UploadBytesOut) / seconds;
    }

    private static void AddTcpConnectionState(NetworkUsageBuilder usage, TcpConnectionState state)
    {
        usage.TcpConnectionCount++;

        if (state == TcpConnectionState.Established)
        {
            usage.EstablishedConnectionCount++;
        }
        else if (state == TcpConnectionState.Listen)
        {
            usage.ListeningConnectionCount++;
        }
    }

    private static ulong SafeDelta(ulong current, ulong previous) => current >= previous ? current - previous : 0;

    private static bool TryEnableTcpStats(ref TcpRowOwnerPid row)
    {
        var rw = new TcpEstatsDataRwV0 { EnableCollection = 1 };
        return SetPerTcpConnectionEStats(
            ref row,
            TcpConnectionEstatsData,
            ref rw,
            0,
            (uint)Marshal.SizeOf<TcpEstatsDataRwV0>(),
            0) == 0;
    }

    private static bool TryEnableTcpStats(ref Tcp6RowOwnerPid row)
    {
        var rw = new TcpEstatsDataRwV0 { EnableCollection = 1 };
        return SetPerTcp6ConnectionEStats(
            ref row,
            TcpConnectionEstatsData,
            ref rw,
            0,
            (uint)Marshal.SizeOf<TcpEstatsDataRwV0>(),
            0) == 0;
    }

    private static bool TryGetTcpStats(ref TcpRowOwnerPid row, out TcpByteSample sample)
    {
        var result = GetPerTcpConnectionEStats(
            ref row,
            TcpConnectionEstatsData,
            IntPtr.Zero,
            0,
            0,
            IntPtr.Zero,
            0,
            0,
            out var rod,
            0,
            (uint)Marshal.SizeOf<TcpEstatsDataRodV0>());

        sample = new TcpByteSample(rod.DataBytesIn, rod.DataBytesOut);
        return result == 0;
    }

    private static bool TryGetTcpStats(ref Tcp6RowOwnerPid row, out TcpByteSample sample)
    {
        var result = GetPerTcp6ConnectionEStats(
            ref row,
            TcpConnectionEstatsData,
            IntPtr.Zero,
            0,
            0,
            IntPtr.Zero,
            0,
            0,
            out var rod,
            0,
            (uint)Marshal.SizeOf<TcpEstatsDataRodV0>());

        sample = new TcpByteSample(rod.DataBytesIn, rod.DataBytesOut);
        return result == 0;
    }

    private static NetworkUsageBuilder GetOrCreate(Dictionary<int, NetworkUsageBuilder> builder, int processId)
    {
        if (!builder.TryGetValue(processId, out var usage))
        {
            usage = new NetworkUsageBuilder();
            builder[processId] = usage;
        }

        return usage;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        TcpTableClass tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedUdpTable(
        IntPtr udpTable,
        ref int udpTableLength,
        bool sort,
        int ipVersion,
        UdpTableClass tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int SetPerTcpConnectionEStats(
        ref TcpRowOwnerPid row,
        int estatsType,
        ref TcpEstatsDataRwV0 rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int SetPerTcp6ConnectionEStats(
        ref Tcp6RowOwnerPid row,
        int estatsType,
        ref TcpEstatsDataRwV0 rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetPerTcpConnectionEStats(
        ref TcpRowOwnerPid row,
        int estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        out TcpEstatsDataRodV0 rod,
        uint rodVersion,
        uint rodSize);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetPerTcp6ConnectionEStats(
        ref Tcp6RowOwnerPid row,
        int estatsType,
        IntPtr rw,
        uint rwVersion,
        uint rwSize,
        IntPtr ros,
        uint rosVersion,
        uint rosSize,
        out TcpEstatsDataRodV0 rod,
        uint rodVersion,
        uint rodSize);

    private sealed class NetworkUsageBuilder
    {
        public int TcpConnectionCount { get; set; }

        public int EstablishedConnectionCount { get; set; }

        public int ListeningConnectionCount { get; set; }

        public int UdpEndpointCount { get; set; }

        public double DownloadBytesPerSecond { get; set; }

        public double UploadBytesPerSecond { get; set; }
    }

    private readonly record struct TcpByteSample(ulong DownloadBytesIn, ulong UploadBytesOut);

    private readonly record struct ConnectionKey(
        int AddressFamily,
        uint LocalAddr,
        string? LocalAddr6,
        uint LocalScopeId,
        uint LocalPort,
        uint RemoteAddr,
        string? RemoteAddr6,
        uint RemoteScopeId,
        uint RemotePort,
        uint ProcessId)
    {
        public static ConnectionKey From(TcpRowOwnerPid row) =>
            new(AfInet, row.LocalAddr, null, 0, row.LocalPort, row.RemoteAddr, null, 0, row.RemotePort, row.OwningPid);

        public static ConnectionKey From(Tcp6RowOwnerPid row) =>
            new(
                AfInet6,
                0,
                Convert.ToHexString(row.LocalAddr),
                row.LocalScopeId,
                row.LocalPort,
                0,
                Convert.ToHexString(row.RemoteAddr),
                row.RemoteScopeId,
                row.RemotePort,
                row.OwningPid);
    }

    private enum TcpTableClass
    {
        TcpTableOwnerPidAll = 5
    }

    private enum UdpTableClass
    {
        UdpTableOwnerPid = 1
    }

    private enum TcpConnectionState : uint
    {
        Listen = 2,
        Established = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpEstatsDataRwV0
    {
        public byte EnableCollection;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpEstatsDataRodV0
    {
        public ulong DataBytesOut;
        public ulong DataSegsOut;
        public ulong DataBytesIn;
        public ulong DataSegsIn;
        public ulong SegsOut;
        public ulong SegsIn;
        public uint SoftErrors;
        public uint SoftErrorReason;
        public uint SndUna;
        public uint SndNxt;
        public uint SndMax;
        public ulong ThruBytesAcked;
        public uint RcvNxt;
        public ulong ThruBytesReceived;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public TcpConnectionState State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;

        public uint RemoteScopeId;
        public uint RemotePort;
        public TcpConnectionState State;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRowOwnerPid
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Udp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;

        public uint LocalScopeId;
        public uint LocalPort;
        public uint OwningPid;
    }
}
