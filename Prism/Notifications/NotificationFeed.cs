using System.Runtime.InteropServices;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Prism.Notifications;

internal enum NotificationAccess { NoIdentity, Denied, Allowed }

internal sealed record NotificationItem(
    uint Id, string AppId, string AppName, string Title, string Body, DateTimeOffset Time, RandomAccessStreamReference? Logo);

/// <summary>Reads the toasts currently in the Windows Notification Center (requires package identity).</summary>
internal sealed class NotificationFeed : IDisposable
{
    private UserNotificationListener? _listener;
    private bool _subscribed;

    /// <summary>Raised on a background thread when Windows reports a change. Not available on every build.</summary>
    public event Action? Changed;

    public static bool HasPackageIdentity
    {
        get
        {
            try
            {
                return Windows.ApplicationModel.Package.Current is not null;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Asks for access; must be called on the UI thread.</summary>
    public async Task<NotificationAccess> RequestAccessAsync()
    {
        if (!HasPackageIdentity)
        {
            return NotificationAccess.NoIdentity;
        }

        _listener ??= UserNotificationListener.Current;
        var status = await _listener.RequestAccessAsync();
        if (status != UserNotificationListenerAccessStatus.Allowed)
        {
            return NotificationAccess.Denied;
        }

        if (!_subscribed)
        {
            try
            {
                _listener.NotificationChanged += OnNotificationChanged;
                _subscribed = true;
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
            {
                // Change events aren't supported here; the caller also polls.
            }
        }
        return NotificationAccess.Allowed;
    }

    public async Task<IReadOnlyList<NotificationItem>> GetAsync()
    {
        if (_listener is null)
        {
            return [];
        }

        var items = new List<NotificationItem>();
        foreach (var notification in await _listener.GetNotificationsAsync(NotificationKinds.Toast))
        {
            if (ToItem(notification) is { } item)
            {
                items.Add(item);
            }
        }
        return items.OrderByDescending(i => i.Time).ToList();
    }

    public void Remove(uint id) => _listener?.RemoveNotification(id);

    public void ClearAll() => _listener?.ClearNotifications();

    private static NotificationItem? ToItem(UserNotification notification)
    {
        try
        {
            var texts = notification.Notification?.Visual?
                .GetBinding(KnownNotificationBindings.ToastGeneric)?
                .GetTextElements()
                .Select(t => t.Text?.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .Cast<string>()
                .ToList() ?? [];

            string appId = string.Empty, appName = "Unknown app";
            RandomAccessStreamReference? logo = null;
            try
            {
                var app = notification.AppInfo;
                appId = app.AppUserModelId ?? string.Empty;
                appName = string.IsNullOrWhiteSpace(app.DisplayInfo.DisplayName) ? appName : app.DisplayInfo.DisplayName;
                logo = app.DisplayInfo.GetLogo(new Windows.Foundation.Size(32, 32));
            }
            catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
            {
                // Some desktop apps don't expose display info.
            }

            string title = texts.Count > 0 ? texts[0] : appName;
            string body = string.Join("\n", texts.Skip(1));
            return new NotificationItem(notification.Id, appId, appName, title, body, notification.CreationTime, logo);
        }
        catch (COMException)
        {
            return null;
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) => Changed?.Invoke();

    public void Dispose()
    {
        if (_subscribed && _listener is not null)
        {
            try
            {
                _listener.NotificationChanged -= OnNotificationChanged;
            }
            catch (COMException)
            {
            }
            _subscribed = false;
        }
    }
}
