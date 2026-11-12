using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace Prism;

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
        var position = AppWindow.Position;

        if (IsNear(cursor, position, size, margin))
        {
            if (!_dodgeEngaged && !busy)
            {
                if (cursor.X >= position.X + size.Width - (int)(GrabSideDip * scale))
                {
                    _dodgeEngaged = true;
                }
                else
                {
                    DodgeFrom(cursor, _dodgeTarget ?? position, size, margin);
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
                _dodgeTarget = home;
                _dodgeHome = null;
                _awayFromHomeSince = null;
            }
        }
        else
        {
            _awayFromHomeSince = null;
        }
    }

    /// <summary>Slides by the widget's height (down, or up if there's no room), sideways as a last resort.</summary>
    private void DodgeFrom(NativeMethods.POINT cursor, PointInt32 from, SizeInt32 size, int margin)
    {
        var work = DisplayArea.GetFromPoint(new PointInt32(from.X + size.Width / 2, from.Y + size.Height / 2),
            DisplayAreaFallback.Nearest).WorkArea;
        int gap = margin * 2 + 8;
        int down = from.Y + size.Height + gap;
        int up = from.Y - size.Height - gap;
        bool downFits = down + size.Height <= work.Y + work.Height;
        bool upFits = up >= work.Y;
        bool preferDown = cursor.Y < from.Y + size.Height / 2;

        PointInt32 target;
        if ((preferDown && downFits) || (!upFits && downFits))
        {
            target = new PointInt32(from.X, down);
        }
        else if (upFits)
        {
            target = new PointInt32(from.X, up);
        }
        else
        {
            int left = from.X - size.Width - gap;
            int right = from.X + size.Width + gap;
            int x = left >= work.X ? left : Math.Min(right, work.X + work.Width - size.Width);
            target = new PointInt32(x, from.Y);
        }

        _dodgeHome ??= AppWindow.Position;
        _dodgeTarget = target;
    }

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
