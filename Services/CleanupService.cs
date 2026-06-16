using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MemoryMonitorBall.Services;

public sealed class CleanupService
{
    public CleanupResult ReleaseWorkingSets()
    {
        var currentProcessId = Environment.ProcessId;
        var successCount = 0;
        var failureCount = 0;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == currentProcessId || process.HasExited)
                    {
                        continue;
                    }

                    if (EmptyWorkingSet(process.Handle))
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }
                }
                catch
                {
                    failureCount++;
                }
            }
        }

        var message = successCount > 0
            ? $"已尝试释放 {successCount} 个进程的工作集，跳过/失败 {failureCount} 个。"
            : "没有可释放的普通进程，或当前权限不足。";

        return new CleanupResult(successCount, failureCount, message);
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr processHandle);
}

public sealed record CleanupResult(int SuccessCount, int FailureCount, string Message);
