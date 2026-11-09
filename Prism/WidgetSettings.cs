using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using Prism.Audio;

namespace Prism;

internal sealed class WidgetSettings
{
    public static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Prism");

    private static readonly string FilePath = Path.Combine(DataFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public int? X { get; set; }
    public int? Y { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public bool Locked { get; set; }
    public bool ClickThrough { get; set; }
    public double Transparency { get; set; } = 0.4;

    [JsonConverter(typeof(JsonStringEnumConverter<AudioSource>))]
    public AudioSource SpectrumSource { get; set; } = AudioSource.Speakers;

    /// <summary>From this time of day, pending follow-ups are highlighted (24-hour "HH:mm").</summary>
    public string FollowUpReminderTime { get; set; } = "17:00";

    public static WidgetSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(FilePath)) ?? new WidgetSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new WidgetSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Prism";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled && Environment.ProcessPath is string exePath)
        {
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
