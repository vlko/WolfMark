using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// Notification-area icon (Shell_NotifyIcon) with the standard menu: show, settings, exit. Messages arrive
/// through a subclass of the main window, which keeps receiving them while the window is hidden.
/// Left click shows the window; right click (or the keyboard context menu) opens the menu.
/// The menu is a native popup menu: a WinUI flyout needs a visible XAML window to anchor to.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const nuint SubclassId = 0x574D; // "WM"
    private const uint CmdShow = 1, CmdSettings = 2, CmdExit = 3;
    private const uint NinSelect = 0x0400; // WM_USER
    private const uint NinKeySelect = NinSelect | 1;

    private static TrayIcon? s_current;
    private readonly HWND _hwnd;
    private readonly uint _taskbarCreated;
    private readonly uint _callbackMessage;
    private bool _version4;
    private HICON _icon;
    private string _tooltip;
    private bool _added;

    public TrayIcon(nint hwnd, string iconPath, string tooltip)
    {
        _hwnd = new HWND(hwnd);
        _tooltip = tooltip;
        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        _callbackMessage = PInvoke.RegisterWindowMessage("WolfMark.TrayCallback"); // not a WM_APP value WinUI might use
        s_current = this;
        PInvoke.SetWindowSubclass(_hwnd, &SubclassProc, SubclassId, 0);
        SetIcon(iconPath);
    }

    /// <summary>Menu texts, resolved when the menu opens (language can change).</summary>
    public required Func<(string Show, string Settings, string Exit)> MenuTexts { get; init; }

    public event EventHandler? ShowRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? ExitRequested;

    public void SetIcon(string iconPath)
    {
        var size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        HANDLE icon;
        fixed (char* path = iconPath)
        {
            icon = PInvoke.LoadImage(HINSTANCE.Null, path, GDI_IMAGE_TYPE.IMAGE_ICON, size, size, IMAGE_FLAGS.LR_LOADFROMFILE);
        }

        var old = _icon;
        _icon = new HICON(icon.Value);
        Update();
        if (!old.IsNull)
        {
            PInvoke.DestroyIcon(old);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            _added = false;
        }

        PInvoke.RemoveWindowSubclass(_hwnd, &SubclassProc, SubclassId);
        if (!_icon.IsNull)
        {
            PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
        if (s_current == this)
        {
            s_current = null;
        }
    }

    private NOTIFYICONDATAW NewData()
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = IconId,
        };
        return data;
    }

    private void Update()
    {
        var data = NewData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = _callbackMessage;
        if (!_icon.IsNull)
        {
            data.uFlags |= NOTIFY_ICON_DATA_FLAGS.NIF_ICON;
            data.hIcon = _icon;
        }

        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(MemoryMarshal.CreateSpan(ref data.szTip[0], 128));
        data.szTip[tip.Length] = '\0';

        if (_added)
        {
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
            return;
        }

        _added = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
        if (_added)
        {
            // Version 4: NIN_SELECT / WM_CONTEXTMENU in the low word of lParam.
            data.Anonymous.uVersion = 4; // NOTIFYICON_VERSION_4
            _version4 = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT SubclassProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam, nuint id, nuint refData)
    {
        if (s_current is { } tray)
        {
            if (msg == tray._callbackMessage)
            {
                tray.OnCallback((uint)(lParam.Value & 0xFFFF));
                return new LRESULT(0);
            }

            if (msg == tray._taskbarCreated)
            {
                // Explorer restarted: the icon is gone and has to be added again.
                tray._added = false;
                tray.Update();
            }
        }

        return PInvoke.DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void OnCallback(uint evt)
    {
        // Version 4 sends NIN_SELECT / WM_CONTEXTMENU in addition to the raw mouse messages – handle one of
        // each pair, otherwise the menu opens twice.
        var show = _version4 ? evt is NinSelect or NinKeySelect : evt == PInvoke.WM_LBUTTONUP;
        var menu = _version4 ? evt == PInvoke.WM_CONTEXTMENU : evt == PInvoke.WM_RBUTTONUP;
        if (show)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (menu)
        {
            ShowMenu();
        }
    }

    private void ShowMenu()
    {
        var (show, settings, exit) = MenuTexts();
        using var menu = PInvoke.CreatePopupMenu_SafeHandle();
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, CmdShow, show);
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, CmdSettings, settings);
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, (string?)null);
        PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, CmdExit, exit);
        PInvoke.GetCursorPos(out var pt);

        // Without the foreground switch the menu does not close when clicking elsewhere (KB135788).
        PInvoke.SetForegroundWindow(_hwnd);
        var cmd = PInvoke.TrackPopupMenuEx(menu, (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN), pt.X, pt.Y, _hwnd, null);
        PInvoke.PostMessage(_hwnd, PInvoke.WM_NULL, default, default);
        switch ((uint)cmd.Value)
        {
            case CmdShow:
                ShowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CmdSettings:
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case CmdExit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }
}
