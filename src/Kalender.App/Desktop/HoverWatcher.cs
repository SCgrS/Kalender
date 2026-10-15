using System.Drawing;
using System.Windows.Threading;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Farenin masaüstündeki takvim günleri üzerinde durup durmadığını izler ve
/// gün kutusunun açılıp kapanmasına karar verir.
///
/// Neden global fare kancası değil de yoklama?
/// Global bir WH_MOUSE_LL kancası, sistemdeki her fare hareketinde bizim
/// sürecimizi uyandırır; ayrıca kancada oluşacak en küçük gecikme bütün
/// sistemin fare girdisini yavaşlatır. 100 ms'de bir GetCursorPos çağırmak
/// hem daha az uyanma hem de sıfır kararlılık riski demek. Üstelik her turda
/// önce ön plandaki pencereye bakılıyor; masaüstü önde değilse tur birkaç
/// mikrosaniyede bitiyor.
/// </summary>
internal sealed class HoverWatcher : IDisposable
{
    /// <summary>Yoklama sıklığı.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>Kutunun açılması için gereken bekleme.</summary>
    private static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Hücreden çıktıktan sonra kapanma gecikmesi.</summary>
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>Masaüstü zincirindeki pencere sınıfları.</summary>
    private static readonly string[] DesktopClasses =
    {
        "Progman",
        "WorkerW",
        "SHELLDLL_DefView",
        "SysListView32",
    };

    /// <summary>
    /// Masaüstünün kök pencereleri. Zincirin alt sınıfları masaüstüne özgü
    /// değil: Kayıt Defteri, Hizmetler, 7-Zip gibi programların listeleri de
    /// SysListView32.
    /// </summary>
    private static readonly string[] DesktopRootClasses =
    {
        "Progman",
        "WorkerW",
    };

    private readonly DesktopLayerManager _layer;
    private readonly Func<CalendarDataSnapshot> _data;
    private readonly DispatcherTimer _timer;

    private DateOnly? _hoveredDay;
    private Rectangle _hoveredCell;
    private DateTime _enteredAt;
    private DateTime _leftAt = DateTime.MaxValue;
    private bool _shown;
    private bool _overCalendar;

    /// <summary>İmlecin bir önceki turdaki yeri; takvimde gezinmeyi anlamak için.</summary>
    private Point _lastPoint;

    /// <summary><see cref="_lastPoint"/>'in okunduğu an (<see cref="Environment.TickCount64"/>).</summary>
    private long _lastPointAt;

    /// <summary>Üstünde masaüstü simgesi bulunan, kutusu bu yüzden açılmayan gün.</summary>
    private DateOnly? _simgeliGun;

    public HoverWatcher(DesktopLayerManager layer, Func<CalendarDataSnapshot> data)
    {
        _layer = layer;
        _data = data;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = PollInterval };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Kutu açılmalı: (gün içeriği, hücrenin ekrandaki yeri).</summary>
    public event Action<DayContent, Rectangle>? ShowRequested;

    /// <summary>Kutu kapanmalı.</summary>
    public event Action? HideRequested;

    /// <summary>
    /// İmleç masaüstünde takvim bloğunun üzerine girdi (true) ya da çıktı (false).
    /// Fare kancası (sağ tık menüsü, ay düğmeleri) yalnızca "true" olduğu sürece
    /// takılı durur; masaüstünün geri kalanında sistemin fare girdisine hiç dokunulmaz.
    /// </summary>
    public event Action<bool>? CalendarHoverChanged;

    /// <summary>
    /// İmleç takvim bloğunun üzerinde hareket etti. Başka bir aya gidilmişken
    /// bu aya dönüş sayacı buna göre baştan başlar: gün kutusunu okuyan
    /// kullanıcının önünden ay kaçmasın.
    /// </summary>
    public event Action? CalendarActivity;

