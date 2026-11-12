using System.Globalization;
using System.Text;

namespace Prism.Notifications;

/// <summary>
/// Append-only log of follow-up activity in %LOCALAPPDATA%\Prism\followup-history.csv,
/// so past days can be reviewed (for example in Excel, filtered by Date).
/// </summary>
internal static class FollowUpHistory
{
    public static readonly string FilePath = Path.Combine(WidgetSettings.DataFolder, "followup-history.csv");

    public const string Header = "Date,Time,Event,Source,Title,Details,Received,Comment";
    private const string HeaderWithoutComment = "Date,Time,Event,Source,Title,Details,Received";

    private static bool _headerChecked;

    public static bool Exists => File.Exists(FilePath);

    /// <summary>Records an event such as "Added", "Done" or "Removed", with an optional closing comment.</summary>
    public static void Log(string eventName, FollowUp followUp, DateTimeOffset? at = null, string? comment = null)
    {
        UpgradeHeader();
        var when = (at ?? DateTimeOffset.Now).LocalDateTime;
        string line = string.Join(",",
            when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            when.ToString("HH:mm", CultureInfo.InvariantCulture),
            eventName,
            Escape(followUp.AppName),
            Escape(followUp.Title),
            Escape(followUp.Body),
            followUp.Time.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            Escape(comment ?? string.Empty));

        try
        {
            Directory.CreateDirectory(WidgetSettings.DataFolder);
            bool isNew = !File.Exists(FilePath);
            // UTF-8 with BOM so Excel shows non-English text correctly.
            using var writer = new StreamWriter(FilePath, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            if (isNew)
            {
                writer.WriteLine(Header);
            }
            writer.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Adds the Comment column to history files created before comments existed.</summary>
    private static void UpgradeHeader()
    {
        if (_headerChecked)
        {
            return;
        }
        _headerChecked = true;
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }
            string[] lines = File.ReadAllLines(FilePath);
            if (lines.Length > 0 && lines[0] == HeaderWithoutComment)
            {
                lines[0] = Header;
                File.WriteAllLines(FilePath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string Escape(string value)
    {
        string text = value.Replace("\r", " ").Replace("\n", " / ").Trim();
        // Notification text comes from other apps; stop spreadsheets treating it as a formula.
        if (text.Length > 0 && "=+-@".Contains(text[0]))
        {
            text = "'" + text;
        }
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
