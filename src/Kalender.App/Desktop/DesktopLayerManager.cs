using System.Drawing;
using System.Windows.Threading;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;
using Kalender.Rendering;
using Kalender.Settings;

namespace Kalender.Desktop;

/// <summary>Fare konumunun denk geldiği gün ve onu barındıran ekran.</summary>
internal sealed record DayHit(DesktopWindow Window, DayCell Cell, Rectangle CellScreenBounds);

/// <summary>
/// Fare konumunun denk geldiği takvim bloğu. <see cref="Cell"/> yalnızca
/// nokta gün ızgarasının içindeyse doludur; ay adının ya da gün adlarının
/// üzerindeyken null kalır.
/// </summary>
internal sealed record CalendarBlockHit(CalendarLayout Layout, DayCell? Cell);

/// <summary>
/// Masaüstü katmanının tamamını yönetir: WorkerW'yi bulur, her monitör için
/// bir çizim penceresi kurar, görüntüleri üretir ve ekran yapılandırması
/// değiştiğinde her şeyi yeniden kurar.
/// </summary>
internal sealed class DesktopLayerManager : IDisposable
{
    /// <summary>Arka arkaya gelen yeniden kurma isteklerini birleştirmek için bekleme süresi.</summary>
    private static readonly TimeSpan RebuildDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>Kabuk inatla katmanı yıkıyorsa geçilen yavaş deneme aralığı.</summary>
    private static readonly TimeSpan BackoffDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Ekran yapılandırmasının sessizce değişmediğinden emin olmak için yapılan
    /// düşük sıklıklı denetim. Windows bazı durumlarda (sürücü yenilenmesi,
    /// uzak masaüstü) bildirim göndermeden çözünürlük değiştirebiliyor.
    /// </summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromSeconds(8);

    private readonly SettingsStore _settings;
    private readonly DesktopSurfaceRenderer _renderer = new();
    private readonly List<(MonitorTarget Monitor, DesktopWindow Window)> _screens = new();
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _watchdogTimer;

    private DesktopHost? _host;
    private Point _hostOrigin;
    private CalendarDataSnapshot _data = CalendarDataSnapshot.Empty;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly _viewMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateTime _rebuildWindowStart = DateTime.MinValue;
    private DateTime _rebuildDeadline = DateTime.MinValue;
    private int _rebuildsInWindow;
    private string _configSignature = string.Empty;
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

    /// <summary>Masaüstü katmanı hiç kurulamadığında bir kez tetiklenir.</summary>
    public event Action? LayerUnavailable;

    /// <summary>Ekran listesi değişti (çözünürlük, ölçek, monitör tak/çıkar).</summary>
    public event Action? MonitorsChanged;

    /// <summary>Şu an takvim çizen pencereler var mı?</summary>
    public bool IsActive => _screens.Count > 0;

    /// <summary>Kurulu ekranlar; ayarlar ekranı bu listeyi gösterir.</summary>
    public IReadOnlyList<MonitorTarget> Monitors => _screens.Select(s => s.Monitor).ToList();

    /// <summary>Yeni gün verisi geldiğinde çağrılır.</summary>
    public void SetData(CalendarDataSnapshot data)
    {
        _data = data;
        Refresh();
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
    public void SetViewMonth(DateOnly month)
    {
        _viewMonth = new DateOnly(month.Year, month.Month, 1);
        Refresh();
    }

    /// <summary>
    /// Katmanın yeniden kurulmasını ister. Kısa aralıkla gelen istekler
    /// birleştirilir; duvar kağıdı değişimi gibi olaylarda kabuk peş peşe
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

        TimeSpan wait = delay ?? DefaultDelay();
        DateTime deadline = DateTime.UtcNow + wait;

        // Bekleyen bir istek daha ileri bir zamana kuruluysa onu öne çekmiyoruz.
        // Duvar kağıdı değişiminde kabuk peş peşe bildirim gönderiyor; her biri
        // beklemeyi sıfırlarsa kabuk daha oturmadan kurmaya kalkarız.
        if (_rebuildTimer.IsEnabled && _rebuildDeadline > deadline)
        {
            Log.Debug($"Yeniden kurma isteği birleştirildi: {reason}");
            return;
        }

        Log.Debug($"Katman yeniden kurulacak ({wait.TotalMilliseconds:F0} ms sonra): {reason}");

        _rebuildDeadline = deadline;
        _rebuildTimer.Stop();
        _rebuildTimer.Interval = wait;
        _rebuildTimer.Start();
    }

