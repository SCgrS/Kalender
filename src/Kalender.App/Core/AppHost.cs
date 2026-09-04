using System.Windows;
using Kalender.Calendar;
using Kalender.Calendar.Holidays;
using Kalender.Desktop;
using Kalender.Interop;
using Kalender.Settings;
using Kalender.Time;
using Kalender.Tray;
using Kalender.Ui;

namespace Kalender.Core;

/// <summary>
/// Uygulamanın omurgası. Parçaları kurar, birbirine bağlar ve kapanışta
/// düzgünce toplar. İş mantığı burada değil, ilgili sınıflardadır.
/// </summary>
internal sealed class AppHost : IDisposable, IAppearanceHost
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
    private HolidayService? _holidays;
    private CalendarDataService? _calendarData;
    private SettingsWindow? _settingsWindow;
    private CalendarDataSnapshot _data = CalendarDataSnapshot.Empty;
    private bool _layerWarningShown;

    /// <summary>Kullanıcı çıkışı seçtiğinde tetiklenir.</summary>
    public event Action? ExitRequested;

    /// <summary>Ekran listesi değişti; ayarlar penceresi kendini tazelemeli.</summary>
    public event Action? MonitorsChanged;

    /// <summary>Tatil listesi yenilendi.</summary>
    public event Action? HolidaysUpdated;

    public DateTimeOffset? HolidaysLastFetchUtc => _holidays?.LastFetchUtc;

    public int HolidayCount => _holidays?.Count ?? 0;

    public SettingsStore Settings => _settings;

    /// <summary>
    /// Ayarlar ekranının gösterdiği monitör listesi. Masaüstü katmanı
    /// kurulamadıysa doğrudan sistemden okunur.
    /// </summary>
    public IReadOnlyList<MonitorTarget> Monitors
    {
        get
        {
            IReadOnlyList<MonitorTarget>? fromLayer = _layer?.Monitors;
            return fromLayer is { Count: > 0 } ? fromLayer : MonitorEnumerator.All();
        }
    }

    public CalendarDataSnapshot Data => _data;

    public DateOnly Today => _dayWatcher?.Today ?? DateOnly.FromDateTime(DateTime.Now);

    public void Start()
    {
        _settings.Load();

        _messages = AppMessageWindow.Create();
        _layer = new DesktopLayerManager(_settings);
        _wallpaper = new WallpaperService(_settings);
        _dayWatcher = new DayChangeWatcher();
        _holidays = new HolidayService();
        _calendarData = new CalendarDataService(_holidays, _settings);

        _tray = new TrayIcon(_messages)
        {
            MenuBuilder = BuildMenu,
        };
        _tray.MenuItemClicked += OnMenuItemClicked;
        _tray.Clicked += OpenSettings;
        _tray.Show("Kalender");

        _layer.LayerUnavailable += OnLayerUnavailable;
        _layer.MonitorsChanged += () => MonitorsChanged?.Invoke();

        _messages.CloseRequested += () => ExitRequested?.Invoke();
        _messages.SettingsRequested += OpenSettings;
        _messages.DisplayChanged += OnDisplayChanged;
        _messages.ShellRestarted += OnShellRestarted;
        _messages.TimeChanged += () => _dayWatcher.Recheck();
        _messages.Resumed += OnResumed;

        _dayWatcher.DayChanged += date =>
        {
            _layer.SetToday(date);
            _calendarData.SetToday(date);
        };

        _calendarData.Changed += snapshot =>
        {
            _data = snapshot;
            _layer.SetData(snapshot);
        };

        // Tatil listesi arka planda inebiliyor; arayüz iş parçacığına taşıyoruz.
        _holidays.Updated += OnHolidaysUpdated;

        // Önce diskteki liste yükleniyor (ağa çıkmadan), sonra gerekiyorsa
        // arka planda tazeleniyor. Böylece açılış internete bağlı değil.
        _holidays.LoadCache();
        _calendarData.SetToday(_dayWatcher.Today);

        // Önce gerçek duvar kağıdını yazıyoruz: bir sonraki açılışta Kalender
        // devreye girene kadar ekran doğru görünsün.
        bool wallpaperChanged = _wallpaper.Apply();

        _layer.SetToday(_dayWatcher.Today);

        if (wallpaperChanged)
        {
            // Duvar kağıdı değişince kabuk WorkerW'yi yıkıp yeniden kuruyor.
            // Hemen kurarsak katmanımız o yıkıma yakalanır; kabuğun oturmasını bekliyoruz.
            _layer.RequestRebuild("açılış (duvar kağıdı değişti)", TimeSpan.FromMilliseconds(2500));
        }
        else
        {
            _layer.Rebuild();
        }

        if (!_settings.Current.FirstRunDone)
        {
            _settings.Update(s => s.FirstRunDone = true);
        }

        Log.Info("Kalender başlatıldı.");

        // Açılışı bekletmeden tatil listesini tazele.
        _ = RefreshHolidaysAsync(force: false);
    }

    private void OnHolidaysUpdated()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _settings.Update(s => s.Holidays.LastFetchUtc = _holidays?.LastFetchUtc);
            _calendarData?.Rebuild();
            HolidaysUpdated?.Invoke();
        });
    }

    /// <summary>Tatil listesini tazeler. Hata olursa sessizce önbellek kullanılır.</summary>
    public async Task RefreshHolidaysAsync(bool force)
    {
        if (_holidays is null)
        {
            return;
        }

        try
        {
            await _holidays.RefreshAsync(force).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn("Tatil yenilemesi başarısız.", ex);
        }
    }

    /// <summary>Ayarlar değiştikten sonra çağrılır: zemini ve çizimi tazeler.</summary>
    public void ApplySettings()
    {
        bool wallpaperChanged = _wallpaper?.Apply() ?? false;

        if (wallpaperChanged)
        {
            _layer?.RequestRebuild("ayar değişti (duvar kağıdı yenilendi)", TimeSpan.FromMilliseconds(2500));
        }
        else
        {
            _layer?.Refresh();
        }
    }

    /// <summary>Ayarlar penceresinin çağırdığı uygulama yolu: kaydet ve tazele.</summary>
    public void ApplyAppearance()
    {
        _settings.Save();

        // Tatil gösterimi gibi ayarlar gün içeriğini de etkiliyor.
        _calendarData?.Rebuild();

        ApplySettings();
    }

    public void OpenSettings()
    {
        try
        {
            if (_settingsWindow is not null)
            {
                _settingsWindow.Activate();
                return;
            }

            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Error("Ayarlar penceresi açılamadı.", ex);
            _settingsWindow = null;
        }
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu()
    {
        bool hidden = _settings.Current.CalendarHidden;

        return new[]
        {
            new TrayMenuItem(MenuSettings, "Ayarlar"),

            // Şimdilik yalnız tatil listesini yeniler; 4. aşamada Google verisi de katılacak.
            new TrayMenuItem(MenuRefresh, "Şimdi yenile"),
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
            case MenuSettings:
                OpenSettings();
                break;

            case MenuRefresh:
                _ = RefreshHolidaysAsync(force: true);
                break;

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
        // Çözünürlük, monitör düzeni ya da ölçek değişti: katmanı sıfırdan kur.
        // Burada duvar kağıdına dokunmuyoruz; duvar kağıdı yazmak yeni bir
        // ayar değişikliği bildirimi doğurur ve döngüye girilir.
        _layer?.RequestRebuild("ekran yapılandırması değişti");
    }

    private void OnShellRestarted()
    {
        // Explorer yeniden başladığında WorkerW de yeniden üretilir.
        _layer?.RequestRebuild("Explorer yeniden başladı", TimeSpan.FromSeconds(2));
    }

    private void OnResumed()
    {
        _dayWatcher?.Recheck();
        _layer?.EnsureAlive();

        // Uykuda geçen sürede ay dönmüş ve liste eskimiş olabilir.
        _ = RefreshHolidaysAsync(force: false);
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
        _settingsWindow?.Close();
        _holidays?.Dispose();
        _dayWatcher?.Dispose();
        _tray?.Dispose();
        _layer?.Dispose();
        _messages?.Dispose();
    }
}
