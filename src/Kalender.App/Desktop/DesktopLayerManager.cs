using System.Drawing;
using System.Windows.Threading;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;
using Kalender.Rendering;
using Kalender.Settings;

namespace Kalender.Desktop;

/// <summary>Fare konumunun denk geldiği gün ve onu barındıran ekran.</summary>
internal sealed record DayHit(MonitorTarget Monitor, DayCell Cell, Rectangle CellScreenBounds);

/// <summary>
/// Fare konumunun denk geldiği takvim bloğu. <see cref="Cell"/> yalnızca
/// nokta gün ızgarasının içindeyse doludur; ay adının ya da gün adlarının
/// üzerindeyken null kalır.
/// </summary>
internal sealed record CalendarBlockHit(CalendarLayout Layout, DayCell? Cell);

/// <summary>Bir monitör ve o monitör için üretilmiş görüntü + geometri.</summary>
internal sealed record ScreenSurface(MonitorTarget Monitor, DesktopSurface Surface);

/// <summary>
/// Masaüstündeki takvimin tamamını yönetir: her monitör için zemin ve takvimi
/// tek bir görüntüde birleştirir, bunu <see cref="WallpaperCanvas"/> ile duvar
/// kağıdına yazar ve ekran yapılandırması değiştiğinde her şeyi yeniler.
///
/// Takvimin ekranda hangi piksellerde durduğunu da burası bilir; gün kutusu ve
/// sağ tık menüsü <see cref="HitTest"/> ile <see cref="HitTestBlock"/> üzerinden
/// buraya soruyor.
/// </summary>
internal sealed class DesktopLayerManager : IDisposable
{
    /// <summary>Arka arkaya gelen yeniden kurma isteklerini birleştirmek için bekleme süresi.</summary>
    private static readonly TimeSpan RebuildDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>
    /// Ekran yapılandırmasının sessizce değişmediğinden emin olmak için yapılan
    /// düşük sıklıklı denetim. Windows bazı durumlarda (sürücü yenilenmesi,
    /// uzak masaüstü) bildirim göndermeden çözünürlük değiştirebiliyor.
    /// </summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(8);

    private readonly SettingsStore _settings;
    private readonly DesktopSurfaceRenderer _renderer = new();
    private readonly WallpaperCanvas _canvas = new();
    private readonly List<ScreenSurface> _screens = new();
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _watchdogTimer;

    private IReadOnlyList<MonitorTarget> _monitors = Array.Empty<MonitorTarget>();
    private CalendarDataSnapshot _data = CalendarDataSnapshot.Empty;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly _viewMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateTime _rebuildDeadline = DateTime.MinValue;
    private string _configSignature = string.Empty;
    private bool _unavailableReported;
    private bool _disposed;

