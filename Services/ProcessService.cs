using System.Diagnostics;
using MemoryMonitorBall.Models;

namespace MemoryMonitorBall.Services;

public sealed class ProcessService
{
    private readonly ProcessNetworkService _processNetworkService = new();

    private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "Idle",
        "Registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "svchost",
        "winlogon",
        "dwm",
        "fontdrvhost"
    };

    public IReadOnlyList<ProcessInfo> GetTopMemoryProcesses(int limit = 30)
    {
        var currentProcessId = Environment.ProcessId;
        using var currentProcess = Process.GetCurrentProcess();
        var currentSessionId = currentProcess.SessionId;
        var networkUsageByProcess = _processNetworkService.GetNetworkUsageByProcess();
        var processes = new List<ProcessInfo>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.HasExited)
                    {
                        continue;
                    }

                    var name = process.ProcessName;
                    var networkUsage = networkUsageByProcess.TryGetValue(process.Id, out var usage)
                        ? usage
                        : new ProcessNetworkUsage(0, 0, 0, 0, 0, 0);
                    var canTerminate = process.Id != currentProcessId
                        && !ProtectedProcessNames.Contains(name)
                        && process.SessionId == currentSessionId;

                    processes.Add(new ProcessInfo
                    {
                        ProcessId = process.Id,
                        Name = name,
                        WorkingSetBytes = process.WorkingSet64,
                        StatusText = GetStatusText(process, networkUsage),
                        NetworkUsage = networkUsage,
                        CanTerminate = canTerminate
                    });
                }
                catch
                {
                    // Processes can exit or reject access during enumeration.
                }
            }
        }

        return processes
            .OrderByDescending(process => process.NetworkUsage.TotalBytesPerSecond)
            .ThenByDescending(process => process.NetworkUsage.TotalConnectionCount)
            .ThenByDescending(process => process.WorkingSetBytes)
            .Take(limit)
            .ToList();
    }

    private static string GetStatusText(Process process, ProcessNetworkUsage networkUsage)
    {
        try
        {
            if (process.HasExited)
            {
                return "\u5df2\u9000\u51fa";
            }

            if (process.MainWindowHandle != IntPtr.Zero && !process.Responding)
            {
                return "\u672a\u54cd\u5e94";
            }

            return networkUsage.TotalConnectionCount > 0
                ? "\u8fd0\u884c\u4e2d / \u8054\u7f51"
                : "\u8fd0\u884c\u4e2d";
        }
        catch
        {
            return "\u53d7\u9650";
        }
    }

    public bool TryTerminate(ProcessInfo processInfo, out string message)
    {
        if (!processInfo.CanTerminate)
        {
            message = "该进程受保护或当前无权结束。";
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processInfo.ProcessId);
            process.Kill(entireProcessTree: false);
            message = $"已结束 {processInfo.DisplayName}。";
            return true;
        }
        catch (Exception ex)
        {
            message = $"结束进程失败：{ex.Message}";
            return false;
        }
    }
}
