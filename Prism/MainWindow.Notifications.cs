using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Prism.Notifications;

namespace Prism;

public sealed partial class MainWindow
{
    private static readonly TimeSpan NotificationPollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan NotificationTimeRefresh = TimeSpan.FromMinutes(1);

    private readonly NotificationFeed _notificationFeed = new();
    private readonly Dictionary<string, BitmapImage> _appLogos = [];
    private IReadOnlyList<NotificationItem> _notifications = [];
    private NotificationAccess _notificationAccess = NotificationAccess.NoIdentity;
    private DispatcherQueueTimer? _notificationTimer;
    private string _notificationSignature = string.Empty;
    private DateTime _lastNotificationRender = DateTime.MinValue;
    private bool _notificationsExpanded;
    private bool _refreshingNotifications;

    /// <summary>Called once the window is loaded, because the access request needs the UI thread.</summary>
    private async void InitializeNotifications()
    {
        await RequestNotificationAccessAsync();
        _notificationFeed.Changed += () => DispatcherQueue.TryEnqueue(() => _ = RefreshNotificationsAsync());

        _notificationTimer = DispatcherQueue.CreateTimer();
        _notificationTimer.Interval = NotificationPollInterval;
        _notificationTimer.Tick += (_, _) => _ = RefreshNotificationsAsync();
        _notificationTimer.Start();
        await RefreshNotificationsAsync();
    }

    private async Task RequestNotificationAccessAsync()
    {
        try
        {
            _notificationAccess = await _notificationFeed.RequestAccessAsync();
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            _notificationAccess = NotificationAccess.Denied;
        }
    }

