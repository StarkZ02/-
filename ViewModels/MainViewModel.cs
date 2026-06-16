using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MemoryMonitorBall.Models;
using MemoryMonitorBall.Services;

namespace MemoryMonitorBall.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MonitorService _monitorService = new();
    private readonly DispatcherTimer _fastTimer;
    private readonly DispatcherTimer _processTimer;
    private readonly RelayCommand _terminateProcessCommand;
    private ProcessInfo? _selectedProcess;
    private bool _isDetailsOpen;
    private string _memoryPercentText = "--%";
    private string _memorySummaryText = "正在读取内存...";
    private string _networkStatusText = "网络：检测中";
    private string _downloadSpeedText = "↓ 0 KB/s";
    private string _uploadSpeedText = "↑ 0 KB/s";
    private string _statusMessage = "就绪";
    private double _memoryUsedPercent;
    private System.Windows.Media.Brush _ballBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(38, 166, 91));

    public MainViewModel()
    {
        Processes = [];
        ToggleDetailsCommand = new RelayCommand(ToggleDetails);
        RefreshProcessesCommand = new RelayCommand(RefreshProcesses);
        ReleaseMemoryCommand = new RelayCommand(ReleaseMemory);
        _terminateProcessCommand = new RelayCommand(TerminateSelectedProcess, () => SelectedProcess?.CanTerminate == true);
        TerminateProcessCommand = _terminateProcessCommand;

        _fastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fastTimer.Tick += (_, _) => RefreshFastMetrics();

        _processTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _processTimer.Tick += (_, _) => RefreshProcesses();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ProcessInfo> Processes { get; }

    public ICommand ToggleDetailsCommand { get; }

    public ICommand RefreshProcessesCommand { get; }

    public ICommand ReleaseMemoryCommand { get; }

    public ICommand TerminateProcessCommand { get; }

    public event EventHandler<string>? UserNotificationRequested;

    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set
        {
            if (SetProperty(ref _isDetailsOpen, value))
            {
                if (value)
                {
                    RefreshProcesses();
                    _processTimer.Start();
                }
                else
                {
                    _processTimer.Stop();
                }
            }
        }
    }

    public ProcessInfo? SelectedProcess
    {
        get => _selectedProcess;
        set
        {
            if (SetProperty(ref _selectedProcess, value))
            {
                _terminateProcessCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MemoryPercentText
    {
        get => _memoryPercentText;
        private set => SetProperty(ref _memoryPercentText, value);
    }

    public string MemorySummaryText
    {
        get => _memorySummaryText;
        private set => SetProperty(ref _memorySummaryText, value);
    }

    public string NetworkStatusText
    {
        get => _networkStatusText;
        private set => SetProperty(ref _networkStatusText, value);
    }

    public string DownloadSpeedText
    {
        get => _downloadSpeedText;
        private set => SetProperty(ref _downloadSpeedText, value);
    }

    public string UploadSpeedText
    {
        get => _uploadSpeedText;
        private set => SetProperty(ref _uploadSpeedText, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public double MemoryUsedPercent
    {
        get => _memoryUsedPercent;
        private set => SetProperty(ref _memoryUsedPercent, value);
    }

    public System.Windows.Media.Brush BallBrush
    {
        get => _ballBrush;
        private set => SetProperty(ref _ballBrush, value);
    }

    public void Start()
    {
        RefreshFastMetrics();
        _fastTimer.Start();
    }

    public void Stop()
    {
        _fastTimer.Stop();
        _processTimer.Stop();
    }

    public void Dispose()
    {
        Stop();
        _fastTimer.Tick -= (_, _) => RefreshFastMetrics();
        _processTimer.Tick -= (_, _) => RefreshProcesses();
    }

    private void RefreshFastMetrics()
    {
        try
        {
            var memory = _monitorService.GetMemorySnapshot();
            var network = _monitorService.GetNetworkSnapshot();

            MemoryUsedPercent = memory.UsedPercent;
            MemoryPercentText = $"{memory.UsedPercent:0}%";
            MemorySummaryText = $"已用 {FormatBytes(memory.UsedBytes)} / 总计 {FormatBytes(memory.TotalBytes)}，可用 {FormatBytes(memory.AvailableBytes)}";
            NetworkStatusText = network.IsOnline ? "网络：在线" : "网络：离线";
            DownloadSpeedText = $"↓ {FormatRate(network.DownloadBytesPerSecond)}";
            UploadSpeedText = $"↑ {FormatRate(network.UploadBytesPerSecond)}";
            BallBrush = CreateBallBrush(memory.UsedPercent);
        }
        catch (Exception ex)
        {
            StatusMessage = $"监控刷新失败：{ex.Message}";
        }
    }

    private void RefreshProcesses()
    {
        try
        {
            var selectedProcessId = SelectedProcess?.ProcessId;
            var processList = _monitorService.GetTopMemoryProcesses();

            Processes.Clear();
            foreach (var process in processList)
            {
                Processes.Add(process);
            }

            SelectedProcess = Processes.FirstOrDefault(process => process.ProcessId == selectedProcessId);
            var networkProcessCount = Processes.Count(process => process.NetworkUsage.TotalConnectionCount > 0);
            StatusMessage = $"已刷新 {Processes.Count} 个进程，{networkProcessCount} 个正在联网。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"进程刷新失败：{ex.Message}";
        }
    }

    private void ReleaseMemory()
    {
        var result = _monitorService.ReleaseWorkingSets();
        StatusMessage = result.Message;
        UserNotificationRequested?.Invoke(this, result.Message);
        RefreshFastMetrics();
        RefreshProcesses();
    }

    private void TerminateSelectedProcess()
    {
        if (SelectedProcess is null)
        {
            return;
        }

        var process = SelectedProcess;
        var confirm = System.Windows.MessageBox.Show(
            $"确定要结束 {process.DisplayName} 吗？",
            "确认结束进程",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _monitorService.TryTerminate(process, out var message);
        StatusMessage = message;
        UserNotificationRequested?.Invoke(this, message);
        RefreshProcesses();
    }

    private void ToggleDetails() => IsDetailsOpen = !IsDetailsOpen;

    private static System.Windows.Media.Brush CreateBallBrush(double memoryUsedPercent)
    {
        var color = memoryUsedPercent switch
        {
            >= 85 => System.Windows.Media.Color.FromRgb(224, 79, 57),
            >= 70 => System.Windows.Media.Color.FromRgb(234, 153, 45),
            _ => System.Windows.Media.Color.FromRgb(38, 166, 91)
        };

        return new SolidColorBrush(color);
    }

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

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
