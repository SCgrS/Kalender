using System.Windows;
using Kalender.Calendar;
using Kalender.Calendar.Holidays;
using Kalender.Desktop;
using Kalender.Google;
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

    // Takvimin üzerindeki sağ tık menüsü
    private const uint MenuGoToDay = 11;
    private const uint MenuPrevMonth = 12;
    private const uint MenuNextMonth = 13;
    private const uint MenuThisMonth = 14;
    private const uint MenuNewEvent = 15;
    private const uint MenuNewTask = 16;

    /// <summary>
    /// Başka bir aya gidildikten sonra kendiliğinden bu aya dönme süresi.
    /// Takvim masaüstünün bir parçası; uzun süre yanlış ayda kalmamalı.
    /// </summary>
    private static readonly TimeSpan ViewResetDelay = TimeSpan.FromMinutes(2);

    private readonly SettingsStore _settings = new();
    private AppMessageWindow? _messages;
    private TrayIcon? _tray;
    private DesktopLayerManager? _layer;
    private DayChangeWatcher? _dayWatcher;
    private HolidayService? _holidays;
    private CalendarDataService? _calendarData;
    private GoogleAuthenticator? _googleAuth;
    private GoogleCalendarProvider? _google;
    private GooglePoller? _poller;
    private HoverWatcher? _hover;
    private DayPopupWindow? _popup;
    private SettingsWindow? _settingsWindow;
    private CalendarRightClickHook? _rightClick;
    private System.Windows.Threading.DispatcherTimer? _viewResetTimer;
    private CalendarDataSnapshot _data = CalendarDataSnapshot.Empty;

    /// <summary>Takvimin üzerindeki menü şu an açık mı?</summary>
    private bool _menuOpen;

    /// <summary>İmleç şu an takvim bloğunun üzerinde mi?</summary>
    private bool _hoverInside;

    /// <summary>Açık menü kapanır kapanmaz bu noktada yeni menü açılacak.</summary>
    private System.Drawing.Point? _pendingMenuPoint;
    private DateOnly _viewMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private bool _layerWarningShown;

    /// <summary>
    /// Ayarlar uygulanırken doğru: gün verisi yenilenir ama ayrıca çizilmez;
    /// çizimi hemen ardından gelen tam yenileme yapar.
    /// </summary>
    private bool _applying;

    /// <summary>Kullanıcı çıkışı seçtiğinde tetiklenir.</summary>
    public event Action? ExitRequested;

    /// <summary>Ekran listesi değişti; ayarlar penceresi kendini tazelemeli.</summary>
    public event Action? MonitorsChanged;

    /// <summary>Google bağlantı durumu ya da verisi değişti.</summary>
    public event Action? GoogleStateChanged;

    public bool GoogleConnected => _googleAuth?.IsConnected == true;

    public bool GoogleOnline => _google?.IsOnline ?? true;

    public string? GoogleAccountEmail => _googleAuth?.AccountEmail;

    public IReadOnlyList<ProviderCalendar> GoogleCalendars
        => (IReadOnlyList<ProviderCalendar>?)_google?.Data.Calendars ?? Array.Empty<ProviderCalendar>();

    public IReadOnlyList<ProviderTaskList> GoogleTaskLists
        => (IReadOnlyList<ProviderTaskList>?)_google?.Data.TaskLists ?? Array.Empty<ProviderTaskList>();

    public int GoogleEventCount => _google?.Data.Events.Count ?? 0;

    public int GoogleTaskCount => _google?.Data.Tasks.Count ?? 0;

    /// <summary>
    /// Google hesabına bağlanır (tarayıcıda onay açılır) ve ilk veriyi alır.
    /// Aşama değişince <paramref name="progress"/> ile arayüze haber verilir.
    /// </summary>
    public async Task<SignInResult> ConnectGoogleAsync(Action<string>? progress, CancellationToken cancel)
    {
        if (_googleAuth is null)
        {
            return new SignInResult(false, "Uygulama henüz hazır değil.");
        }

        SignInResult result = await _googleAuth.SignInAsync(cancel).ConfigureAwait(true);

        if (result.Success)
        {
            progress?.Invoke("Bağlandı; takvimler ve görevler alınıyor…");
            await RefreshGoogleAsync(force: true).ConfigureAwait(true);
        }

        return result;
    }

    public void DisconnectGoogle()
    {
        _googleAuth?.SignOut();
        _settings.Update(s =>
        {
            s.Google.AccountEmail = null;
            s.Google.SelectedCalendarIds.Clear();
            s.Google.SelectedTaskListIds.Clear();
        });
    }

    public async Task RefreshGoogleAsync(bool force)
    {
        if (_poller is null)
        {
            return;
        }

        await _poller.RefreshAsync(force).ConfigureAwait(true);
        GoogleStateChanged?.Invoke();
    }

    /// <summary>Yoklama aralığı ayarı değiştiğinde çağrılır.</summary>
    public void ApplyPollInterval()
    {
        _poller?.ApplyInterval();
        _poller?.Start();
    }

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
        _dayWatcher = new DayChangeWatcher();
        _holidays = new HolidayService();
        _googleAuth = new GoogleAuthenticator(new SettingsOAuthConfig(_settings));
        _google = new GoogleCalendarProvider(_googleAuth, _settings);
        _google.LoadCache();

        _calendarData = new CalendarDataService(_holidays, _google, _settings);
        _poller = new GooglePoller(_google, _calendarData, _settings);

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
        _messages.Suspending += OnSuspending;

        _dayWatcher.DayChanged += date =>
        {
            _layer.SetToday(date);

            // Gün değiştiyse gezinilen ay artık geçerli değil; bu aya dönüyoruz.
            GoToCurrentMonth();
        };

        _calendarData.Changed += snapshot =>
        {
            _data = snapshot;
            _layer.SetData(snapshot, refresh: !_applying);
            _hover?.Reset();
        };

        _hover = new HoverWatcher(_layer, () => _data);
        _hover.ShowRequested += OnPopupRequested;
        _hover.HideRequested += () => _popup?.HidePopup();

        // Sağ tık menüsü: kanca yalnız imleç takvimin üzerindeyken takılı durur.
        _rightClick = new CalendarRightClickHook(
            Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher,
            // Noktanın masaüstünde olması da şart: takvimin üstünde duran bir
            // pencere (örneğin bizim açık menümüz) varken tıklamayı yutmamalıyız.
            point => HoverWatcher.IsOnDesktopSurface(point) && _layer.HitTestBlock(point) is not null);
        _rightClick.RightClicked += OnCalendarRightClick;
        _hover.CalendarHoverChanged += inside =>
        {
            _hoverInside = inside;
            UpdateRightClickHook();
        };

        _hover.Start();

        _viewResetTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = ViewResetDelay,
        };
        _viewResetTimer.Tick += (_, _) => GoToCurrentMonth();

        // Tatil listesi arka planda inebiliyor; arayüz iş parçacığına taşıyoruz.
        _holidays.Updated += OnHolidaysUpdated;

        // Önce diskteki liste yükleniyor (ağa çıkmadan), sonra gerekiyorsa
        // arka planda tazeleniyor. Böylece açılış internete bağlı değil.
        _holidays.LoadCache();
        _calendarData.SetViewMonth(_viewMonth);

        _googleAuth.StateChanged += OnGoogleStateChanged;
        _googleAuth.YetkiDustu += OnGoogleYetkiDustu;
        _poller.StatusChanged += UpdateTrayTooltip;

        // Her yoklamadan sonra ayarlar penceresi de tazelensin: takvim listesi
        // ve sayılar ilk alıştan sonra kendiliğinden dolsun, kullanıcı sayfayı
        // kapatıp açmak zorunda kalmasın.
        _poller.StatusChanged += () => GoogleStateChanged?.Invoke();
        _poller.Start();

        _layer.SetToday(_dayWatcher.Today);
        _layer.Rebuild();

        // Açılışta başlatma kaydını ayarla eşitle; kullanıcı yolunu değiştirmiş
        // ya da uygulamayı taşımış olabilir.
        StartupManager.Apply(_settings.Current.StartWithWindows);

        // Eski bir kurucuyla kurulmuş kopyada indirme işareti kalmış olabilir;
        // Windows her açılışta onay istememesi için burada da temizleniyor.
        if (Installer.RunningInstalled && Environment.ProcessPath is { } exe)
        {
            Installer.RemoveMarkOfTheWeb(exe);
        }

        if (!_settings.Current.FirstRunDone)
        {
            _settings.Update(s => s.FirstRunDone = true);
        }

        Log.Info("Kalender başlatıldı.");

        // Açılışı bekletmeden tatil listesini tazele.
        _ = RefreshHolidaysAsync(force: false);
    }

    // ------------------------------------------------------- sağ tık menüsü

    /// <summary>
    /// Takvimin üzerinde sağ tıklandı. Menü, imlecin altındaki güne göre
    /// kurulur; gün ızgarasının dışındaysa (ay adı, gün adları) gün gerektiren
    /// satırlar görünen ayı hedefler.
    /// </summary>
    private void OnCalendarRightClick(System.Drawing.Point screenPoint)
    {
        try
        {
            if (_messages is null || _layer is null)
            {
                return;
            }

            if (_menuOpen)
            {
                // Menü zaten açıkken başka bir güne sağ tıklandı. Açık menüyü
                // kapatıyoruz; kapanınca aşağıdaki kuyruk yenisini o günün
                // üzerinde açacak. Yoksa ikinci tık hiçbir şey yapmıyordu.
                _pendingMenuPoint = screenPoint;
                NativeMethods.EndMenu();
                return;
            }

            CalendarBlockHit? hit = _layer.HitTestBlock(screenPoint);
            if (hit is null)
            {
                return;
            }

            DateOnly? gun = hit.Cell?.Date;
            bool buAyda = _viewMonth == CurrentMonth;

            var items = new List<PopupMenuItem>
            {
                gun is null
                    ? new PopupMenuItem(MenuGoToDay,
                        $"{TurkishNames.MonthNameTitle(_viewMonth.Month)} {_viewMonth.Year} ayını Google Takvim'de aç")
                    : new PopupMenuItem(MenuGoToDay,
                        $"{TurkishNames.LongDate(gun.Value)} gününe git"),
                PopupMenuItem.Separator,
                new PopupMenuItem(MenuPrevMonth,
                    "Önceki ay: " + TurkishNames.MonthNameTitle(_viewMonth.AddMonths(-1).Month)),
                new PopupMenuItem(MenuNextMonth,
                    "Sonraki ay: " + TurkishNames.MonthNameTitle(_viewMonth.AddMonths(1).Month)),
                new PopupMenuItem(MenuThisMonth, "Bu aya dön", Enabled: !buAyda),
                PopupMenuItem.Separator,
                new PopupMenuItem(MenuNewEvent, "Yeni etkinlik…"),
                new PopupMenuItem(MenuNewTask, "Yeni görev…"),
                PopupMenuItem.Separator,
                new PopupMenuItem(MenuSettings, "Ayarlar"),
            };

            // Menü açılırken gün kutusu ekranda kalmasın.
            _popup?.HidePopup();

            uint secim;
            _menuOpen = true;
            try
            {
                secim = PopupMenu.Show(_messages.Handle, screenPoint.X, screenPoint.Y, items);
            }
            finally
            {
                _menuOpen = false;

                // Menü açıkken imleç menünün üzerindeydi, yani "takvimden
                // çıkıldı" sayılıyordu ve kancayı sökmeyi ertelemiştik.
                UpdateRightClickHook();
            }

            if (_pendingMenuPoint is System.Drawing.Point sonraki)
            {
                _pendingMenuPoint = null;
                _ = Application.Current?.Dispatcher.BeginInvoke(() => OnCalendarRightClick(sonraki));
                return;
            }

            switch (secim)
            {
                case MenuGoToDay:
                    Ac(gun is null ? GoogleLinks.Month(_viewMonth) : GoogleLinks.Day(gun.Value));
                    break;

                case MenuPrevMonth:
                    ShiftMonth(-1);
                    break;

                case MenuNextMonth:
                    ShiftMonth(1);
                    break;

                case MenuThisMonth:
                    GoToCurrentMonth();
                    break;

                case MenuNewEvent:
                    Ac(GoogleLinks.NewEvent(gun));
                    break;

                case MenuNewTask:
                    Ac(GoogleLinks.NewTask(gun));
                    break;

                case MenuSettings:
                    OpenSettings();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Takvim sağ tık menüsü açılamadı.", ex);
        }
    }

    /// <summary>
    /// Sağ tık kancası takılı mı olmalı? İmleç takvimin üzerindeyken ya da
    /// bizim menümüz açıkken evet, başka her durumda hayır. Menü açıkken
    /// sökülürse sonraki sağ tık kabuğa gidiyor ve Windows'un masaüstü menüsü
    /// bizimkinin üstüne biniyor.
    /// </summary>
    private void UpdateRightClickHook()
    {
        if (_rightClick is null)
        {
            return;
        }

        if (_hoverInside || _menuOpen)
        {
            _rightClick.Arm();
        }
        else
        {
            _rightClick.Disarm();
        }
    }

    private static DateOnly CurrentMonth => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    /// <summary>Görünen ayı ileri ya da geri kaydırır ve geri dönüş sayacını kurar.</summary>
    private void ShiftMonth(int delta)
    {
        _viewMonth = _viewMonth.AddMonths(delta);
        ApplyViewMonth();

        _viewResetTimer?.Stop();
        _viewResetTimer?.Start();
    }

    /// <summary>İçinde bulunduğumuz aya döner ve sayacı durdurur.</summary>
    private void GoToCurrentMonth()
    {
        _viewResetTimer?.Stop();

        if (_viewMonth == CurrentMonth)
        {
            return;
        }

        _viewMonth = CurrentMonth;
        ApplyViewMonth();
    }

    /// <summary>Görünen ayı çizime ve veri katmanına uygular.</summary>
    private void ApplyViewMonth()
    {
        // Çizimi veri katmanı yapar: yeni ayın verisi derlenince Changed ile
        // bir kez çizilir. Katman ayrıca çizseydi görüntü iki kez yazılırdı.
        _layer?.SetViewMonth(_viewMonth, refresh: _calendarData is null);
        _calendarData?.SetViewMonth(_viewMonth);
        _hover?.Reset();

        // Yeni ayın verisi Google'dan istenmeli; aralık değiştiği için tam
        // yenileme kendiliğinden yapılıyor.
        _ = RefreshGoogleAsync(force: false);
    }

    /// <summary>Adresi ya da klasörü kullanıcının varsayılan uygulamasında açar.</summary>
    private static void Ac(string target)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Açılamadı: " + target, ex);
        }
    }

    /// <summary>Gün kutusunu açar. Pencere bir kez kurulup yeniden kullanılır.</summary>
    private void OnPopupRequested(DayContent content, System.Drawing.Rectangle cellBounds)
    {
        try
        {
            _popup ??= new DayPopupWindow();

            // Tamamlanmış kayıtların grisi takvimdekiyle aynı olsun.
            _popup.MutedColor = Rendering.Theme.From(_settings.Current.Appearance).Muted;
            _popup.BackgroundOpacity = _settings.Current.Appearance.DayPopupOpacity;
            _popup.ShowFor(content, cellBounds);
        }
        catch (Exception ex)
        {
            Log.Error("Gün kutusu açılamadı.", ex);
            _popup = null;
        }
    }

    private void OnGoogleStateChanged()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _settings.Update(s => s.Google.AccountEmail = _googleAuth?.AccountEmail);

            if (_googleAuth?.IsConnected == true)
            {
                _poller?.Start();
            }
            else
            {
                _poller?.Stop();
                _google?.ClearCache();
                _calendarData?.Rebuild();
            }

            UpdateTrayTooltip();
            GoogleStateChanged?.Invoke();
        });
    }

    /// <summary>
    /// Google izni kendiliğinden düştü. Tepsi ipucu değişiyor ama kimse ona
    /// bakmıyor; etkinlikler takvimden sessizce kaybolunca kullanıcı bunu
    /// bağlantı sorunu sanıyordu. Balonla haber veriyoruz.
    /// </summary>
    private void OnGoogleYetkiDustu()
    {
        Application.Current?.Dispatcher.BeginInvoke(() => _tray?.ShowBalloon(
            "Kalender",
            "Google bağlantısı düştü; etkinlikler gösterilemiyor. Tepsi simgesine tıklayıp "
                + "Ayarlar > Google'dan yeniden bağlanın.",
            warning: true));
    }

    /// <summary>
    /// Tepsi ipucunda bağlantı durumunu gösteriyoruz. Çevrimdışıyken hata
    /// kutusu açmak yerine burada belli ediliyor (şartname böyle istiyor).
    /// </summary>
    private void UpdateTrayTooltip()
    {
        if (_tray is null)
        {
            return;
        }

        string durum = _googleAuth?.IsConnected != true
            ? "Google bağlı değil"
            : _google?.IsOnline == true
                ? "Google bağlı"
                : "Google bağlı (çevrimdışı)";

        _tray.UpdateTooltip($"Kalender · {durum}");
    }

    private void OnHolidaysUpdated()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _settings.Update(s => s.Holidays.LastFetchUtc = _holidays?.LastFetchUtc);
            _calendarData?.Rebuild();
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
        // Takvim duvar kağıdının kendisine çiziliyor; ayar değişikliği doğrudan
        // yeni bir görüntü demek. Zorluyoruz: duvar kağıdı eşitlemesi yeni
        // açılmışsa görüntü aynı olsa bile yazılmalı.
        _layer?.Rebuild(force: true);
    }

    /// <summary>Ayarlar penceresinin çağırdığı uygulama yolu: kaydet ve tazele.</summary>
    public void ApplyAppearance()
    {
        _settings.Save();

        // Tatil gösterimi gibi ayarlar gün içeriğini de etkiliyor. Veri burada
        // çizimsiz güncellenir, hemen ardından ApplySettings bir kez çizer.
        // Önceden her değişiklikte görüntü iki kez üretilip iki kez yazılıyordu.
        _applying = true;
        try
        {
            _calendarData?.Rebuild();
        }
        finally
        {
            _applying = false;
        }

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

    private IReadOnlyList<PopupMenuItem> BuildMenu()
    {
        bool hidden = _settings.Current.CalendarHidden;

        return new[]
        {
            new PopupMenuItem(MenuSettings, "Ayarlar"),
            new PopupMenuItem(MenuRefresh, "Şimdi yenile"),
            PopupMenuItem.Separator,
            new PopupMenuItem(MenuToggleCalendar, hidden ? "Takvimi göster" : "Takvimi gizle"),
            new PopupMenuItem(MenuRedraw, "Takvimi yeniden çiz"),
            new PopupMenuItem(MenuRestoreWallpaper, "Eski duvar kağıdıma dön"),
            PopupMenuItem.Separator,
            new PopupMenuItem(MenuExit, "Çıkış"),
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
                _ = RefreshGoogleAsync(force: true);
                break;

            case MenuToggleCalendar:
                _settings.Update(s => s.CalendarHidden = !s.CalendarHidden);
                _layer?.Refresh();
                break;

            case MenuRedraw:
                _layer?.Rebuild(force: true);
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
    /// Kalender kurulmadan önceki duvar kağıdını geri yükler ve duvar kağıdına
    /// yazmayı kapatır; yoksa bir sonraki açılışta yeniden değiştirilirdi.
    /// Takvim duvar kağıdına çizildiği için bu, takvimi de masaüstünden kaldırır.
    /// </summary>
    private void RestoreWallpaper()
    {
        bool ok = WallpaperService.Restore();
        _settings.Update(s => s.SyncSystemWallpaper = false);

        _tray?.ShowBalloon(
            "Kalender",
            ok
                ? "Eski duvar kağıdınız geri yüklendi. Takvim artık masaüstüne çizilmeyecek."
                : "Yedeklenmiş bir duvar kağıdı bulunamadı. Takvim yine de artık masaüstüne çizilmeyecek.",
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
        // Explorer yeniden başlarken duvar kağıdını kendi önbelleğinden
        // okuyor; bizim son yazdığımızı geri koymak için yeniden çiziyoruz.
        _layer?.RequestRebuild("Explorer yeniden başladı", TimeSpan.FromSeconds(2));
    }

    private void OnResumed()
    {
        _dayWatcher?.Recheck();
        _layer?.EnsureAlive();

        // Uykuda geçen sürede ay dönmüş ve liste eskimiş olabilir.
        _ = RefreshHolidaysAsync(force: false);

        // Uyandıktan sonra tek seferlik yenileme; zamanlayıcı uykuda durmuştu.
        _poller?.Start();
    }

    private void OnSuspending()
    {
        // Uykuda ağa çıkmanın anlamı yok; zamanlayıcıyı durduruyoruz.
        _poller?.Stop();
    }

    private void OnLayerUnavailable()
    {
        if (_layerWarningShown)
        {
            return;
        }

        _layerWarningShown = true;
        Log.Warn("Takvim duvar kağıdına yazılamadı.");

        _tray?.ShowBalloon(
            "Kalender",
            "Takvim duvar kağıdına yazılamadı. Windows duvar kağıdı ayarı bir ilke (policy) ile kilitlenmiş olabilir.",
            warning: true);
    }

    public void Dispose()
    {
        _settingsWindow?.Close();
        _viewResetTimer?.Stop();
        _hover?.Dispose();
        _rightClick?.Dispose();
        _popup?.Close();
        _poller?.Dispose();
        _google?.Dispose();
        _googleAuth?.Dispose();
        _holidays?.Dispose();
        _dayWatcher?.Dispose();
        _tray?.Dispose();
        _layer?.Dispose();
        _messages?.Dispose();
    }
}
