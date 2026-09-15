using System.Runtime.InteropServices;
using System.Text;

namespace Kalender.Interop;

/// <summary>
/// Kullandığımız bütün Win32 çağrıları ve sabitleri tek yerde toplandı.
/// Uygulama yalnızca x64 hedeflediği için işaretçi genişliği 8 bayt kabul edilir.
/// </summary>
internal static class NativeMethods
{
    // ---------------------------------------------------------------- yapılar

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        [MarshalAs(UnmanagedType.Bool)] public bool fErase;
        public RECT rcPaint;
        [MarshalAs(UnmanagedType.Bool)] public bool fRestore;
        [MarshalAs(UnmanagedType.Bool)] public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersionOrTimeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    // ------------------------------------------------------------- temsilciler

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT lprcMonitor, IntPtr dwData);

    /// <summary>Alt düzey fare kancasının geri çağırma imzası.</summary>
    public delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    // ---------------------------------------------------------------- sabitler

    // Pencere mesajları
    public const uint WM_NULL = 0x0000;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_TIMECHANGE = 0x001E;
    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const uint WM_CONTEXTMENU = 0x007B;
    public const uint WM_PRINT = 0x0317;
    public const uint WM_PRINTCLIENT = 0x0318;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCHITTEST = 0x0084;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;

    public const int MA_NOACTIVATE = 3;
    public const int HTTRANSPARENT = -1;

    // Progman penceresine gönderilerek WorkerW üretilmesini sağlayan belgesiz mesaj.
    public const uint WM_SPAWN_WORKERW = 0x052C;

    // Pencere biçemleri
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_DISABLED = 0x08000000;

    public const uint WS_EX_NOACTIVATE = 0x08000000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_APPWINDOW = 0x00040000;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    // SetWindowPos bayrakları
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_NOOWNERZORDER = 0x0200;

    public static readonly IntPtr HWND_BOTTOM = new(1);
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int SW_SHOW = 5;

    public const uint SMTO_NORMAL = 0x0000;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // Sistem ölçüleri
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public const uint MONITORINFOF_PRIMARY = 0x00000001;
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    public const int MDT_EFFECTIVE_DPI = 0;

    public const uint EDD_GET_DEVICE_INTERFACE_NAME = 0x00000001;

    public const uint GA_ROOT = 2;

    // Tepsi simgesi
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;

    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_STATE = 0x00000008;
    public const uint NIF_INFO = 0x00000010;
    public const uint NIF_SHOWTIP = 0x00000080;

    public const uint NOTIFYICON_VERSION_4 = 4;

    public const uint NIIF_NONE = 0x00000000;
    public const uint NIIF_INFO = 0x00000001;
    public const uint NIIF_WARNING = 0x00000002;

    // Tepsi geri çağırma mesajları (NOTIFYICON_VERSION_4)
    public const uint NIN_SELECT = WM_USER + 0;
    public const uint NIN_KEYSELECT = WM_USER + 1;

    // Menü
    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_UNCHECKED = 0x00000000;
    public const uint MF_DISABLED = 0x00000002;
    public const uint MF_GRAYED = 0x00000001;

    public const uint TPM_LEFTALIGN = 0x0000;
    public const uint TPM_RIGHTALIGN = 0x0008;
    public const uint TPM_BOTTOMALIGN = 0x0020;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;

    // Fare tuşu durumu
    public const int VK_LBUTTON = 0x01;
    public const int VK_RBUTTON = 0x02;
    public const int VK_MBUTTON = 0x04;

    // Tam ekran / sunum durumu
    public const int QUNS_NOT_PRESENT = 1;
    public const int QUNS_BUSY = 2;
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    public const int QUNS_PRESENTATION_MODE = 4;
    public const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
    public const int QUNS_QUIET_TIME = 6;
    public const int QUNS_APP = 7;

    // Oturum olayları (uzak masaüstü, kilit açma)
    public const uint NOTIFY_FOR_THIS_SESSION = 0;
    public const int WTS_CONSOLE_CONNECT = 0x1;
    public const int WTS_REMOTE_CONNECT = 0x3;
    public const int WTS_SESSION_LOGON = 0x5;
    public const int WTS_SESSION_UNLOCK = 0x8;

    // Güç olayları
    public const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_APMSUSPEND = 0x0004;

    // SystemParametersInfo
    public const uint SPI_SETDESKWALLPAPER = 0x0014;
    public const uint SPI_GETDESKWALLPAPER = 0x0073;

    // RedrawWindow bayrakları
    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_ERASE = 0x0004;
    public const uint RDW_ALLCHILDREN = 0x0080;
    public const uint RDW_UPDATENOW = 0x0100;
    public const uint RDW_FRAME = 0x0400;

    // ------------------------------------------------------------- user32.dll

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW", SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "UnregisterClassW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT Point);

    /// <summary>Açık olan menüyü kapatır. Menüyü başka bir noktada yeniden açarken gerekiyor.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndMenu();

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    /// <summary>
    /// İki iş parçacığının girdi kuyruğunu birbirine bağlar. Ön plana geçme
    /// kilidini aşmak için gerekiyor: Windows, ön planda olmayan bir sürecin
    /// SetForegroundWindow çağrısını sessizce yok sayıyor.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint attachTo, uint attachFrom, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "AppendMenuW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    public static extern uint TrackPopupMenuEx(IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterWindowMessageW")]
    public static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

    // ---------------------------------------------------------- fare kancası

    /// <summary>Alt düzey fare kancası türü.</summary>
    public const int WH_MOUSE_LL = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    // ------------------------------------------------------------ kernel32.dll

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>
    /// Dosyayı ya da "dosya:akış" biçiminde verilen alternatif veri akışını siler.
    /// .NET'in File.Delete'i akış adındaki iki noktayı yol olarak kabul etmiyor.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "DeleteFileW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteFile(string lpFileName);

    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;

    // -------------------------------------------------------------- shell32.dll

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpdata);

    [DllImport("shell32.dll")]
    public static extern int SHQueryUserNotificationState(out int pquns);

    // ------------------------------------------------------------ wtsapi32.dll

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    // --------------------------------------------------------------- shcore.dll

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // -------------------------------------------------------------- yardımcılar

    public static string GetClassNameOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int n = GetClassName(hWnd, sb, sb.Capacity);
        return n > 0 ? sb.ToString(0, n) : string.Empty;
    }

    /// <summary>Sol, sağ veya orta fare tuşundan biri basılı mı?</summary>
    public static bool IsMouseButtonDown()
    {
        return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
            || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
            || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
    }

    public static int LoWord(IntPtr value) => unchecked((short)(long)value);

    public static int HiWord(IntPtr value) => unchecked((short)((long)value >> 16));
}
