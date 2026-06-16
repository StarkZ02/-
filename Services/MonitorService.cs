using MemoryMonitorBall.Models;

namespace MemoryMonitorBall.Services;

public sealed class MonitorService
{
    private readonly MemoryService _memoryService = new();
    private readonly NetworkService _networkService = new();
    private readonly ProcessService _processService = new();
    private readonly CleanupService _cleanupService = new();

    public MemorySnapshot GetMemorySnapshot() => _memoryService.GetSnapshot();

    public NetworkSnapshot GetNetworkSnapshot() => _networkService.GetSnapshot();

    public IReadOnlyList<ProcessInfo> GetTopMemoryProcesses() => _processService.GetTopMemoryProcesses();

    public CleanupResult ReleaseWorkingSets() => _cleanupService.ReleaseWorkingSets();

    public bool TryTerminate(ProcessInfo processInfo, out string message) =>
        _processService.TryTerminate(processInfo, out message);
}