    /// <summary>
    /// İmleç bir önceki turdan bu yana yer değiştirdi (takvimin üstünde olsun
    /// olmasın). Değer, önceki turda imlecin okunduğu an
    /// (<see cref="Environment.TickCount64"/>). Fare kancası takılıysa bu hareketi
    /// görmüş olmalıydı; görmediyse Windows onu sessizce sökmüştür
    /// (bkz. CalendarMouseHook.Sokulmus).
    /// </summary>
    public event Action<long>? ImlecOynadi;

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        Reset();
        SetCalendarHover(false);
    }

    /// <summary>İmlecin takvim bloğu üzerinde olma durumu değiştiyse haber verir.</summary>
    private void SetCalendarHover(bool inside)
    {
        if (inside == _overCalendar)
        {
            return;
        }

        _overCalendar = inside;
        CalendarHoverChanged?.Invoke(inside);
    }

    /// <summary>Veri ya da katman değiştiğinde açık kutu geçersizdir.</summary>
    public void Reset()
    {
        _hoveredDay = null;
        _simgeliGun = null;
        if (_shown)
        {
            _shown = false;
            HideRequested?.Invoke();
        }
    }

    private void Tick()
    {
        try
        {
            long okuma = Environment.TickCount64;
            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            {
                return;
            }

            var point = new Point(cursor.X, cursor.Y);

            bool masaustuGorunuyor = PointerIsOnDesktop(cursor)
                && !ForegroundCovers(point)
                && NotificationsAllowed(cursor);

            // Fare kancası tuş basılıyken de takılı kalmalı: karar basma
            // anında verilir, bırakma anında uygulanır. Bu yüzden blok
            // denetimi tuş denetiminden önce yapılıyor. İmleç tuş basılıyken
            // takvimden çıkarsa kanca, yuttuğu basışın bırakışını alana dek
            // yine takılı kalır (AppHost.UpdateMouseHook).
            bool takvimde = masaustuGorunuyor && _layer.HitTestBlock(point) is not null;
            SetCalendarHover(takvimde);

            if (takvimde && point != _lastPoint)
            {
                CalendarActivity?.Invoke();
            }

            if (point != _lastPoint)
            {
                ImlecOynadi?.Invoke(_lastPointAt);
            }

            _lastPoint = point;
            _lastPointAt = okuma;

            if (!masaustuGorunuyor || NativeMethods.IsMouseButtonDown())
            {
                Reset();
                return;
            }

            DayHit? hit = _layer.HitTest(point);

            if (hit is null)
            {
                Leave();
                return;
            }

            if (_hoveredDay != hit.Cell.Date)
            {
                // Başka bir güne geçildi: sayaç sıfırlanır, açık kutu kapanır.
                _hoveredDay = hit.Cell.Date;
                _simgeliGun = null;
                _hoveredCell = hit.CellScreenBounds;
                _enteredAt = DateTime.UtcNow;
                _leftAt = DateTime.MaxValue;

                if (_shown)
                {
                    _shown = false;
                    HideRequested?.Invoke();
                }

                return;
            }

            _leftAt = DateTime.MaxValue;

            if (_shown || DateTime.UtcNow - _enteredAt < OpenDelay)
            {
                return;
            }

            DayContent? content = _data().For(hit.Cell.Date);
            if (content is null || !content.HasAnything)
            {
                // O günde gösterecek bir şey yoksa boş kutu açmıyoruz.
                return;
            }

            // Günün üstünde bir masaüstü simgesi duruyorsa imleç simgededir;
            // kutu simgenin üstüne açılmasın. Soru kutu açılacağı an, her gün
            // için bir kez sorulur; imleç başka güne geçince yeniden.
            if (_simgeliGun == hit.Cell.Date)
            {
                return;
            }

            if (MasaustuSimgeleri.Ustunde(point))
            {
                _simgeliGun = hit.Cell.Date;
                return;
            }

            _shown = true;
            ShowRequested?.Invoke(content, hit.CellScreenBounds);
        }
        catch (Exception ex)
        {
            Log.Warn("Gün kutusu izleyicisinde hata.", ex);
            Reset();
        }
    }

    private void Leave()
    {
        _hoveredDay = null;

        if (!_shown)
        {
            return;
        }

        if (_leftAt == DateTime.MaxValue)
        {
            _leftAt = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - _leftAt >= CloseDelay)
        {
            _shown = false;
            _leftAt = DateTime.MaxValue;
            HideRequested?.Invoke();
        }
    }

    /// <summary>
    /// Ön plandaki pencere imlecin bulunduğu noktayı örtüyor mu?
    ///
    /// Kural şu: üstte Word, Chrome gibi bir pencere varken kutu açılmamalı.
    /// Bunu "ön plandaki pencere masaüstünün kendisi mi" diye sormak yanlış
    /// sonuç veriyor: Windows, bir pencere simge durumuna küçültüldükten sonra
    /// bile onu ön plan saymaya devam edebiliyor; o durumda masaüstü apaçık
    /// önümüzdeyken kutu hiç açılmıyordu.
    ///
    /// Doğru soru, o noktada gerçekten bir pencere olup olmadığı. Bunu zaten
    /// WindowFromPoint yanıtlıyor; burası yalnızca ek bir güvence: ön plandaki
    /// pencere görünür durumdaysa ve imleci kapsıyorsa kutu açılmaz.
    /// </summary>
    private static bool ForegroundCovers(Point point)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        if (DesktopClasses.Contains(NativeMethods.GetClassNameOf(foreground), StringComparer.Ordinal))
        {
            return false;
        }

        if (!NativeMethods.IsWindowVisible(foreground) || NativeMethods.IsIconic(foreground))
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(foreground, out NativeMethods.RECT rect))
        {
            return false;
        }

        return point.X >= rect.Left && point.X < rect.Right
            && point.Y >= rect.Top && point.Y < rect.Bottom;
    }

    /// <summary>
    /// Ekran koordinatındaki noktada masaüstünün kendisi mi duruyor? Sağ tık
    /// kancası da buna bakıyor: takvimin üstünde ama bir pencerenin (örneğin
    /// açık menünün) altında kalan noktalarda tıklamayı yutmamalıyız.
    /// </summary>
    internal static bool IsOnDesktopSurface(Point point)
        => PointerIsOnDesktop(new NativeMethods.POINT { X = point.X, Y = point.Y });

    /// <summary>
    /// İmlecin altındaki pencere masaüstü zincirinden mi? Sınıf adı yetmez,
    /// pencerenin kökü de masaüstü olmalı: takvimin üstünde duran ama ön planda
    /// olmayan bir programın listesine sağ tık Kalender'in menüsünü açıyor, ok
    /// yerine denk gelen sol tık ayı değiştiriyordu.
    /// </summary>
    private static bool PointerIsOnDesktop(NativeMethods.POINT cursor)
    {
        IntPtr under = NativeMethods.WindowFromPoint(cursor);
        if (under == IntPtr.Zero
            || !DesktopClasses.Contains(NativeMethods.GetClassNameOf(under), StringComparer.Ordinal))
        {
            return false;
        }

        IntPtr root = NativeMethods.GetAncestor(under, NativeMethods.GA_ROOT);
        return root != IntPtr.Zero
            && DesktopRootClasses.Contains(NativeMethods.GetClassNameOf(root), StringComparer.Ordinal);
    }

    /// <summary>
    /// Tam ekran, sunum ya da oyun kipinde kutu açılmaz; ama yalnız ön plandaki
    /// pencere imlecin bulunduğu ekrandaysa. Windows bu durumu ekran ayırmadan
    /// bildiriyor: öbür ekranda tam ekran video oynarken takvimin ekranındaki
    /// ilk sağ tık Windows'un menüsünü açıyor, ay okları ilk tıklamada
    /// çalışmıyordu.
    /// </summary>
    private static bool NotificationsAllowed(NativeMethods.POINT cursor)
    {
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out int state) != 0)
            {
                return true;
            }

            return state is NativeMethods.QUNS_ACCEPTS_NOTIFICATIONS or NativeMethods.QUNS_QUIET_TIME
                || !ImlecinEkraninda(NativeMethods.GetForegroundWindow(), cursor);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Pencere imlecin bulunduğu ekranda mı? Hiçbir ekranda durmayan pencere
    /// (ya da pencerenin olmaması) hayır demek.
    /// </summary>
    internal static bool ImlecinEkraninda(IntPtr pencere, NativeMethods.POINT imlec)
    {
        if (pencere == IntPtr.Zero)
        {
            return false;
        }

        IntPtr ekran = NativeMethods.MonitorFromWindow(pencere, NativeMethods.MONITOR_DEFAULTTONULL);
        return ekran != IntPtr.Zero
            && ekran == NativeMethods.MonitorFromPoint(imlec, NativeMethods.MONITOR_DEFAULTTONEAREST);
    }

    public void Dispose()
    {
        _timer.Stop();
        SetCalendarHover(false);
    }
}
