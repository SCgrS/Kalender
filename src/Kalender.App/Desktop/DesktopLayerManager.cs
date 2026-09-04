using System.Drawing;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Interop;
using Kalender.Rendering;
using Kalender.Settings;

namespace Kalender.Desktop;

/// <summary>Fare konumunun denk geldiği gün ve onu barındıran ekran.</summary>
internal sealed record DayHit(DesktopWindow Window, DayCell Cell, Rectangle CellScreenBounds);

/// <summary>
/// Masaüstü katmanının tamamını yönetir: WorkerW'yi bulur, her monitör için
/// bir çizim penceresi kurar, görüntüleri üretir ve ekran yapılandırması
/// değiştiğinde her şeyi yeniden kurar.
/// </summary>
internal sealed class DesktopLayerManager : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly DesktopSurfaceRenderer _renderer = new();
    private readonly List<(MonitorTarget Monitor, DesktopWindow Window)> _screens = new();

    private DesktopHost? _host;
    private Point _hostOrigin;
    private CalendarDataSnapshot _data = CalendarDataSnapshot.Empty;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);

    public DesktopLayerManager(SettingsStore settings)
    {
        _settings = settings;
    }

    /// <summary>Masaüstü katmanı hiç kurulamadığında bir kez tetiklenir.</summary>
    public event Action? LayerUnavailable;

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

    /// <summary>Katmanı sıfırdan kurar. Açılışta, ekran değişiminde ve Explorer yeniden başladığında çağrılır.</summary>
    public void Rebuild()
    {
        DisposeWindows();

        _host = WorkerWLocator.Find();
        if (_host is null)
        {
            LayerUnavailable?.Invoke();
            return;
        }

        _hostOrigin = ResolveHostOrigin(_host.Handle);

        foreach (MonitorTarget monitor in MonitorEnumerator.All())
        {
            try
            {
                DesktopWindow window = DesktopWindow.Create(
                    monitor.StableId, monitor.Bounds, _host.Handle, _hostOrigin);

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
    }

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
                    monitor.DpiScale,
                    settings,
                    placement,
                    _data,
                    _today,
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

    /// <summary>Ana pencere hâlâ yaşıyor mu? Değilse katman yeniden kurulur.</summary>
    public bool EnsureAlive()
    {
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
        DisposeWindows();
        _renderer.Dispose();
    }
}
