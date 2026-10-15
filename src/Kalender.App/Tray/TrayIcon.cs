using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Tray;

/// <summary>
/// Bildirim alanı (tepsi) simgesi. Üçüncü parti bir kütüphane yerine doğrudan
/// Shell_NotifyIcon kullanılıyor; hem bağımlılık eklemiyor hem de Explorer
/// yeniden başladığında simgeyi geri koymak gibi ayrıntıları elimizde tutuyor.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;

    private readonly IntPtr _hwnd;
    private Icon? _icon;
    private bool _added;
    private bool _menuOpen;
    private string _tooltip = "Kalender";

    public TrayIcon(AppMessageWindow host)
    {
        _hwnd = host.Handle;
        host.TrayEvent += OnTrayEvent;
        host.ShellRestarted += Readd;
    }

    /// <summary>Menü açılmadan hemen önce çağrılır; güncel satırları vermelidir.</summary>
    public Func<IReadOnlyList<PopupMenuItem>>? MenuBuilder { get; set; }

    /// <summary>Menüden bir satır seçildi.</summary>
    public event Action<uint>? MenuItemClicked;

    /// <summary>Simgeye sol tıklandı.</summary>
    public event Action? Clicked;

    /// <summary>
    /// Menü kapandı (seçilen satır bundan sonra bildirilir). Menü açıkken
    /// takvime tıklandıysa menü bu yüzden kapatılmıştır; tıklama şimdi işlenir.
    /// </summary>
    public event Action? MenuKapandi;

    public void Show(string tooltip)
    {
        _tooltip = tooltip;
        _icon ??= LoadIcon();

        var data = BuildData(NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP
            | NativeMethods.NIF_SHOWTIP);

        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            Log.Warn("Tepsi simgesi eklenemedi.");
            return;
        }

        _added = true;

        var version = BuildData(0);
        version.uVersionOrTimeout = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_SETVERSION, ref version);
    }

    public void UpdateTooltip(string tooltip)
    {
        _tooltip = tooltip;
        if (!_added)
        {
            return;
        }

        var data = BuildData(NativeMethods.NIF_TIP | NativeMethods.NIF_SHOWTIP);
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    /// <summary>Tek seferlik bilgi balonu. Yalnızca gerçekten gerektiğinde kullanılır.</summary>
    public void ShowBalloon(string title, string message, bool warning = false)
    {
        if (!_added)
        {
            return;
        }

        var data = BuildData(NativeMethods.NIF_INFO);
        data.szInfoTitle = Kirp(title, 63);
        data.szInfo = Kirp(message, 255);
        data.dwInfoFlags = warning ? NativeMethods.NIIF_WARNING : NativeMethods.NIIF_INFO;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private void Readd()
    {
        _added = false;
        Show(_tooltip);
    }

    /// <summary>
    /// Tepsi olayları. NOTIFYICON_VERSION_4 ile kabuk, sağ tık için hem
    /// WM_RBUTTONUP'ı hem de onun ardından WM_CONTEXTMENU'yü gönderir; sol tık
    /// için de WM_LBUTTONUP'ın ardından NIN_SELECT gelir. İkisini birden
    /// dinlemek menüyü iki kez açmaya kalkıyordu: ikinci çağrı açık menüleri
    /// kapattığı için birincisi kapanıyor, ikincisi de "zaten açık menü var"
    /// diye reddediliyordu; kullanıcı sağ tıkladığında hiçbir şey olmuyordu.
    /// Bu yüzden yalnızca sürüm 4'ün kendi olayları dinleniyor. WM_CONTEXTMENU
    /// klavyeden (Shift+F10, menü tuşu) de gelir, NIN_KEYSELECT ise Enter ve
    /// Boşluk'tan.
    /// </summary>
    private void OnTrayEvent(uint message, int x, int y)
    {
        switch (message)
        {
            case NativeMethods.WM_CONTEXTMENU:
                ShowMenu(x, y);
                break;

            case NativeMethods.NIN_SELECT:
            case NativeMethods.NIN_KEYSELECT:
                Clicked?.Invoke();
                break;
        }
    }

    private void ShowMenu(int x, int y)
    {
        // Menü açıkken gelen ikinci istek yok sayılır; TrackPopupMenu zaten iç
        // içe çalışmaz ve ikinci deneme açık olanı kapatırdı.
        if (_menuOpen)
        {
            return;
        }

        IReadOnlyList<PopupMenuItem>? items = MenuBuilder?.Invoke();
        if (items is null || items.Count == 0)
        {
            return;
        }

        uint secim;
        _menuOpen = true;
        try
        {
            secim = PopupMenu.Show(_hwnd, x, y, items);
        }
        finally
        {
            _menuOpen = false;
        }

        MenuKapandi?.Invoke();

        if (secim != 0)
        {
            MenuItemClicked?.Invoke(secim);
        }
    }

    private NativeMethods.NOTIFYICONDATA BuildData(uint flags)
    {
        return new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = IconId,
            uFlags = flags,
            uCallbackMessage = AppMessageWindow.TrayCallbackMessage,
            hIcon = _icon?.Handle ?? IntPtr.Zero,
            szTip = Kirp(_tooltip, 127),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
        };
    }

    private static string Kirp(string value, int max)
        => value.Length <= max ? value : value[..max];

    private static Icon? LoadIcon()
    {
        try
        {
            int size = Math.Max(16, NativeMethods.GetSystemMetrics(49)); // SM_CXSMICON
            Assembly asm = Assembly.GetExecutingAssembly();
            using Stream? stream = asm.GetManifestResourceStream("Kalender.Assets.Kalender.ico");
            if (stream is not null)
            {
                return new Icon(stream, new Size(size, size));
            }

            Log.Warn("Gömülü simge bulunamadı.");
        }
        catch (Exception ex)
        {
            Log.Warn("Tepsi simgesi yüklenemedi.", ex);
        }

        return null;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = BuildData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);
            _added = false;
        }

        _icon?.Dispose();
        _icon = null;
    }
}
