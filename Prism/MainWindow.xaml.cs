using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System.Power;
using Windows.UI;
using WinRT.Interop;

namespace Prism;

public sealed partial class MainWindow : Window
{
    private const double BatteryInnerWidth = 18;
    private const double DefaultMarginDip = 24;

    private readonly IntPtr _hwnd;
    private readonly OverlappedPresenter _presenter;
    private readonly WidgetSettings _settings;
    private readonly DispatcherQueueTimer _clockTimer;
    private readonly TrayIcon _tray;
    private readonly AlwaysActiveAcrylicBackdrop _backdrop;
    private readonly SolidColorBrush _greenBrush = new(Color.FromArgb(0xFF, 0x2F, 0xB3, 0x5A));
    private readonly SolidColorBrush _orangeBrush = new(Color.FromArgb(0xFF, 0xF5, 0xA5, 0x24));
    private readonly SolidColorBrush _redBrush = new(Color.FromArgb(0xFF, 0xE5, 0x48, 0x4D));

    private bool _needsDefaultPlacement;
    private double _lastScale;
    private bool _dragging;
    private NativeMethods.POINT _dragStartCursor;
    private PointInt32 _dragStartWindow;

    public MainWindow()
    {
        InitializeComponent();
        _settings = WidgetSettings.Load();
        _hwnd = WindowNative.GetWindowHandle(this);

        _backdrop = new AlwaysActiveAcrylicBackdrop(_settings.Transparency);
        SystemBackdrop = _backdrop;
        Sheen.Opacity = 1 - 0.6 * _backdrop.Transparency;
        _presenter = OverlappedPresenter.Create();
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        _presenter.SetBorderAndTitleBar(false, false);
        _presenter.IsAlwaysOnTop = _settings.AlwaysOnTop;
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.Title = "Prism";
        AppWindow.Changed += AppWindow_Changed;
        NativeMethods.SetClickThrough(_hwnd, _settings.ClickThrough);

        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        AppWindow.ResizeClient(new SizeInt32((int)(300 * scale), (int)(180 * scale)));
        if (_settings.X is int x && _settings.Y is int y && IsOnScreen(new PointInt32(x, y)))
        {
            AppWindow.Move(new PointInt32(x, y));
        }
        else
        {
            _needsDefaultPlacement = true;
        }

        _clockTimer = DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        PowerManager.RemainingChargePercentChanged += OnPowerChanged;
        PowerManager.BatteryStatusChanged += OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged += OnPowerChanged;
        UpdateBattery();

        _tray = new TrayIcon(_hwnd, "Prism") { LeftClick = ToggleVisibility, MenuProvider = BuildTrayMenu };
        UpdatePinGlyph();
        InitializeSpectrum();

        Root.Loaded += Root_Loaded;
        Closed += MainWindow_Closed;
    }

    private static bool IsOnScreen(PointInt32 point) =>
        DisplayArea.GetFromPoint(new PointInt32(point.X + 20, point.Y + 20), DisplayAreaFallback.None) is not null;

    private void Root_Loaded(object sender, RoutedEventArgs e)
    {
        _lastScale = Root.XamlRoot.RasterizationScale;
        ResizeToContent();
        if (_needsDefaultPlacement)
        {
            var workArea = DisplayArea.Primary.WorkArea;
            int margin = (int)(DefaultMarginDip * _lastScale);
            AppWindow.Move(new PointInt32(workArea.X + workArea.Width - AppWindow.Size.Width - margin, workArea.Y + margin));
            _needsDefaultPlacement = false;
        }
        Root.XamlRoot.Changed += XamlRoot_Changed;
        InitializeNotifications();
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (sender.RasterizationScale != _lastScale)
        {
            _lastScale = sender.RasterizationScale;
            ResizeToContent();
            ApplyRoundedCorners();
        }
    }