    public DesktopLayerManager(SettingsStore settings)
    {
        _settings = settings;

        _rebuildTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RebuildDelay };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            Rebuild();
        };

        _watchdogTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = WatchdogInterval };
        _watchdogTimer.Tick += (_, _) => EnsureAlive();
        _watchdogTimer.Start();
    }

    /// <summary>Takvim duvar kağıdına hiç yazılamadığında bir kez tetiklenir.</summary>
    public event Action? LayerUnavailable;

    /// <summary>Ekran listesi değişti (çözünürlük, ölçek, monitör tak/çıkar).</summary>
    public event Action? MonitorsChanged;

    /// <summary>Şu an takvim çizilen ekranlar var mı?</summary>
    public bool IsActive => _screens.Count > 0;

    /// <summary>Kurulu ekranlar; ayarlar ekranı bu listeyi gösterir.</summary>
    public IReadOnlyList<MonitorTarget> Monitors => _screens.Select(s => s.Monitor).ToList();

    /// <summary>Yeni gün verisi geldiğinde çağrılır.</summary>
    /// <param name="refresh">
    /// Hemen yeniden çizilsin mi? Ardından zaten tam bir yeniden çizim
    /// gelecekse (ayar değişikliği) false verilir; yoksa görüntü iki kez
    /// üretilip duvar kağıdına iki kez yazılır.
    /// </param>
    public void SetData(CalendarDataSnapshot data, bool refresh = true)
    {
        _data = data;

        if (refresh)
        {
            Refresh();
        }
    }

    public void SetToday(DateOnly today)
    {
        _today = today;
        Refresh();
    }

    /// <summary>
    /// Ekranda hangi ayın çizileceğini belirler. Normalde içinde bulunduğumuz
    /// aydır; sağ tık menüsünden ileri geri gidildiğinde geçici olarak değişir.
    /// </summary>
    /// <param name="refresh">
    /// Hemen yeniden çizilsin mi? Yeni ayın verisi hemen ardından
    /// <see cref="SetData"/> ile gelecekse false verilir; yoksa yeni ay önce
    /// eski ayın verisiyle çizilip yazılır, sonra bir kez daha.
    /// </param>
    public void SetViewMonth(DateOnly month, bool refresh = true)
    {
        _viewMonth = new DateOnly(month.Year, month.Month, 1);

        if (refresh)
        {
            Refresh();
        }
    }

    /// <summary>
    /// Her şeyin sıfırdan kurulmasını ister. Kısa aralıkla gelen istekler
    /// birleştirilir; ekran yapılandırması değişimlerinde Windows peş peşe
    /// birkaç bildirim gönderdiği için bu gerekli.
    /// </summary>
    /// <param name="reason">Günlüğe yazılacak sebep.</param>
    /// <param name="delay">Bekleme süresi; verilmezse varsayılan kullanılır.</param>
    public void RequestRebuild(string reason, TimeSpan? delay = null)
    {
        if (_disposed)
        {
            return;
        }

        TimeSpan wait = delay ?? RebuildDelay;
        DateTime deadline = DateTime.UtcNow + wait;

        // Bekleyen bir istek daha ileri bir zamana kuruluysa onu öne çekmiyoruz;
        // yoksa Windows daha oturmadan kurmaya kalkarız.
        if (_rebuildTimer.IsEnabled && _rebuildDeadline > deadline)
        {
            Log.Debug($"Yeniden kurma isteği birleştirildi: {reason}");
            return;
        }

        Log.Debug($"Takvim yeniden kurulacak ({wait.TotalMilliseconds:F0} ms sonra): {reason}");

        _rebuildDeadline = deadline;
        _rebuildTimer.Stop();
        _rebuildTimer.Interval = wait;
        _rebuildTimer.Start();
    }

    /// <summary>
    /// Monitör listesini yeniden okur ve görüntüleri tazeler.
    /// </summary>
    /// <param name="force">
    /// Görüntü aynı olsa bile duvar kağıdına yeniden yazılsın mı? Normalde
    /// hayır: duvar kağıdı yazmak kabuğa "ayar değişti" yayını yaptırıyor, o da
    /// bize geri dönüp yeni bir kurma isteği doğuruyor. İçerik aynıysa yazmayı
    /// atlayarak bu zinciri kendiliğinden söndürüyoruz. Yalnızca kullanıcının
    /// açıkça istediği durumlarda (yeniden çiz, ayar değişikliği) zorluyoruz.
    /// </param>
    public void Rebuild(bool force = false)
    {
        if (_disposed)
        {
            return;
        }

        _rebuildTimer.Stop();

        if (force)
        {
            _canvas.Forget();
        }

        IReadOnlyList<MonitorTarget> monitors = MonitorEnumerator.All();
        if (monitors.Count == 0)
        {
            Log.Warn("Hiç monitör bulunamadı; takvim çizilemiyor.");
            ReportUnavailable();
            return;
        }

        string signature = BuildConfigSignature(monitors);
        bool configChanged = signature != _configSignature;
        _configSignature = signature;
        _monitors = monitors;

        Refresh();

        if (configChanged)
        {
            Log.Info("Ekran yapılandırması: " + signature);
            MonitorsChanged?.Invoke();
        }
    }

    private bool _refreshing;
    private bool _refreshAgain;

    /// <summary>
    /// Görüntüleri yeniden üretir ve değişenleri duvar kağıdına yazar.
    /// Monitör listesi burada yeniden okunmaz.
    ///
    /// İç içe çalışmaz: duvar kağıdı yazılırken COM çağrısı mesaj döngüsünü
    /// döndürebilir ve o arada gelen bir ayar değişikliği (ya da Google
    /// yoklamasının getirdiği yeni veri) buraya yeniden girip kullanılmakta
    /// olan görüntüleri atabilirdi. Arada gelen istek not edilir; iş bitince
    /// en son hâl bir kez daha çizilir.
    /// </summary>
    public void Refresh()
    {
        if (_refreshing)
        {
            _refreshAgain = true;
            return;
        }

        _refreshing = true;
        try
        {
            do
            {
                _refreshAgain = false;
                RefreshOnce();
            }
            while (_refreshAgain && !_disposed);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshOnce()
    {
        if (_disposed || _monitors.Count == 0)
        {
            return;
        }

        AppSettings settings = _settings.Current;
        bool placementsAdded = false;
        var fresh = new List<ScreenSurface>();

        foreach (MonitorTarget monitor in _monitors)
        {
            try
            {
                if (!settings.Monitors.TryGetValue(monitor.StableId, out MonitorPlacement? placement))
                {
                    // Bu monitörü ilk kez görüyoruz; takvimi varsayılan yerine koy.
                    placement = PlacementDefaults.For(monitor);
                    settings.Monitors[monitor.StableId] = placement;
                    placementsAdded = true;
                }

                bool drawCalendar = ShouldDrawCalendar(settings, monitor, placement);

                DesktopSurface surface = _renderer.Render(
                    monitor.Bounds.Size,
                    settings,
                    placement,
                    _data,
                    _today,
                    _viewMonth,
                    drawCalendar);

                fresh.Add(new ScreenSurface(monitor, surface));
            }
            catch (Exception ex)
            {
                Log.Error($"Görüntü üretilemedi: {monitor}", ex);
            }
        }

        if (placementsAdded)
        {
            _settings.Save();
        }

        if (fresh.Count == 0)
        {
            return;
        }

        // Geometri her hâlükârda güncellenir: gün kutusu, takvim duvar kağıdına
        // yazılamasa bile doğru yeri bilmeli.
        List<ScreenSurface> previous = _screens.ToList();
        _screens.Clear();
        _screens.AddRange(fresh);

        if (settings.SyncSystemWallpaper)
        {
            CanvasResult result = _canvas.Apply(
                fresh.Select(s => new CanvasScreen(s.Monitor, s.Surface.Image)).ToList());

            if (result.Success)
            {
                _unavailableReported = false;
            }
            else
            {
                ReportUnavailable();
            }
        }

        foreach (ScreenSurface old in previous)
        {
            old.Surface.Dispose();
        }
    }

    /// <summary>
    /// Ekran yapılandırması hâlâ aynı mı? Değilse her şey yeniden kurulur.
    ///
    /// Düşük sıklıklı zamanlayıcıdan ve uykudan uyanma sonrası çağrılır. Olay
    /// tabanlı bildirimler çoğu durumu yakalıyor ama Windows bazen çözünürlük
    /// değişimini bildirmeden geçiyor; bu denetim o boşluğu kapatıyor.
    /// </summary>
    public bool EnsureAlive()
    {
        if (_disposed)
        {
            return false;
        }

        if (BuildConfigSignature(MonitorEnumerator.All()) != _configSignature)
        {
            Log.Info("Ekran yapılandırması bildirimsiz değişmiş, takvim yeniden kuruluyor.");
            Rebuild();
        }

        return _screens.Count > 0;
    }

    /// <summary>
    /// Ekran koordinatındaki noktanın hangi güne denk geldiğini bulur.
    /// Gün kutusu bunu kullanır.
    /// </summary>
    public DayHit? HitTest(Point screenPoint)
    {
        foreach (ScreenSurface screen in _screens)
        {
            if (!screen.Monitor.Bounds.Contains(screenPoint))
            {
                continue;
            }

            CalendarLayout? layout = screen.Surface.Layout;
            if (layout is null)
            {
                return null;
            }

            DayCell? cell = layout.HitTest(ToLocal(screen.Monitor, screenPoint));
            if (cell is null)
            {
                return null;
            }

            var screenBounds = new Rectangle(
                screen.Monitor.Bounds.X + (int)cell.Bounds.X,
                screen.Monitor.Bounds.Y + (int)cell.Bounds.Y,
                (int)cell.Bounds.Width,
                (int)cell.Bounds.Height);

            return new DayHit(screen.Monitor, cell, screenBounds);
        }

        return null;
    }

    /// <summary>
    /// Ekran koordinatındaki nokta takvim bloğunun (ay adı, gün adları ve
    /// ızgara) üzerinde mi? Sağ tık menüsü bunu kullanır: menü blok üzerinde
    /// açılır, masaüstünün geri kalanı Windows'a bırakılır.
    /// </summary>
    public CalendarBlockHit? HitTestBlock(Point screenPoint)
    {
        foreach (ScreenSurface screen in _screens)
        {
            if (!screen.Monitor.Bounds.Contains(screenPoint))
            {
                continue;
            }

            CalendarLayout? layout = screen.Surface.Layout;
            if (layout is null)
            {
                return null;
            }

            PointF local = ToLocal(screen.Monitor, screenPoint);
            if (!layout.Bounds.Contains(local))
            {
                return null;
            }

            return new CalendarBlockHit(layout, layout.HitTest(local));
        }

        return null;
    }

    /// <summary>Ekran koordinatındaki noktayı monitörün kendi koordinatına çevirir.</summary>
    private static PointF ToLocal(MonitorTarget monitor, Point screenPoint)
        => new(screenPoint.X - monitor.Bounds.X, screenPoint.Y - monitor.Bounds.Y);

    /// <summary>
    /// Ekranların kimliği, yeri, ölçüsü ve DPI'sının özeti. Değiştiğinde
    /// görüntüler ve ayarlar ekranı tazelenmelidir.
    /// </summary>
    private static string BuildConfigSignature(IReadOnlyList<MonitorTarget> monitors)
        => string.Join(";", monitors.Select(m =>
            $"{m.StableId}@{m.Bounds.X},{m.Bounds.Y},{m.Bounds.Width}x{m.Bounds.Height}#{m.Dpi}"));

    private static bool ShouldDrawCalendar(AppSettings settings, MonitorTarget monitor, MonitorPlacement placement)
    {
        if (settings.CalendarHidden)
        {
            return false;
        }

        return settings.Appearance.DisplayTarget switch
        {
            DisplayTarget.PrimaryOnly => monitor.IsPrimary,
            DisplayTarget.SelectedScreens => placement.Enabled,
            _ => true,
        };
    }

    private void ReportUnavailable()
    {
        if (_unavailableReported)
        {
            return;
        }

        _unavailableReported = true;
        LayerUnavailable?.Invoke();
    }

    /// <summary>
    /// Takvimsiz zemini duvar kağıdına yazar. Kapanışta çağrılıyor: takvim
    /// duvar kağıdının parçası olduğu için, uygulama kapandıktan sonra ekranda
    /// donmuş bir takvim kalmasın.
    /// </summary>
    private void PaintPlainBackground()
    {
        try
        {
            AppSettings settings = _settings.Current;
            var plain = new List<ScreenSurface>();

            foreach (ScreenSurface screen in _screens)
            {
                if (!settings.Monitors.TryGetValue(screen.Monitor.StableId, out MonitorPlacement? placement))
                {
                    placement = PlacementDefaults.For(screen.Monitor);
                }

                DesktopSurface surface = _renderer.Render(
                    screen.Monitor.Bounds.Size, settings, placement,
                    CalendarDataSnapshot.Empty, _today, _viewMonth, drawCalendar: false);

                plain.Add(new ScreenSurface(screen.Monitor, surface));
            }

            _canvas.Apply(plain.Select(s => new CanvasScreen(s.Monitor, s.Surface.Image)).ToList());

            foreach (ScreenSurface s in plain)
            {
                s.Surface.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Kapanışta zemin yazılamadı.", ex);
        }
    }

    private void DisposeSurfaces()
    {
        foreach (ScreenSurface screen in _screens)
        {
            screen.Surface.Dispose();
        }

        _screens.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        _rebuildTimer.Stop();
        _watchdogTimer.Stop();

        // Takvimi duvar kağıdından siliyoruz; yoksa yarın da bugünün takvimi
        // ekranda asılı kalır.
        if (_canvas.HasPainted && _settings.Current.SyncSystemWallpaper)
        {
            PaintPlainBackground();
        }

        DisposeSurfaces();
        _renderer.Dispose();
    }
}
