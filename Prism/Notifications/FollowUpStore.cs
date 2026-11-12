using System.Text.Json;

namespace Prism.Notifications;

/// <summary>
/// A starred notification or a manually added item. Kept after the notification itself is gone.
/// </summary>
internal sealed record FollowUp(string Key, string AppName, string Title, string Body, DateTimeOffset Time, DateTimeOffset MarkedAt);

/// <summary>Saves follow-ups in %LOCALAPPDATA%\Prism\followups.json.</summary>
internal sealed class FollowUpStore
{
    private static readonly string FilePath = Path.Combine(WidgetSettings.DataFolder, "followups.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public const string ManualSource = "Manual";

    private readonly List<FollowUp> _items = Load();

    public FollowUpStore()
    {
        // Start the history with follow-ups that existed before history was recorded.
        if (!FollowUpHistory.Exists)
        {
            foreach (var item in _items)
            {
                FollowUpHistory.Log("Added", item, item.MarkedAt);
            }
        }
    }

    /// <summary>Oldest first, so the longest-waiting item is on top.</summary>
    public IReadOnlyList<FollowUp> Items => _items;

    /// <summary>Identifies the same notification across refreshes and restarts (Windows ids are not stable).</summary>
    public static string KeyOf(NotificationItem item) => $"{item.AppId}|{item.Time.UtcTicks}|{item.Title}";

    public bool Contains(NotificationItem item) => _items.Any(f => f.Key == KeyOf(item));

    public void Toggle(NotificationItem item)
    {
        string key = KeyOf(item);
        var existing = _items.FirstOrDefault(f => f.Key == key);
        if (existing is not null)
        {
            _items.Remove(existing);
            FollowUpHistory.Log("Removed", existing);
        }
        else
        {
            var followUp = new FollowUp(key, item.AppName, item.Title, item.Body, item.Time, DateTimeOffset.Now);
            _items.Add(followUp);
            FollowUpHistory.Log("Added", followUp);
        }
        Save();
    }

    public void AddManual(string text)
    {
        var now = DateTimeOffset.Now;
        var followUp = new FollowUp($"manual|{now.UtcTicks}", ManualSource, text.Trim(), string.Empty, now, now);
        _items.Add(followUp);
        FollowUpHistory.Log("Added", followUp);
        Save();
    }

    public void Complete(string key, string? comment = null)
    {
        var existing = _items.FirstOrDefault(f => f.Key == key);
        if (existing is not null)
        {
            _items.Remove(existing);
            FollowUpHistory.Log("Done", existing, comment: string.IsNullOrWhiteSpace(comment) ? null : comment.Trim());
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
