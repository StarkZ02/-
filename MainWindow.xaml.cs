using System.Windows;
using System.Windows.Input;
using MemoryMonitorBall.Models;
using MemoryMonitorBall.Services;
using MemoryMonitorBall.ViewModels;
using Forms = System.Windows.Forms;

namespace MemoryMonitorBall;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;
    private readonly System.Windows.Threading.DispatcherTimer _singleClickTimer;
    private Forms.NotifyIcon? _notifyIcon;
    private System.Windows.Point? _dragStartPoint;
    private bool _wasDragged;
    private bool _isExitRequested;
    private bool _suppressNextMouseUpClick;

    public MainWindow()
    {
        _settings = _settingsService.Load();
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.UserNotificationRequested += ViewModel_UserNotificationRequested;
        _singleClickTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime)
        };
        _singleClickTimer.Tick += SingleClickTimer_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RestorePosition();
        CreateNotifyIcon();
        _viewModel.Start();
    }

    private void BallSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            _singleClickTimer.Stop();
            _dragStartPoint = null;
            _wasDragged = false;
            _suppressNextMouseUpClick = true;
            BallSurface.ReleaseMouseCapture();
            _viewModel.ReleaseMemoryCommand.Execute(null);
            e.Handled = true;
            return;
        }

        _dragStartPoint = e.GetPosition(this);
        _wasDragged = false;
        BallSurface.CaptureMouse();
    }

    private void BallSurface_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _wasDragged = true;
        BallSurface.ReleaseMouseCapture();

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove can throw if the mouse button is released between event dispatch and call.
        }
    }

    private void BallSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        BallSurface.ReleaseMouseCapture();
        _dragStartPoint = null;

        if (_suppressNextMouseUpClick)
        {
            _suppressNextMouseUpClick = false;
            return;
        }

        if (!_wasDragged)
        {
            _singleClickTimer.Stop();
            _singleClickTimer.Start();
        }
    }

    private void SingleClickTimer_Tick(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        _viewModel.ToggleDetailsCommand.Execute(null);
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _settings.Left = Left;
        _settings.Top = Top;
        _settingsService.Save(_settings);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExitRequested)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        _viewModel.Stop();
        _singleClickTimer.Stop();
        _singleClickTimer.Tick -= SingleClickTimer_Tick;
        _viewModel.UserNotificationRequested -= ViewModel_UserNotificationRequested;
        _notifyIcon?.Dispose();
    }

    private void DetailsPopup_Closed(object? sender, EventArgs e)
    {
        _viewModel.IsDetailsOpen = false;
    }

    private void ToggleDetailsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleDetailsCommand.Execute(null);
    }

    private void ReleaseMemoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ReleaseMemoryCommand.Execute(null);
    }

    private void HideMenuItem_Click(object sender, RoutedEventArgs e)
    {
        HideToTray();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void CloseDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.IsDetailsOpen = false;
    }

    private void RestorePosition()
    {
        if (_settings.Left.HasValue && _settings.Top.HasValue && IsOnAnyScreen(_settings.Left.Value, _settings.Top.Value))
        {
            Left = _settings.Left.Value;
            Top = _settings.Top.Value;
            return;
        }

        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Bottom - Height - 80;
    }

    private static bool IsOnAnyScreen(double left, double top)
    {
        var point = new System.Drawing.Point((int)left, (int)top);
        return Forms.Screen.AllScreens.Any(screen => screen.WorkingArea.Contains(point));
    }

    private void CreateNotifyIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "内存监控球",
            Icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        _notifyIcon.ContextMenuStrip.Items.Add("显示", null, (_, _) => ShowFromTray());
        _notifyIcon.ContextMenuStrip.Items.Add("隐藏", null, (_, _) => HideToTray());
        _notifyIcon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
        _notifyIcon.ContextMenuStrip.Items.Add("退出", null, (_, _) => ExitApplication());
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void HideToTray()
    {
        _viewModel.IsDetailsOpen = false;
        Hide();
    }

    private void ShowFromTray()
    {
        Show();
        Activate();
    }

    private void ExitApplication()
    {
        _isExitRequested = true;
        Close();
    }

    private void ViewModel_UserNotificationRequested(object? sender, string message)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = "内存监控球";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(2500);
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(processPath) ?? System.Drawing.SystemIcons.Application;
            }
        }
        catch
        {
            // Fall back to the default icon if the embedded executable icon cannot be read.
        }

        return System.Drawing.SystemIcons.Application;
    }
}
