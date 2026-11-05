using System.Runtime.InteropServices;
using static Prism.NativeMethods;

namespace Prism;

/// <summary>A menu entry for the tray menu. A <c>null</c> entry renders as a separator.</summary>
internal sealed record TrayMenuItem(string Text, bool Checked, Action OnClick);

/// <summary>Notification-area icon that routes its callbacks through the owner window's message loop.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WM_APP + 1;
    private static readonly UIntPtr SubclassId = 1;

    private readonly IntPtr _hwnd;
    private readonly string _tooltip;
    private readonly SubclassProc _subclassProc;
    private readonly uint _taskbarCreatedMessage;
    private bool _disposed;

    public TrayIcon(IntPtr hwnd, string tooltip)
    {
        _hwnd = hwnd;
        _tooltip = tooltip;
        _subclassProc = WndProc;
        _taskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");
        SetWindowSubclass(_hwnd, _subclassProc, SubclassId, UIntPtr.Zero);
        AddIcon();
    }

    public Action? LeftClick { get; init; }

    public Func<IReadOnlyList<TrayMenuItem?>>? MenuProvider { get; init; }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = CallbackMessage,
        hIcon = LoadIconW(IntPtr.Zero, IDI_APPLICATION),
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private void AddIcon()
    {
        var data = CreateData();
        Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr idSubclass, UIntPtr refData)
    {
        if (msg == CallbackMessage)
        {
            uint mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            if (mouseMessage == WM_LBUTTONUP)
            {
                LeftClick?.Invoke();
            }
            else if (mouseMessage == WM_RBUTTONUP)
            {
                ShowMenu();
            }
            return IntPtr.Zero;
        }

        if (msg == _taskbarCreatedMessage)
        {
            AddIcon();
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0)
        {
            return;
        }

        IntPtr menu = CreatePopupMenu();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is TrayMenuItem item)
            {
                AppendMenuW(menu, MF_STRING | (item.Checked ? MF_CHECKED : 0), (UIntPtr)(uint)(i + 1), item.Text);
            }
            else
            {
                AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            }
        }

        GetCursorPos(out var cursor);
        SetForegroundWindow(_hwnd);
        int command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, cursor.X, cursor.Y, _hwnd, IntPtr.Zero);
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);

        if (command > 0 && items[command - 1] is TrayMenuItem selected)
        {
            selected.OnClick();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        var data = CreateData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        RemoveWindowSubclass(_hwnd, _subclassProc, SubclassId);
    }
}
