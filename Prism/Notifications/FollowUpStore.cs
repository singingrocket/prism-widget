using System.Text.Json;

namespace Prism.Notifications;

/// <summary>A notification the user starred to follow up on. Kept after the notification itself is gone.</summary>
internal sealed record FollowUp(string Key, string AppName, string Title, string Body, DateTimeOffset Time, DateTimeOffset MarkedAt);

/// <summary>Saves follow-ups in %LOCALAPPDATA%\Prism\followups.json.</summary>
internal sealed class FollowUpStore
{
    private static readonly string FilePath = Path.Combine(WidgetSettings.DataFolder, "followups.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<FollowUp> _items = Load();

    /// <summary>Oldest first, so the longest-waiting item is on top.</summary>
    public IReadOnlyList<FollowUp> Items => _items;

    /// <summary>Identifies the same notification across refreshes and restarts (Windows ids are not stable).</summary>
    public static string KeyOf(NotificationItem item) => $"{item.AppId}|{item.Time.UtcTicks}|{item.Title}";

    public bool Contains(NotificationItem item) => _items.Any(f => f.Key == KeyOf(item));

    public void Toggle(NotificationItem item)
    {
        string key = KeyOf(item);
        if (_items.RemoveAll(f => f.Key == key) == 0)
        {
            _items.Add(new FollowUp(key, item.AppName, item.Title, item.Body, item.Time, DateTimeOffset.Now));
        }
        Save();
    }

    public void Complete(string key)
    {
        if (_items.RemoveAll(f => f.Key == key) > 0)
        {
            Save();
        }
    }

    private static List<FollowUp> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<List<FollowUp>>(File.ReadAllText(FilePath)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return [];
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(WidgetSettings.DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_items, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
