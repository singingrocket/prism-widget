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

    // Closing-comment box state, kept while the list is rebuilt by refreshes.
    private string? _commentKey;
    private string _commentDraft = string.Empty;
    private bool _commentFocused;

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
        if (_commentKey is not null && _followUps.Items.All(f => f.Key != _commentKey))
        {
            ResetComment();
        }
        bool refocus = _commentFocused;
        FollowUpsList.Children.Clear();
        foreach (var followUp in _followUps.Items)
        {
            FollowUpsList.Children.Add(CreateFollowUpCard(followUp, refocus));
        }
    }

    private Border CreateFollowUpCard(FollowUp followUp, bool refocusComment)
    {
        var grid = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

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

        bool commenting = _commentKey == followUp.Key;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var comment = CreateIconButton("\uE90A", commenting ? "Cancel comment" : "Close with a comment");
        comment.Click += (_, _) =>
        {
            if (_commentKey == followUp.Key)
            {
                ResetComment();
            }
            else
            {
                _commentKey = followUp.Key;
                _commentDraft = string.Empty;
                _commentFocused = true;
            }
            RenderFollowUps();
            ResizeToContent();
        };
        actions.Children.Add(comment);

        var done = CreateIconButton("\uE73E", "Mark as done");
        done.Click += (_, _) => CloseFollowUp(followUp.Key, comment: null);
        actions.Children.Add(done);
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        if (commenting)
        {
            var commentRow = CreateCommentRow(followUp.Key, refocusComment);
            Grid.SetRow(commentRow, 1);
            Grid.SetColumnSpan(commentRow, 2);
            grid.Children.Add(commentRow);
        }

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

    /// <summary>Comment box and Close button shown under a follow-up.</summary>
    private Grid CreateCommentRow(string key, bool focus)
    {
        var row = new Grid { ColumnSpacing = 6 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var box = new TextBox
        {
            PlaceholderText = "Closing comment (optional)…",
            Text = _commentDraft,
            FontSize = 12,
            MaxLength = 500,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
        };
        AutomationProperties.SetName(box, "Closing comment");
        box.TextChanged += (_, _) => _commentDraft = box.Text;
        box.GotFocus += (_, _) => _commentFocused = true;
        box.LostFocus += (_, _) => _commentFocused = false;
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                CloseFollowUp(key, box.Text);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Escape)
            {
                ResetComment();
                RenderFollowUps();
                ResizeToContent();
                e.Handled = true;
            }
        };
        if (focus)
        {
            box.Loaded += (_, _) =>
            {
                box.Focus(FocusState.Programmatic);
                box.SelectionStart = box.Text.Length;
            };
        }
        row.Children.Add(box);

        var close = new Button
        {
            Content = "Close",
            FontSize = 12,
            Padding = new Thickness(12, 4, 12, 5),
            VerticalAlignment = VerticalAlignment.Stretch,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        AutomationProperties.SetName(close, "Close follow-up");
        close.Click += (_, _) => CloseFollowUp(key, box.Text);
        Grid.SetColumn(close, 1);
        row.Children.Add(close);
        return row;
    }

    private void CloseFollowUp(string key, string? comment)
    {
        if (_commentKey == key)
        {
            ResetComment();
        }
        _followUps.Complete(key, comment);
        RenderNotifications();
        ResizeToContent();
    }

    private void ResetComment()
    {
        _commentKey = null;
        _commentDraft = string.Empty;
        _commentFocused = false;
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
