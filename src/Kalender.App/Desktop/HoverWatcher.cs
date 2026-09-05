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
        "KalenderMasaustuKatmani",
    };

    private readonly DesktopLayerManager _layer;
    private readonly Func<CalendarDataSnapshot> _data;
    private readonly DispatcherTimer _timer;

    private DateOnly? _hoveredDay;
    private Rectangle _hoveredCell;
    private DateTime _enteredAt;
    private DateTime _leftAt = DateTime.MaxValue;
    private bool _shown;

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

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        Reset();
    }

    /// <summary>Veri ya da katman değiştiğinde açık kutu geçersizdir.</summary>
    public void Reset()
    {
        _hoveredDay = null;
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
            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            {
                return;
            }

            var point = new Point(cursor.X, cursor.Y);

            if (!PointerIsOnDesktop(cursor)
                || ForegroundCovers(point)
                || NativeMethods.IsMouseButtonDown()
                || !NotificationsAllowed())
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

    /// <summary>İmlecin altındaki pencere masaüstü zincirinden mi?</summary>
    private static bool PointerIsOnDesktop(NativeMethods.POINT cursor)
    {
        IntPtr under = NativeMethods.WindowFromPoint(cursor);
        if (under == IntPtr.Zero)
        {
            return false;
        }

        return DesktopClasses.Contains(NativeMethods.GetClassNameOf(under), StringComparer.Ordinal);
    }

    /// <summary>Tam ekran, sunum ya da oyun kipinde kutu açılmaz.</summary>
    private static bool NotificationsAllowed()
    {
        try
        {
            if (NativeMethods.SHQueryUserNotificationState(out int state) != 0)
            {
                return true;
            }

            return state is NativeMethods.QUNS_ACCEPTS_NOTIFICATIONS or NativeMethods.QUNS_QUIET_TIME;
        }
        catch
        {
            return true;
        }
    }

    public void Dispose() => _timer.Stop();
}
