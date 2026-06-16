using System.Runtime.InteropServices;
using MemoryMonitorBall.Models;

namespace MemoryMonitorBall.Services;

public sealed class MemoryService
{
    public MemorySnapshot GetSnapshot()
    {
        var status = new MemoryStatusEx();
        status.Length = (uint)Marshal.SizeOf<MemoryStatusEx>();

        if (!GlobalMemoryStatusEx(ref status))
        {
            throw new InvalidOperationException("无法读取系统内存状态。");
        }

        var usedBytes = status.TotalPhys - status.AvailPhys;
        var usedPercent = status.TotalPhys == 0 ? 0 : usedBytes * 100d / status.TotalPhys;

        return new MemorySnapshot(status.TotalPhys, status.AvailPhys, usedBytes, usedPercent);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