    private async Task RefreshNotificationsAsync()
    {
        if (_refreshingNotifications || !AppWindow.IsVisible)
        {
            return;
        }
        _refreshingNotifications = true;
        try
        {
            if (_notificationAccess == NotificationAccess.Allowed)
            {
                var items = await _notificationFeed.GetAsync();
                string signature = string.Join(",", items.Select(i => i.Id));
                if (signature == _notificationSignature && DateTime.Now - _lastNotificationRender < NotificationTimeRefresh)
                {
                    return;
                }
                _notifications = items;
                _notificationSignature = signature;
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            _notificationAccess = NotificationAccess.Denied;
            _notifications = [];
        }
        finally
        {
            _refreshingNotifications = false;
        }
        RenderNotifications();
    }

    private void RenderNotifications()
    {
        _lastNotificationRender = DateTime.Now;
        int count = _notifications.Count;
        bool allowed = _notificationAccess == NotificationAccess.Allowed;

        NotificationsSummaryText.Text = _notificationAccess switch
        {
            NotificationAccess.NoIdentity => "Notifications aren't set up",
            NotificationAccess.Denied => "Notification access is off",
            _ => count switch { 0 => "No new notifications", 1 => "1 notification", _ => $"{count} notifications" },
        };

        string? message = _notificationAccess switch
        {
            NotificationAccess.NoIdentity => "Run Prism\\Packaging\\Register-Prism.ps1 once, then start Prism again.",
            NotificationAccess.Denied => "Allow Prism under Settings → Privacy & security → Notifications.",
            _ when count == 0 => "You're all caught up.",
            _ => null,
        };
        NotificationsMessage.Text = message ?? string.Empty;
        NotificationsMessage.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        NotificationsToolbar.Visibility = allowed && count > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationSettingsButton.Visibility = _notificationAccess == NotificationAccess.Denied
            ? Visibility.Visible
            : Visibility.Collapsed;

        NotificationsList.Children.Clear();
        foreach (var group in _notifications.GroupBy(n => (n.AppId, n.AppName)))
        {
            NotificationsList.Children.Add(CreateNotificationGroup(group.ToList()));
        }
        RenderFollowUps();

        if (_notificationsExpanded && Root.XamlRoot is not null)
        {
            ResizeToContent();
        }
    }

    /// <summary>One card per app, like the Windows Notification Center.</summary>
    private Border CreateNotificationGroup(IReadOnlyList<NotificationItem> items)
    {
        var content = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        header.Children.Add(CreateAppLogo(items[0]));
        header.Children.Add(new TextBlock
        {
            Text = items[0].AppName,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Opacity = 0.75,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        content.Children.Add(header);

        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                content.Children.Add(new Rectangle
                {
                    Height = 1,
                    Fill = (Brush)Application.Current.Resources["NotificationCardBorderBrush"],
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }
            content.Children.Add(CreateNotificationRow(items[i]));
        }

        return new Border
        {
            Child = content,
            Background = (Brush)Application.Current.Resources["NotificationCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["NotificationCardBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 6, 10),
        };
    }

    private Grid CreateNotificationRow(NotificationItem item)
    {
        var row = new Grid { ColumnSpacing = 6, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(item.Body))
        {
            text.Children.Add(new TextBlock
            {
                Text = item.Body,
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        row.Children.Add(text);

        bool starred = _followUps.Contains(item);
        var side = new Grid { MinWidth = 52 };
        var info = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        info.Children.Add(new TextBlock
        {
            Text = FormatNotificationTime(item.Time),
            FontSize = 11,
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Right,
        });
        if (starred)
        {
            info.Children.Add(new FontIcon
            {
                Glyph = "\uE735",
                FontSize = 12,
                Foreground = (Brush)Application.Current.Resources["FollowUpBrush"],
                HorizontalAlignment = HorizontalAlignment.Right,
            });
        }
        side.Children.Add(info);

        // Hover actions replace the time: star for follow-up, ✕ to dismiss.
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0,
        };
        actions.Children.Add(CreateFollowUpToggle(item));
        var close = CreateIconButton("\uE711", "Dismiss");
        close.Click += (_, _) => DismissNotification(item.Id);
        actions.Children.Add(close);
        side.Children.Add(actions);
        Grid.SetColumn(side, 1);
        row.Children.Add(side);

        row.PointerEntered += (_, _) => { actions.Opacity = 1; info.Opacity = 0; };
        row.PointerExited += (_, _) => { actions.Opacity = 0; info.Opacity = 1; };
        return row;
    }

    private FrameworkElement CreateAppLogo(NotificationItem item)
    {
        var image = new Image { Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        if (_appLogos.TryGetValue(item.AppId, out var cached))
        {
            image.Source = cached;
        }
        else if (item.Logo is not null)
        {
            _ = LoadAppLogoAsync(item, image);
        }
        return image;
    }

    private async Task LoadAppLogoAsync(NotificationItem item, Image image)
    {
        try
        {
            using var stream = await item.Logo!.OpenReadAsync();
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            _appLogos[item.AppId] = bitmap;
            image.Source = bitmap;
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No usable logo; the app name is still shown.
        }
    }

    private static string FormatNotificationTime(DateTimeOffset time)
    {
        var local = time.LocalDateTime;
        var age = DateTime.Now - local;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Now";
        }
        if (age < TimeSpan.FromHours(1))
        {
            return $"{(int)age.TotalMinutes}m ago";
        }
        return local.Date == DateTime.Today ? local.ToString("h:mm tt") : local.ToString("MMM d");
    }

    private void DismissNotification(uint id)
    {
        _notificationFeed.Remove(id);
        _notifications = _notifications.Where(n => n.Id != id).ToList();
        _notificationSignature = string.Join(",", _notifications.Select(n => n.Id));
        RenderNotifications();
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        _notificationFeed.ClearAll();
        _notifications = [];
        _notificationSignature = string.Empty;
        RenderNotifications();
    }

    private async void NotificationsHeader_Click(object sender, RoutedEventArgs e)
    {
        _notificationsExpanded = !_notificationsExpanded;
        NotificationsPanel.Visibility = _notificationsExpanded ? Visibility.Visible : Visibility.Collapsed;
        NotificationsChevron.Glyph = _notificationsExpanded ? "\uE70E" : "\uE70D";
        ResizeToContent();

        if (_notificationsExpanded && _notificationAccess == NotificationAccess.Denied)
        {
            await RequestNotificationAccessAsync();
            await RefreshNotificationsAsync();
        }
    }

    private async void NotificationSettingsButton_Click(object sender, RoutedEventArgs e) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-notifications"));

    private void StopNotifications()
    {
        _notificationTimer?.Stop();
        _notificationFeed.Dispose();
    }
}