    /// <summary>
    /// Kısa sürede çok fazla yeniden kurma olduysa araya daha uzun bir bekleme
    /// koyar; aksi hâlde kabukla sonsuz bir çekişmeye girebiliriz.
    /// </summary>
    private TimeSpan DefaultDelay()
        => _rebuildsInWindow > 5 ? BackoffDelay : RebuildDelay;

    /// <summary>Art arda gelen yeniden kurmaları sayar; geri çekilmeye bunun sonucuna göre karar verilir.</summary>
    private void CountRebuild()
    {
        DateTime now = DateTime.UtcNow;
        if (now - _rebuildWindowStart > TimeSpan.FromSeconds(20))
        {
            _rebuildWindowStart = now;
            _rebuildsInWindow = 0;
        }

        _rebuildsInWindow++;

        if (_rebuildsInWindow == 6)
        {
            Log.Warn("Masaüstü katmanı kısa sürede çok kez yeniden kuruldu; bekleme süresi uzatılıyor.");
        }
    }

    /// <summary>Katmanı sıfırdan kurar. Açılışta, ekran değişiminde ve Explorer yeniden başladığında çağrılır.</summary>
    public void Rebuild()
    {
        if (_disposed)
        {
            return;
        }

        _rebuildTimer.Stop();
        CountRebuild();
        DisposeWindows();

        _host = WorkerWLocator.Find();
        if (_host is null)
        {
            LayerUnavailable?.Invoke();
            return;
        }

        _hostOrigin = ResolveHostOrigin(_host.Handle);

        IReadOnlyList<MonitorTarget> monitors = MonitorEnumerator.All();
        string signature = BuildConfigSignature(monitors);
        bool configChanged = signature != _configSignature;
        _configSignature = signature;

        foreach (MonitorTarget monitor in monitors)
        {
            try
            {
                DesktopWindow window = DesktopWindow.Create(
                    monitor.StableId, monitor.Bounds, _host.Handle, _hostOrigin);

                // Kabuk WorkerW'yi yıkarsa (duvar kağıdı değişimi, tema değişimi,
                // Explorer'ın kendini toparlaması) çocuğumuz da yok olur; o anda
                // katmanı yeniden kuruyoruz.
                window.DestroyedByShell += () => RequestRebuild("çizim penceresi kabuk tarafından yok edildi");

                if (_host.Kind == DesktopHostKind.Progman)
                {
                    // Progman altında simgeler kardeşimiz olur; en alta inmemiz gerekir.
                    window.SendToBack();
                }

                _screens.Add((monitor, window));
            }
            catch (Exception ex)
            {
                Log.Error($"Monitör için çizim penceresi kurulamadı: {monitor}", ex);
            }
        }

        Log.Info($"Masaüstü katmanı kuruldu: {_screens.Count} ekran, tür {_host.Kind}.");
        Refresh();

        if (configChanged)
        {
            Log.Info("Ekran yapılandırması değişti: " + signature);
            MonitorsChanged?.Invoke();
        }
    }

    /// <summary>
    /// Ekranların kimliği, yeri, ölçüsü ve DPI'sının özeti. Değiştiğinde
    /// katman ve ayarlar ekranı tazelenmelidir.
    /// </summary>
    private static string BuildConfigSignature(IReadOnlyList<MonitorTarget> monitors)
        => string.Join(";", monitors.Select(m =>
            $"{m.StableId}@{m.Bounds.X},{m.Bounds.Y},{m.Bounds.Width}x{m.Bounds.Height}#{m.Dpi}"));

    /// <summary>Pencereleri yeniden kurmadan yalnızca görüntüleri üretir.</summary>
    public void Refresh()
    {
        if (_screens.Count == 0)
        {
            return;
        }

        AppSettings settings = _settings.Current;
        bool placementsAdded = false;

        foreach ((MonitorTarget monitor, DesktopWindow window) in _screens)
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

                window.SetSurface(surface);
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
    }

