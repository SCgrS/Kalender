using Kalender.Desktop;
using Kalender.Interop;
using Kalender.Settings;
using Kalender.Time;
using Kalender.Tray;

namespace Kalender.Core;

/// <summary>
/// Uygulamanın omurgası. Parçaları kurar, birbirine bağlar ve kapanışta
/// düzgünce toplar. İş mantığı burada değil, ilgili sınıflardadır.
/// </summary>
internal sealed class AppHost : IDisposable
{
    private const uint MenuSettings = 1;
    private const uint MenuRefresh = 2;
    private const uint MenuToggleCalendar = 3;
    private const uint MenuRedraw = 4;
    private const uint MenuRestoreWallpaper = 5;
    private const uint MenuExit = 9;

    private readonly SettingsStore _settings = new();
    private AppMessageWindow? _messages;
    private TrayIcon? _tray;
    private DesktopLayerManager? _layer;
    private WallpaperService? _wallpaper;
    private DayChangeWatcher? _dayWatcher;
    private bool _layerWarningShown;

    /// <summary>Kullanıcı çıkışı seçtiğinde tetiklenir.</summary>
    public event Action? ExitRequested;

    public SettingsStore Settings => _settings;

    public void Start()
    {
        _settings.Load();

        _messages = AppMessageWindow.Create();
        _layer = new DesktopLayerManager(_settings);
        _wallpaper = new WallpaperService(_settings);
        _dayWatcher = new DayChangeWatcher();

        _tray = new TrayIcon(_messages)
        {
            MenuBuilder = BuildMenu,
        };
        _tray.MenuItemClicked += OnMenuItemClicked;
        _tray.Clicked += () => { /* Ayarlar penceresi 6. aşamada bağlanacak. */ };
        _tray.Show("Kalender");

        _layer.LayerUnavailable += OnLayerUnavailable;

        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ShellRestarted += OnShellRestarted;
        _messages.TimeChanged += () => _dayWatcher.Recheck();
        _messages.Resumed += OnResumed;

        _dayWatcher.DayChanged += date => _layer.SetToday(date);

        // Önce gerçek duvar kağıdını yazıyoruz: bir sonraki açılışta Kalender
        // devreye girene kadar ekran doğru görünsün.
        _wallpaper.Apply();

        _layer.SetToday(_dayWatcher.Today);
        _layer.Rebuild();

        if (!_settings.Current.FirstRunDone)
        {
            _settings.Update(s => s.FirstRunDone = true);
        }

        Log.Info("Kalender başlatıldı.");
    }

    /// <summary>Ayarlar değiştikten sonra çağrılır: zemini ve çizimi tazeler.</summary>
    public void ApplySettings()
    {
        _wallpaper?.Apply();
        _layer?.Refresh();
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        bool hidden = _settings.Current.CalendarHidden;

        return new[]
        {
            // Ayarlar penceresi 6. aşamada eklenecek.
            new TrayMenuItem(MenuSettings, "Ayarlar", Enabled: false),
            new TrayMenuItem(MenuRefresh, "Şimdi yenile", Enabled: false),
            TrayMenuItem.Separator,
            new TrayMenuItem(MenuToggleCalendar, hidden ? "Takvimi göster" : "Takvimi gizle"),
            new TrayMenuItem(MenuRedraw, "Takvimi yeniden çiz"),
            new TrayMenuItem(MenuRestoreWallpaper, "Eski duvar kağıdıma dön"),
            TrayMenuItem.Separator,
            new TrayMenuItem(MenuExit, "Çıkış"),
        };
    }

    private void OnMenuItemClicked(uint id)
    {
        switch (id)
        {
            case MenuToggleCalendar:
                _settings.Update(s => s.CalendarHidden = !s.CalendarHidden);
                _layer?.Refresh();
                break;

            case MenuRedraw:
                _layer?.Rebuild();
                break;

            case MenuRestoreWallpaper:
                RestoreWallpaper();
                break;

            case MenuExit:
                ExitRequested?.Invoke();
                break;
        }
    }

    /// <summary>
    /// Kalender kurulmadan önceki duvar kağıdını geri yükler ve zemin eşitlemesini
    /// kapatır; yoksa bir sonraki açılışta yeniden değiştirilirdi.
    /// </summary>
    private void RestoreWallpaper()
    {
        bool ok = WallpaperService.Restore();
        _settings.Update(s => s.SyncSystemWallpaper = false);

        _tray?.ShowBalloon(
            "Kalender",
            ok
                ? "Eski duvar kağıdınız geri yüklendi. Windows duvar kağıdı eşitlemesi kapatıldı."
                : "Yedeklenmiş bir duvar kağıdı bulunamadı. Windows duvar kağıdı eşitlemesi yine de kapatıldı.",
            warning: !ok);
    }

    private void OnDisplayChanged()
    {
        // Çözünürlük ya da monitör düzeni değişti: katmanı sıfırdan kur.
        _layer?.Rebuild();
        _wallpaper?.Apply();
    }

    private void OnShellRestarted()
    {
        // Explorer yeniden başladığında WorkerW de yeniden üretilir.
        _layer?.Rebuild();
    }

    private void OnResumed()
    {
        _dayWatcher?.Recheck();
        if (_layer is not null && !_layer.EnsureAlive())
        {
            _layer.Rebuild();
        }
    }

    private void OnLayerUnavailable()
    {
        if (_layerWarningShown)
        {
            return;
        }

        _layerWarningShown = true;
        Log.Warn("Masaüstü katmanı kurulamadı; yalnızca duvar kağıdı zemini uygulanıyor.");

        _tray?.ShowBalloon(
            "Kalender",
            "Masaüstü katmanı bu Windows kurulumunda açılamadı. Zemin duvar kağıdı olarak uygulandı, ancak takvim çizilemiyor.",
            warning: true);
    }

    public void Dispose()
    {
        _dayWatcher?.Dispose();
        _tray?.Dispose();
        _layer?.Dispose();
        _messages?.Dispose();
    }
}
