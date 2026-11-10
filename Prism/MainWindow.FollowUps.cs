using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Prism.Notifications;
using Windows.System;

namespace Prism;

public sealed partial class MainWindow
{
    private static readonly TimeSpan DefaultFollowUpReminder = new(17, 0, 0);

    private readonly FollowUpStore _followUps = new();

    private TimeSpan FollowUpReminder =>
        TimeSpan.TryParseExact(_settings.FollowUpReminderTime, @"hh\:mm", CultureInfo.InvariantCulture, out var time)
            ? time
            : DefaultFollowUpReminder;

    private bool FollowUpsDue => _followUps.Items.Count > 0 && DateTime.Now.TimeOfDay >= FollowUpReminder;

    /// <summary>Updates the star badge, the end-of-day highlight and the Follow-ups section.</summary>
    private void RenderFollowUps()
    {
        int pending = _followUps.Items.Count;
        FollowUpBadge.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
        FollowUpBadgeText.Text = pending.ToString(CultureInfo.CurrentCulture);

        bool due = FollowUpsDue;
        NotificationsHeader.Background = due
            ? (Brush)Application.Current.Resources["FollowUpDueBrush"]
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (due)
        {
            NotificationsSummaryText.Text = pending == 1 ? "1 follow-up left today" : $"{pending} follow-ups left today";
        }

        string reminder = DateTime.Today.Add(FollowUpReminder).ToString("h:mm tt", CultureInfo.CurrentCulture);
        FollowUpDueText.Text = pending == 0 ? string.Empty : due ? "Due now" : $"Due by {reminder}";
        FollowUpsList.Children.Clear();
        foreach (var followUp in _followUps.Items)
        {
            FollowUpsList.Children.Add(CreateFollowUpCard(followUp));
        }
    }

    private Border CreateFollowUpCard(FollowUp followUp)
    {
        var grid = new Grid { ColumnSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = followUp.AppName == FollowUpStore.ManualSource
                ? $"Added manually · {FormatNotificationTime(followUp.Time)}"
                : $"{followUp.AppName} · {FormatNotificationTime(followUp.Time)}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["FollowUpTextBrush"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = followUp.Title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (!string.IsNullOrEmpty(followUp.Body))
        {
            text.Children.Add(new TextBlock
            {
                Text = followUp.Body,
                FontSize = 12,
                Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        grid.Children.Add(text);

        var done = CreateIconButton("\uE73E", "Mark as done");
        done.VerticalAlignment = VerticalAlignment.Center;
        done.Click += (_, _) =>
        {
            _followUps.Complete(followUp.Key);
            RenderNotifications();
        };
        Grid.SetColumn(done, 1);
        grid.Children.Add(done);

        return new Border
        {
            Child = grid,
            Background = (Brush)Application.Current.Resources["FollowUpCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["FollowUpCardBorderBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 6, 8),
        };
    }

    private void AddManualFollowUp()
    {
        string text = NewFollowUpBox.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }
        _followUps.AddManual(text);
        NewFollowUpBox.Text = string.Empty;
        RenderNotifications();
    }

    private void AddFollowUpButton_Click(object sender, RoutedEventArgs e) => AddManualFollowUp();

    private void NewFollowUpBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddManualFollowUp();
            e.Handled = true;
        }
    }

    private void FollowUpHistoryButton_Click(object sender, RoutedEventArgs e) => OpenFollowUpHistory();

    /// <summary>Opens the history file in its default app (usually Excel), creating it if needed.</summary>
    private void OpenFollowUpHistory()
    {
        if (!FollowUpHistory.Exists)
        {
            Directory.CreateDirectory(WidgetSettings.DataFolder);
            File.WriteAllText(FollowUpHistory.FilePath, FollowUpHistory.Header + Environment.NewLine,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        try
        {
            Process.Start(new ProcessStartInfo(FollowUpHistory.FilePath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No app is associated with .csv files; show the file in Explorer instead.
            Process.Start("explorer.exe", $"/select,\"{FollowUpHistory.FilePath}\"");
        }
    }

    /// <summary>Star that adds or removes a notification from the follow-ups.</summary>
    private Button CreateFollowUpToggle(NotificationItem item)
    {
        bool starred = _followUps.Contains(item);
        var star = CreateIconButton(starred ? "\uE735" : "\uE734", starred ? "Remove from follow-ups" : "Follow up before day end");
        if (starred && star.Content is FontIcon icon)
        {
            icon.Foreground = (Brush)Application.Current.Resources["FollowUpBrush"];
        }
        star.Click += (_, _) =>
        {
            _followUps.Toggle(item);
            RenderNotifications();
        };
        return star;
    }

    private static Button CreateIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 11 },
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["NotificationCardBrush"],
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, tooltip);
        AutomationProperties.SetName(button, tooltip);
        return button;
    }
}
