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

    /// <summary>
    /// Başka bir aya gidildikten sonra kendiliğinden bu aya dönme süresi.
    /// Takvim masaüstünün bir parçası; uzun süre yanlış ayda kalmamalı. Sayaç
    /// takvime her dokunuşta (ay düğmesi, sağ tık menüsü, imleci takvimin
    /// üzerinde gezdirme) baştan başlar; bu süre boyunca dokunulmazsa bu aya dönülür.
    /// </summary>
    private static readonly TimeSpan ViewResetDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Açılıştan ve uykudan uyanmadan sonra görüntünün güvence için bir kez
    /// daha yazılma süresi. Windows'un masaüstünü kurması bitmiş olsun diye
    /// açılışta daha uzun bekleniyor.
    /// </summary>
    private static readonly TimeSpan AcilisTazelemeGecikmesi = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan UyanmaTazelemeGecikmesi = TimeSpan.FromSeconds(20);

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
    private CalendarMouseHook? _mouseHook;
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

    private readonly UpdateService _updates;

    /// <summary>Açık güncelleme hatırlatma penceresi; yoksa null.</summary>
    private SetupWindow? _updatePrompt;

    /// <summary>
    /// Ayarlar uygulanırken doğru: gün verisi yenilenir ama ayrıca çizilmez;
    /// çizimi hemen ardından gelen tam yenileme yapar.
    /// </summary>
    private bool _applying;

    public AppHost()
    {
        _updates = new UpdateService(_settings);
    }

    /// <summary>Kullanıcı çıkışı seçtiğinde tetiklenir.</summary>
    public event Action? ExitRequested;

    public UpdateService Updates => _updates;

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

        // Zemin olarak seçili hazır fotoğraf diskten silindiyse yeniden çıkarılır.
        HazirFotograflar.RestoreMissing(_settings.Current);

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

        // Sağ tık menüsü ve ay düğmeleri: kanca yalnız imleç takvimin
        // üzerindeyken takılı durur. Noktanın masaüstünde olması da şart:
        // takvimin üstünde duran bir pencere (örneğin bizim açık menümüz)
        // varken tıklamayı yutmamalıyız. Takvimin üstündeki bir masaüstü
        // simgesine (klasör, kısayol) tıklama da simgeye gider. Denetimler
        // ucuzdan pahalıya sıralı; simge sorusu yalnız gerekirse sorulur.
        _mouseHook = new CalendarMouseHook(
            Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher,
            point => _layer.HitTestBlock(point) is not null
                && HoverWatcher.IsOnDesktopSurface(point)
                && !MasaustuSimgeleri.Ustunde(point),
            point => _layer.HitTestNav(point) is NavButton dugme and not NavButton.None
                && HoverWatcher.IsOnDesktopSurface(point)
                && !MasaustuSimgeleri.Ustunde(point)
                    ? dugme
                    : NavButton.None);
        _mouseHook.RightClicked += OnCalendarRightClick;
        _mouseHook.NavClicked += OnNavClicked;
        _hover.CalendarHoverChanged += inside =>
        {
            _hoverInside = inside;
            UpdateMouseHook();
        };
        _hover.CalendarActivity += RestartViewResetTimer;

        _hover.Start();

        _viewResetTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = ViewResetDelay,
        };
        _viewResetTimer.Tick += (_, _) => ChangeMonth(GoToCurrentMonth);

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
        _layer.TazelemeIste(AcilisTazelemeGecikmesi);

        // Açılışta başlatma kaydını ayarla eşitle; kullanıcı yolunu değiştirmiş
        // ya da uygulamayı taşımış olabilir.
        StartupManager.Apply(_settings.Current.StartWithWindows);

        // Eski bir kurucuyla kurulmuş kopyada indirme işareti kalmış olabilir;
        // Windows her açılışta onay istememesi için burada da temizleniyor.
        if (Installer.RunningInstalled && Environment.ProcessPath is { } exe)
        {
            Installer.RemoveMarkOfTheWeb(exe);
        }

        _updates.ExitRequested += OnUpdateInstalling;
        _updates.PromptRequested += OnUpdatePromptRequested;
        _updates.Start();

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
    /// Takvimin üzerindeki sağ tık menüsünün satırları. İlk satır imlecin
    /// altındaki günü ("10 Ekim'i takvimde aç"), gün ızgarasının dışındaysa
    /// (ay adı, gün adları) görünen ayı Google Takvim'de açar.
    ///
    /// Yeni etkinlik ve yeni görev satırları kaldırıldı: Google görev ekranı
    /// için adres sunmuyor, etkinlik taslağı da her seferinde açılmıyordu.
    /// </summary>
    internal static List<PopupMenuItem> SagTikMenusu(DateOnly? gun, DateOnly gorunenAy, bool buAyda)
        => new List<PopupMenuItem>
        {
            gun is null
                ? new PopupMenuItem(MenuGoToDay,
                    $"{TurkishNames.MonthNameTitle(gorunenAy.Month)} {gorunenAy.Year} ayını takvimde aç")
                : new PopupMenuItem(MenuGoToDay,
                    $"{TurkishNames.DayMonthAccusative(gun.Value)} takvimde aç"),
            PopupMenuItem.Separator,
            new PopupMenuItem(MenuPrevMonth,
                "Önceki ay: " + TurkishNames.MonthNameTitle(gorunenAy.AddMonths(-1).Month)),
            new PopupMenuItem(MenuNextMonth,
                "Sonraki ay: " + TurkishNames.MonthNameTitle(gorunenAy.AddMonths(1).Month)),
            new PopupMenuItem(MenuThisMonth, "Bu aya dön", Enabled: !buAyda),
            PopupMenuItem.Separator,
            new PopupMenuItem(MenuSettings, "Ayarlar"),
        };

    /// <summary>
    /// Takvimin üzerinde sağ tıklandı. Menü, imlecin altındaki güne göre
    /// kurulur (<see cref="SagTikMenusu"/>).
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
            List<PopupMenuItem> items = SagTikMenusu(gun, _viewMonth, buAyda: _viewMonth == CurrentMonth);

            // Menü açılırken gün kutusu ekranda kalmasın.
            _popup?.HidePopup();

            // Menü açıkken bu aya dönüş sayacı işlemesin. Satırlar açılış
            // anındaki aya göre yazıldı; menü 30 saniyeden uzun açık kalıp
            // sayaç ayı değiştirirse "Önceki ay: Eylül" başka bir aya gider,
            // ayın satırı da başka bir ayı açardı. Kapanınca süre baştan başlar.
            bool sayacCalisiyordu = _viewResetTimer?.IsEnabled == true;
            _viewResetTimer?.Stop();

            uint secim;
            _menuOpen = true;
            try
            {
                secim = PopupMenu.Show(_messages.Handle, screenPoint.X, screenPoint.Y, items);
            }
            finally
            {
                _menuOpen = false;

                if (sayacCalisiyordu)
                {
                    _viewResetTimer?.Start();
                }

                // Menü açıkken imleç menünün üzerindeydi, yani "takvimden
                // çıkıldı" sayılıyordu ve kancayı sökmeyi ertelemiştik.
                UpdateMouseHook();
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
                    ChangeMonth(() => ShiftMonth(-1));
                    break;

                case MenuNextMonth:
                    ChangeMonth(() => ShiftMonth(1));
                    break;

                case MenuThisMonth:
                    ChangeMonth(GoToCurrentMonth);
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
    /// Fare kancası takılı mı olmalı? İmleç takvimin üzerindeyken ya da
    /// bizim menümüz açıkken evet, başka her durumda hayır. Menü açıkken
    /// sökülürse sonraki sağ tık kabuğa gidiyor ve Windows'un masaüstü menüsü
    /// bizimkinin üstüne biniyor.
    /// </summary>
    private void UpdateMouseHook()
    {
        if (_mouseHook is null)
        {
            return;
        }

        if (_hoverInside || _menuOpen)
        {
            _mouseHook.Arm();
        }
        else
        {
            _mouseHook.Disarm();
        }
    }

    /// <summary>Ay düğmelerinden birine basıldı: önceki ay, sonraki ay ya da bu aya dönüş.</summary>
    private void OnNavClicked(NavButton dugme)
    {
        switch (dugme)
        {
            case NavButton.Previous:
                ChangeMonth(() => ShiftMonth(-1));
                break;

            case NavButton.Next:
                ChangeMonth(() => ShiftMonth(1));
                break;

            case NavButton.Today:
                ChangeMonth(GoToCurrentMonth);
                break;
        }
    }

    /// <summary>
    /// Görünen ayı değiştirir. Yeni ay çizilip duvar kağıdına yazılırken fare
    /// kancası sökülü tutulur: arayüz iş parçacığı o sırada meşgul ve takılı
    /// bir alt düzey kanca bütün sistemin fare hareketini bekletirdi. Buraya
    /// tıklamanın iki yarısı da kancadan geçtikten sonra gelinir; sökmek
    /// kabuğa yarım bir olay çifti göndermez.
    /// </summary>
    private void ChangeMonth(Action change)
    {
        _mouseHook?.Disarm();
        try
        {
            change();
        }
        catch (Exception ex)
        {
            Log.Warn("Ay değiştirilemedi.", ex);
        }
        finally
        {
            UpdateMouseHook();
        }
    }

    private static DateOnly CurrentMonth => new(DateTime.Now.Year, DateTime.Now.Month, 1);

    /// <summary>Görünen ayı ileri ya da geri kaydırır ve geri dönüş sayacını kurar.</summary>
    private void ShiftMonth(int delta)
    {
        _viewMonth = _viewMonth.AddMonths(delta);
        ApplyViewMonth();

        if (_viewMonth == CurrentMonth)
        {
            // Düğmelerle bu aya geri gelindi; sayacın bekleyeceği bir şey kalmadı.
            _viewResetTimer?.Stop();
            return;
        }

        _viewResetTimer?.Stop();
        _viewResetTimer?.Start();
    }

    /// <summary>
    /// Takvime dokunuldu (imleç üzerinde gezindi): başka bir aydaysak bu aya
    /// dönüş sayacı baştan başlar. Bu ayda iken sayaç zaten çalışmıyor.
    /// </summary>
    private void RestartViewResetTimer()
    {
        if (_viewResetTimer is { IsEnabled: true })
        {
            _viewResetTimer.Stop();
            _viewResetTimer.Start();
        }
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
        // Bu ay yakın zamanda gösterildiyse Google verisi bellekte; Google'dan
        // yeniden gelmesi beklenmeden onunla çizilir, noktacıklar kaybolmaz.
        (DateOnly ilk, DateOnly son) = CalendarLayout.GridRange(_viewMonth);
        _google?.GecmistenGetir(ilk, son);

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
            // Pencere açılırken güncelleme bilgisi eskiyse tazelensin; bildirim birazdan belirir.
            _updates.CheckIfStale();

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

    /// <summary>
    /// Güncellemeden sonraki ilk açılış. Güncelleme ayarlar penceresinden
    /// başlatıldıysa pencere yeni sürümle yeniden açılır ve bunu söyler;
    /// hatırlatma penceresinden başlatıldıysa yalnızca bir bildirim çıkar.
    /// </summary>
    /// <param name="quiet">Ayarlar penceresi açılmasın, bildirimle yetinilsin mi?</param>
    public void ShowUpdated(bool quiet)
    {
        _updates.ClearPromptShown();

        string text = $"Kalender {Installer.Version} sürümüne güncellendi.";
        if (quiet)
        {
            _tray?.ShowBalloon("Kalender", text + " Ayarlarınız olduğu gibi duruyor.", warning: false);
            return;
        }

        OpenSettings();
        _settingsWindow?.ShowNotice(text);
    }

    /// <summary>
    /// Yeni sürüm bulundu ve haber verme zamanı geldi: kurulum penceresinin
    /// güncelleme kipini açar. Kurulu olmayan (taşınabilir) kopyada açılmaz;
    /// kendini kuracağı bir yer yoktur. Pencere klavye odağını almaz: kullanıcı
    /// o sırada yazı yazıyorsa bir tuş yanlışlıkla güncellemeyi başlatmasın.
    /// </summary>
    private void OnUpdatePromptRequested()
    {
        try
        {
            if (_updatePrompt is not null || !Installer.RunningInstalled)
            {
                return;
            }

            _updatePrompt = new SetupWindow(_updates);
            _updatePrompt.Closed += (_, _) => _updatePrompt = null;
            _updatePrompt.Show();

            _updates.MarkPromptShown();
            Log.Info("Güncelleme penceresi gösterildi.");
        }
        catch (Exception ex)
        {
            Log.Warn("Güncelleme penceresi açılamadı.", ex);
            _updatePrompt = null;
        }
    }

    /// <summary>
    /// Yeni sürümün kurucusu başlatıldı; dosyamızı değiştirebilsin diye
    /// kapanıyoruz. Takvim duvar kağıdından silinmez: yeni sürüm birkaç saniye
    /// içinde aynısını çizecek. Kurulum başarısız olursa kurucu bu sürümü geri açar.
    /// </summary>
    private void OnUpdateInstalling()
    {
        if (_layer is not null)
        {
            _layer.KeepWallpaperOnExit = true;
        }

        ExitRequested?.Invoke();
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
            new PopupMenuItem(MenuRestoreWallpaper, "Eski duvar kâğıdıma dön"),
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
        // Önce yazma kapatılır: geri yükleme sürerken gelen bir çizim takvimi
        // yeniden yazmasın. Katman yedeği olmayan ya da geri yüklenemeyen
        // ekrana takvimsiz zemin yazar; önceden orada takvim donmuş kalıyordu.
        _settings.Update(s => s.SyncSystemWallpaper = false);
        bool ok = _layer?.DuvarKagidiniGeriYukle() ?? WallpaperService.Restore();

        _tray?.ShowBalloon(
            "Kalender",
            ok
                ? "Eski duvar kâğıdınız geri yüklendi. Takvim artık masaüstüne çizilmeyecek."
                : "Yedeklenmiş bir duvar kâğıdı bulunamadı. Takvim yine de artık masaüstüne çizilmeyecek.",
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
        _layer?.TazelemeIste(UyanmaTazelemeGecikmesi);

        // Uykuda geçen sürede ay dönmüş ve liste eskimiş olabilir.
        _ = RefreshHolidaysAsync(force: false);

        // Uykudan önce alınan erişim anahtarına güvenilmiyor: süresi saatle
        // hesaplanmıştı, Google ise onu uykuda geçersiz saymış olabilir.
        _googleAuth?.ErisimAnahtariniBirak();

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
        Log.Warn("Takvim duvar kâğıdına yazılamadı.");

        _tray?.ShowBalloon(
            "Kalender",
            "Takvim duvar kâğıdına yazılamadı. Windows duvar kâğıdı ayarı bir ilke (policy) ile kilitlenmiş olabilir.",
            warning: true);
    }

    public void Dispose()
    {
        _settingsWindow?.Close();
        _updatePrompt?.Close();
        _updates.Dispose();
        _viewResetTimer?.Stop();
        _hover?.Dispose();
        _mouseHook?.Dispose();
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