    /// <summary>
    /// Katman hâlâ sağlam mı? Ana pencere yaşıyor mu, pencerelerimiz ona bağlı mı
    /// ve ekran yapılandırması aynı mı? Değilse katman yeniden kurulur.
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

        if (_host is null || !NativeMethods.IsWindow(_host.Handle))
        {
            Log.Info("Masaüstü katmanı kaybolmuş, yeniden kuruluyor.");
            Rebuild();
            return _screens.Count > 0;
        }

        foreach ((_, DesktopWindow window) in _screens)
        {
            if (NativeMethods.GetParent(window.Handle) != _host.Handle)
            {
                Log.Info("Çizim penceresi ana pencereden koptu, katman yeniden kuruluyor.");
                Rebuild();
                return _screens.Count > 0;
            }
        }

        if (BuildConfigSignature(MonitorEnumerator.All()) != _configSignature)
        {
            Log.Info("Ekran yapılandırması bildirimsiz değişmiş, katman yeniden kuruluyor.");
            Rebuild();
            return _screens.Count > 0;
        }

        return _screens.Count > 0;
    }

    /// <summary>
    /// Ekran koordinatındaki noktanın hangi güne denk geldiğini bulur.
    /// Hem ipucu balonu hem de ileride eklenecek takvim menüsü bunu kullanır.
    /// </summary>
    public DayHit? HitTest(Point screenPoint)
    {
        foreach ((MonitorTarget monitor, DesktopWindow window) in _screens)
        {
            if (!monitor.Bounds.Contains(screenPoint))
            {
                continue;
            }

            CalendarLayout? layout = window.Layout;
            if (layout is null)
            {
                return null;
            }

            DayCell? cell = layout.HitTest(window.ToLocal(screenPoint));
            if (cell is null)
            {
                return null;
            }

            var screenBounds = new Rectangle(
                monitor.Bounds.X + (int)cell.Bounds.X,
                monitor.Bounds.Y + (int)cell.Bounds.Y,
                (int)cell.Bounds.Width,
                (int)cell.Bounds.Height);

            return new DayHit(window, cell, screenBounds);
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
        foreach ((MonitorTarget monitor, DesktopWindow window) in _screens)
        {
            if (!monitor.Bounds.Contains(screenPoint))
            {
                continue;
            }

            CalendarLayout? layout = window.Layout;
            if (layout is null)
            {
                return null;
            }

            PointF local = window.ToLocal(screenPoint);
            if (!layout.Bounds.Contains(local))
            {
                return null;
            }

            return new CalendarBlockHit(layout, layout.HitTest(local));
        }

        return null;
    }

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

    /// <summary>
    /// Çocuk pencerelerin konumu ana pencerenin istemci alanına göredir.
    /// WorkerW kenarlıksızdır, bu yüzden pencere dikdörtgeninin sol üstü
    /// istemci başlangıcıyla aynıdır.
    /// </summary>
    private static Point ResolveHostOrigin(IntPtr host)
    {
        if (NativeMethods.GetWindowRect(host, out NativeMethods.RECT rect))
        {
            return new Point(rect.Left, rect.Top);
        }

        Rectangle virtualScreen = MonitorEnumerator.VirtualScreen();
        return new Point(virtualScreen.X, virtualScreen.Y);
    }

    private void DisposeWindows()
    {
        foreach ((_, DesktopWindow window) in _screens)
        {
            window.Dispose();
        }

        _screens.Clear();
    }

    public void Dispose()
    {
        _disposed = true;
        _rebuildTimer.Stop();
        _watchdogTimer.Stop();

        bool hadWindows = _screens.Count > 0;
        DisposeWindows();
        _renderer.Dispose();

        if (hadWindows)
        {
            // Pencerelerimiz gittikten sonra kabuk duvar kağıdını kendiliğinden
            // yeniden boyamıyor; takvim ekranda asılı kalıyor. Açıkça tazeliyoruz.
            DesktopRefresher.ForceRedraw();
        }
    }
}
