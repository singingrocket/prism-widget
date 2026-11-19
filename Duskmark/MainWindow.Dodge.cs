using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Duskmark;

/// <summary>
/// "Move away from mouse": the widget slides out of the pointer's way, except when the pointer
/// approaches from the right side, which is the grab side for reading and dragging.
/// </summary>
public sealed partial class MainWindow
{
    private const double DodgeMarginDip = 28;
    private const double GrabSideDip = 28;
    private const double DodgeStep = 0.35;
    private static readonly TimeSpan ReturnHomeDelay = TimeSpan.FromSeconds(1.2);

    private DispatcherQueueTimer? _dodgeTimer;
    private PointInt32? _dodgeHome;
    private PointInt32? _dodgeTarget;
    private DateTime? _awayFromHomeSince;
    private bool _dodgeEngaged;
    private bool _menuOpen;

    private void InitializeDodge()
    {
        _dodgeTimer = DispatcherQueue.CreateTimer();
        _dodgeTimer.Interval = TimeSpan.FromMilliseconds(30);
        _dodgeTimer.Tick += (_, _) => OnDodgeTick();
        ApplyMoveAway();
    }

    private void SetMoveAway(bool value)
    {
        _settings.MoveAwayFromMouse = value;
        _settings.Save();
        ApplyMoveAway();
    }

    private void ApplyMoveAway()
    {
        bool on = _settings.MoveAwayFromMouse;
        GrabHandle.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            _dodgeTimer?.Start();
            return;
        }
        _dodgeTimer?.Stop();
        if (_dodgeHome is PointInt32 home)
        {
            AppWindow.Move(home);
        }
        ResetDodge();
    }

    private void ResetDodge()
    {
        _dodgeHome = null;
        _dodgeTarget = null;
        _awayFromHomeSince = null;
        _dodgeEngaged = false;
    }

    /// <summary>Where the widget belongs: the user's position, not where it slid to.</summary>
    private PointInt32 HomePosition => _dodgeHome ?? AppWindow.Position;

    private void OnDodgeTick()
    {
        if (!AppWindow.IsVisible || Root.XamlRoot is null)
        {
            return;
        }
        if (_dragging)
        {
            _dodgeTarget = null;
            return;
        }
        AnimateTowardTarget();

        bool busy = _menuOpen || FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox;
        double scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        int margin = (int)(DodgeMarginDip * scale);
        NativeMethods.GetCursorPos(out var cursor);
        var size = AppWindow.Size;
        // Judge against where the widget is going, not where it is mid-slide, so a slide can't re-trigger itself.
        var resting = _dodgeTarget ?? AppWindow.Position;

        if (IsNear(cursor, resting, size, margin))
        {
            if (!_dodgeEngaged && !busy)
            {
                if (cursor.X >= resting.X + size.Width - (int)(GrabSideDip * scale))
                {
                    _dodgeEngaged = true;
                }
                else
                {
                    DodgeFrom(cursor, size, margin);
                }
            }
            _awayFromHomeSince = null;
            return;
        }

        if (!busy)
        {
            _dodgeEngaged = false;
        }
        if (_dodgeHome is PointInt32 home && !busy && !IsNear(cursor, home, size, margin))
        {
            _awayFromHomeSince ??= DateTime.Now;
            if (DateTime.Now - _awayFromHomeSince >= ReturnHomeDelay)
            {
                // _dodgeHome is cleared on arrival, so a pointer meeting the widget on its way back still uses the real home.
                _dodgeTarget = home;
                _awayFromHomeSince = null;
            }
        }
        else
        {
            _awayFromHomeSince = null;
        }
    }

    /// <summary>
    /// Picks the spot nearest home, in any of 8 directions or a screen corner, that stays on screen and clears the pointer.
    /// If no spot clears it (a very large widget), the widget stays where it is instead of jumping around.
    /// </summary>
    private void DodgeFrom(NativeMethods.POINT cursor, SizeInt32 size, int margin)
    {
        var home = HomePosition;
        var work = DisplayArea.GetFromPoint(new PointInt32(home.X + size.Width / 2, home.Y + size.Height / 2),
            DisplayAreaFallback.Nearest).WorkArea;
        int gap = margin + 8;

        var candidates = new List<PointInt32>();
        foreach (int dx in new[] { -1, 0, 1 })
        {
            foreach (int dy in new[] { -1, 0, 1 })
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }
                int x = dx < 0 ? cursor.X - margin - gap - size.Width : dx > 0 ? cursor.X + margin + gap : home.X;
                int y = dy < 0 ? cursor.Y - margin - gap - size.Height : dy > 0 ? cursor.Y + margin + gap : home.Y;
                candidates.Add(ClampToWorkArea(new PointInt32(x, y), size, work));
            }
        }
        candidates.Add(ClampToWorkArea(new PointInt32(work.X, work.Y), size, work));
        candidates.Add(ClampToWorkArea(new PointInt32(work.X + work.Width, work.Y), size, work));
        candidates.Add(ClampToWorkArea(new PointInt32(work.X, work.Y + work.Height), size, work));
        candidates.Add(ClampToWorkArea(new PointInt32(work.X + work.Width, work.Y + work.Height), size, work));

        PointInt32? best = null;
        long bestDistance = long.MaxValue;
        foreach (var candidate in candidates)
        {
            if (IsNear(cursor, candidate, size, margin))
            {
                continue;
            }
            long ddx = candidate.X - home.X, ddy = candidate.Y - home.Y;
            long distance = ddx * ddx + ddy * ddy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        if (best is not PointInt32 target)
        {
            return;
        }

        _dodgeHome ??= AppWindow.Position;
        _dodgeTarget = target;
    }

    private static PointInt32 ClampToWorkArea(PointInt32 point, SizeInt32 size, RectInt32 work) => new(
        Math.Clamp(point.X, work.X, Math.Max(work.X, work.X + work.Width - size.Width)),
        Math.Clamp(point.Y, work.Y, Math.Max(work.Y, work.Y + work.Height - size.Height)));

    private void AnimateTowardTarget()
    {
        if (_dodgeTarget is not PointInt32 target)
        {
            return;
        }
        var current = AppWindow.Position;
        int dx = target.X - current.X, dy = target.Y - current.Y;
        if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2)
        {
            AppWindow.Move(target);
            _dodgeTarget = null;
            if (_dodgeHome is PointInt32 home && home.Equals(target))
            {
                _dodgeHome = null;
            }
            return;
        }
        AppWindow.Move(new PointInt32(current.X + (int)Math.Round(dx * DodgeStep), current.Y + (int)Math.Round(dy * DodgeStep)));
    }

    private static bool IsNear(NativeMethods.POINT cursor, PointInt32 position, SizeInt32 size, int margin) =>
        cursor.X >= position.X - margin && cursor.X <= position.X + size.Width + margin &&
        cursor.Y >= position.Y - margin && cursor.Y <= position.Y + size.Height + margin;

    private void MoveAwayItem_Click(object sender, RoutedEventArgs e) => SetMoveAway(MoveAwayItem.IsChecked);

    private void WidgetMenu_Opened(object sender, object e) => _menuOpen = true;

    private void WidgetMenu_Closed(object sender, object e) => _menuOpen = false;
}