    private void ResizeToContent()
    {
        Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double scale = Root.XamlRoot.RasterizationScale;
        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Ceiling(Root.DesiredSize.Width * scale),
            (int)Math.Ceiling(Root.DesiredSize.Height * scale)));

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int overflow = AppWindow.Position.Y + AppWindow.Size.Height - (workArea.Y + workArea.Height);
        if (overflow > 0)
        {
            AppWindow.Move(new PointInt32(AppWindow.Position.X, Math.Max(workArea.Y, AppWindow.Position.Y - overflow)));
        }
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            ApplyRoundedCorners();
        }
    }

    private void ApplyRoundedCorners()
    {
        double radius = ((CornerRadius)Application.Current.Resources["WidgetCornerRadius"]).TopLeft;
        double scale = Root.XamlRoot?.RasterizationScale ?? NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        NativeMethods.SetRoundedRegion(_hwnd, (int)Math.Round(radius * 2 * scale));
    }

    private void UpdateClock()
    {
        if (DateTime.Now.Second == 0)
        {
            TimeZoneInfo.ClearCachedData();
        }

        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        TimeText.Text = now.ToString("h:mm", culture);
        AmPmText.Text = now.ToString("tt", culture);
        DateText.Text = now.ToString("dddd, MMMM d", culture);
        TimeZoneText.Text = FormatTimeZone(TimeZoneInfo.Local, now);
    }

    private static string FormatTimeZone(TimeZoneInfo zone, DateTime now)
    {
        string name = zone.IsDaylightSavingTime(now) ? zone.DaylightName : zone.StandardName;
        TimeSpan offset = zone.GetUtcOffset(now);
        string sign = offset < TimeSpan.Zero ? "−" : "+";
        return $"{name} · UTC{sign}{offset:hh\\:mm}";
    }

    private void OnPowerChanged(object? sender, object e) => DispatcherQueue.TryEnqueue(UpdateBattery);

    private void UpdateBattery()
    {
        var status = PowerManager.BatteryStatus;
        bool pluggedIn = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
        if (status == BatteryStatus.NotPresent)
        {
            BatteryBadge.Visibility = Visibility.Collapsed;
            return;
        }

        int percent = Math.Clamp(PowerManager.RemainingChargePercent, 0, 100);
        BatteryBadge.Visibility = Visibility.Visible;
        BatteryPercentText.Text = $"{percent}%";
        BatteryFill.Width = BatteryInnerWidth * percent / 100.0;
        BatteryFill.Fill = percent <= 10 ? _redBrush : percent <= 20 ? _orangeBrush : _greenBrush;
        ChargingIcon.Visibility = status == BatteryStatus.Charging || (pluggedIn && status == BatteryStatus.Idle)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        Tools.Opacity = 1;
        BatteryBadge.Opacity = 0;
    }

    private void Root_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        Tools.Opacity = 0;
        BatteryBadge.Opacity = 1;
    }

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_settings.Locked || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
        {
            return;
        }
        NativeMethods.GetCursorPos(out _dragStartCursor);
        _dragStartWindow = AppWindow.Position;
        _dragging = Root.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }
        NativeMethods.GetCursorPos(out var cursor);
        AppWindow.Move(new PointInt32(
            _dragStartWindow.X + cursor.X - _dragStartCursor.X,
            _dragStartWindow.Y + cursor.Y - _dragStartCursor.Y));
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            Root.ReleasePointerCapture(e.Pointer);
            EndDrag();
        }
    }

    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        SavePosition();
    }

    private void SavePosition()
    {
        _settings.X = AppWindow.Position.X;
        _settings.Y = AppWindow.Position.Y;
        _settings.Save();
    }

    private void WidgetMenu_Opening(object sender, object e)
    {
        AlwaysOnTopItem.IsChecked = _settings.AlwaysOnTop;
        LockItem.IsChecked = _settings.Locked;
        ClickThroughItem.IsChecked = _settings.ClickThrough;
        StartupItem.IsChecked = StartupRegistration.IsEnabled;
        foreach (var item in TransparencyMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = Math.Abs(ParseLevel(item) - _settings.Transparency) < 0.01;
        }
        foreach (var item in SpectrumMenu.Items.OfType<RadioMenuFlyoutItem>())
        {
            item.IsChecked = (string)item.Tag == _settings.SpectrumSource.ToString();
        }
    }

    private static double ParseLevel(RadioMenuFlyoutItem item) =>
        double.Parse((string)item.Tag, CultureInfo.InvariantCulture);

    private void TransparencyItem_Click(object sender, RoutedEventArgs e) =>
        SetTransparency(ParseLevel((RadioMenuFlyoutItem)sender));

    private void SetTransparency(double value)
    {
        _settings.Transparency = value;
        _backdrop.Transparency = value;
        Sheen.Opacity = 1 - 0.6 * _backdrop.Transparency;
        _settings.Save();
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => SetAlwaysOnTop(!_settings.AlwaysOnTop);

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => WidgetMenu.ShowAt(SettingsButton);

    private void AlwaysOnTopItem_Click(object sender, RoutedEventArgs e) => SetAlwaysOnTop(AlwaysOnTopItem.IsChecked);

    private void LockItem_Click(object sender, RoutedEventArgs e) => SetLocked(LockItem.IsChecked);

    private void ClickThroughItem_Click(object sender, RoutedEventArgs e) => SetClickThrough(ClickThroughItem.IsChecked);

    private void StartupItem_Click(object sender, RoutedEventArgs e) => StartupRegistration.SetEnabled(StartupItem.IsChecked);

    private void HideItem_Click(object sender, RoutedEventArgs e) => AppWindow.Hide();

    private void ExitItem_Click(object sender, RoutedEventArgs e) => Close();

    private void SetAlwaysOnTop(bool value)
    {
        _settings.AlwaysOnTop = value;
        _presenter.IsAlwaysOnTop = value;
        UpdatePinGlyph();
        _settings.Save();
    }

    private void SetLocked(bool value)
    {
        _settings.Locked = value;
        _settings.Save();
    }

    private void SetClickThrough(bool value)
    {
        _settings.ClickThrough = value;
        NativeMethods.SetClickThrough(_hwnd, value);
        _settings.Save();
    }

    private void UpdatePinGlyph()
    {
        PinGlyph.Glyph = _settings.AlwaysOnTop ? "\uE840" : "\uE718";
        PinButton.Opacity = _settings.AlwaysOnTop ? 1.0 : 0.6;
    }

    private void ToggleVisibility()
    {
        if (AppWindow.IsVisible)
        {
            AppWindow.Hide();
        }
        else
        {
            AppWindow.Show();
        }
    }

    private IReadOnlyList<TrayMenuItem?> BuildTrayMenu() =>
    [
        new(AppWindow.IsVisible ? "Hide widget" : "Show widget", false, ToggleVisibility),
        null,
        new("Always on top", _settings.AlwaysOnTop, () => SetAlwaysOnTop(!_settings.AlwaysOnTop)),
        new("Lock position", _settings.Locked, () => SetLocked(!_settings.Locked)),
        new("Click-through", _settings.ClickThrough, () => SetClickThrough(!_settings.ClickThrough)),
        new("Start with Windows", StartupRegistration.IsEnabled, () => StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled)),
        null,
        new("Exit", false, Close),
    ];

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _clockTimer.Stop();
        PowerManager.RemainingChargePercentChanged -= OnPowerChanged;
        PowerManager.BatteryStatusChanged -= OnPowerChanged;
        PowerManager.PowerSupplyStatusChanged -= OnPowerChanged;
        StopSpectrum();
        StopNotifications();
        SavePosition();
        _tray.Dispose();
        Application.Current.Exit();
    }
}
