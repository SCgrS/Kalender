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
/// Bir monitör ve üzerine çizilen takvimin geometrisi (takvim gizliyse null).
/// Görüntünün kendisi tutulmuyor: duvar kağıdına yazıldıktan sonra yalnızca
/// takvimin ekrandaki yeri gerekiyor; 2560 × 1440 bir ekranda görüntü 14 MB
/// bellek tutuyordu.
/// </summary>
internal sealed record EkrandakiTakvim(MonitorTarget Monitor, CalendarLayout? Layout);

/// <summary>
/// Masaüstündeki takvimin tamamını yönetir: her monitör için zemin ve takvimi
/// tek bir görüntüde birleştirir, bunu <see cref="WallpaperCanvas"/> ile duvar
/// kağıdına yazar ve ekran yapılandırması değiştiğinde her şeyi yeniler.
///
/// Takvimin ekranda hangi piksellerde durduğunu da burası bilir; gün kutusu,
/// sağ tık menüsü ve ay düğmeleri <see cref="HitTest"/>, <see cref="HitTestBlock"/>
/// ve <see cref="HitTestNav"/> üzerinden buraya soruyor.
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

    /// <summary>
    /// Monitör kimliği okunamadığında çizimin en fazla ne kadar erteleneceği.
    /// Süre dolarsa (kimliği hiç okunmayan bir ekran) aygıt adıyla çizilir.
    /// </summary>
    private static readonly TimeSpan KimlikBeklemeSuresi = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Ekranda gerçekten bizim görüntümüzün durduğu kaç nöbet turunda bir
    /// denetlenir (8 sn × 4, yaklaşık yarım dakika).
    /// </summary>
    private const int DuvarKagidiDenetimAraligi = 4;

    /// <summary>Windows'un hiç monitör bağlı değilken gösterdiği sanal ekranın aygıt adı.</summary>
    private const string BaglantisizEkran = "WinDisc";

    private readonly SettingsStore _settings;
    private readonly DesktopSurfaceRenderer _renderer = new();
    private readonly WallpaperCanvas _canvas = new();
    private readonly List<EkrandakiTakvim> _screens = new();
    private readonly DispatcherTimer _rebuildTimer;
    private readonly DispatcherTimer _watchdogTimer;
    private readonly DispatcherTimer _tazelemeTimer;

    private IReadOnlyList<MonitorTarget> _monitors = Array.Empty<MonitorTarget>();
    private CalendarDataSnapshot _data = CalendarDataSnapshot.Empty;
    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);
    private DateOnly _viewMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);
    private DateTime _rebuildDeadline = DateTime.MinValue;
    private string _configSignature = string.Empty;
    private bool _unavailableReported;
    private bool _disposed;

    /// <summary>Aygıt adı -> bu süreçte o adla en son okunan donanım kimliği.</summary>
    private readonly Dictionary<string, string> _bilinenKimlikler = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Kimliği okunamayan bir ekran yüzünden ne zamandan beri bekleniyor.</summary>
    private DateTime? _kimlikBeklemeBasi;

    /// <summary>Bekleme süresi doldu, geçici adla çiziliyor.</summary>
    private bool _aygitAdiylaCiziliyor;

    private int _nobetSayaci;

    /// <summary>Takvim duvar kâğıdından kaldırılıyor; bu sırada gelen çizim yeniden kaldırmaya kalkmasın.</summary>
    private bool _geriYukleniyor;

    /// <summary>Kimliği okunamayan bir ekran yüzünden çizim şu an erteleniyor mu?</summary>
    private bool Beklemede => _kimlikBeklemeBasi is not null && !_aygitAdiylaCiziliyor;

    /// <summary>
    /// Takvim masaüstünde görünüyor mu? Duvar kâğıdına yazma kapalıyken
    /// (tepsideki "Eski duvar kâğıdıma dön" ya da ayarlardaki anahtar) takvimin
    /// eski yeri boş masaüstüdür: sağ tık, ok tıklaması ve gün kutusu orada
    /// çalışmamalı. Yazma açıkken ama duvar kâğıdına yazılamadığında isabet
    /// yine verilir (bkz. <see cref="RefreshOnce"/>).
    /// </summary>
    private bool TakvimGorunuyor => _settings.Current.SyncSystemWallpaper;

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
        _watchdogTimer.Tick += (_, _) => Nobet();
        _watchdogTimer.Start();

        _tazelemeTimer = new DispatcherTimer(DispatcherPriority.Background);
        _tazelemeTimer.Tick += (_, _) => ZorlaTazele();
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
    /// aydır; ay düğmeleriyle ya da sağ tık menüsünden ileri geri gidildiğinde
    /// geçici olarak değişir.
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

        IReadOnlyList<MonitorTarget> monitors = EkranlariOku();
        if (monitors.Count == 0)
        {
            Log.Warn("Hiç monitör bulunamadı; takvim çizilemiyor.");
            ReportUnavailable();
            return;
        }

        if (KimlikBekleniyor(monitors))
        {
            return;
        }

        string signature = BuildConfigSignature(monitors);
        bool configChanged = signature != _configSignature;
        _configSignature = signature;
        _monitors = monitors;

        if (configChanged)
        {
            // Görüntü özetleri monitör kimliğine göre tutuluyor. Kimlik bir süre
            // başka bir ad taşıyıp geri dönünce eski kimliğin özeti yeni
            // görüntüyle aynı çıkıyor ve yazma atlanıyordu; ekranda arada
            // yazılmış, takvimi yanlış yerde gösteren görüntü kalıyordu.
            _canvas.Forget();
        }

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

                    // Geçici ad ayarlara yazılmıyor: kimlik okununca monitör kendi
                    // kaydıyla geri geliyor, bu kayıt yalnızca kalabalık ediyordu.
                    if (!monitor.KimlikOkunamadi)
                    {
                        settings.Monitors[monitor.StableId] = placement;
                        placementsAdded = true;
                    }
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
        _screens.Clear();
        _screens.AddRange(fresh.Select(s => new EkrandakiTakvim(s.Monitor, s.Surface.Layout)));

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

        // Görüntüler yazıldı; bundan sonra yalnızca takvimin yeri gerekiyor.
        foreach (ScreenSurface s in fresh)
        {
            s.Surface.Dispose();
        }

        // "Takvimi Windows duvar kâğıdına işle" bu süreç takvimi yazdıktan sonra
        // kapatıldı. Yalnız yazmayı bırakmak yetmiyordu: son yazılan takvim
        // duvar kâğıdında, uygulama kapansa da donmuş kalıyordu. Tepsideki "Eski
        // duvar kâğıdıma dön" gibi önceki duvar kâğıdı geri konur; anahtar
        // yeniden açılınca takvim baştan yazılır.
        if (!settings.SyncSystemWallpaper && _canvas.HasPainted && !_geriYukleniyor)
        {
            DuvarKagidiniGeriYukle();
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

        IReadOnlyList<MonitorTarget> monitors = EkranlariOku();
        if (BuildConfigSignature(monitors) != _configSignature)
        {
            // Kimlik beklenirken her denemede yazmıyoruz; bekleme bir kez yazıldı.
            if (!monitors.Any(m => m.KimlikOkunamadi))
            {
                Log.Info("Ekran yapılandırması bildirimsiz değişmiş, takvim yeniden kuruluyor.");
            }

            Rebuild();
        }
        else if (Beklemede && !monitors.Any(m => m.KimlikOkunamadi))
        {
            // Beklenen kimlik geldi, yapılandırma öncekiyle aynı. Beklerken
            // (ekran bağlı değilken) yazılan görüntü yerine ulaşmamış olabilir;
            // bir kez yeniden yazıyoruz.
            KimlikBekleniyor(monitors);
            _canvas.Forget();
            Refresh();
        }

        return _screens.Count > 0;
    }

    /// <summary>
    /// Verilen süre sonra görüntüyü, içeriği aynı olsa bile yeni bir dosyayla
    /// bir kez daha yazar ve masaüstünü yeniden boyatır.
    ///
    /// Açılışta ve uykudan uyanınca ilk yazılan görüntü bazen ekrana hiç
    /// gelmiyor: Explorer daha oturmadan yazılınca Windows bir önceki
    /// oturumdan kalan görüntüyü göstermeye devam ediyor ve takvim eski yerinde
    /// ya da eski hâliyle kalıyor. Windows'un kaydı bizim dosyamızı gösterdiği
    /// için yarım dakikalık denetim bunu her zaman yakalayamıyor.
    /// </summary>
    public void TazelemeIste(TimeSpan gecikme)
    {
        if (_disposed)
        {
            return;
        }

        _tazelemeTimer.Stop();
        _tazelemeTimer.Interval = gecikme;
        _tazelemeTimer.Start();
    }

    private void ZorlaTazele()
    {
        _tazelemeTimer.Stop();

        if (_disposed)
        {
            return;
        }

        if (Beklemede)
        {
            // Monitör henüz tanınmadı; tanınınca zaten yazılacak, biz yine de
            // biraz sonra bir kez daha deneriz.
            TazelemeIste(TimeSpan.FromSeconds(10));
            return;
        }

        if (_screens.Count == 0 || !_settings.Current.SyncSystemWallpaper)
        {
            return;
        }

        Log.Info("Görüntü güvence için yeniden yazılıyor.");
        _canvas.Forget();
        Refresh();
        DesktopRefresher.ForceRedraw();
    }

    /// <summary>
    /// Nöbet zamanlayıcısının her turu: ekran yapılandırmasına, birkaç turda bir
    /// de ekranda gerçekten bizim görüntümüzün durup durmadığına bakar.
    /// </summary>
    private void Nobet()
    {
        EnsureAlive();

        if (++_nobetSayaci % DuvarKagidiDenetimAraligi != 0)
        {
            return;
        }

        // Kimlik beklenirken monitör geçiş hâlinde; o arada okunan duvar kağıdı
        // yanıltıcı olabilir.
        if (_screens.Count > 0
            && !Beklemede
            && _settings.Current.SyncSystemWallpaper
            && !_canvas.EkrandakilerBizimMi())
        {
            _canvas.Forget();
            Refresh();
        }
    }

    /// <summary>
    /// Monitörleri okur. Kimliği o an okunamayan ekrana, aynı aygıt adıyla bu
    /// süreçte daha önce okunmuş donanım kimliğini verir: ekran kapanıp
    /// açılırken Windows aygıt adını (\\.\DISPLAY1) koruyor, kimliği bir süre
    /// vermiyor.
    /// </summary>
    private IReadOnlyList<MonitorTarget> EkranlariOku()
    {
        IReadOnlyList<MonitorTarget> monitors = MonitorEnumerator.All();
        if (!monitors.Any(m => m.KimlikOkunamadi))
        {
            return monitors;
        }

        return monitors
            .Select(m => m.KimlikOkunamadi && _bilinenKimlikler.TryGetValue(m.DeviceName, out string? kimlik)
                ? m.KimlikIle(kimlik)
                : m)
            .ToList();
    }

    /// <summary>
    /// Kimliği okunamayan bir ekran varsa çizimi erteler; nöbet zamanlayıcısı
    /// birkaç saniyede bir yeniden dener.
    ///
    /// Ekran kapanırken, uyanırken ya da açılışta monitör henüz hazır değilken
    /// Windows donanım kimliğini birkaç saniye vermiyor. O arada çizilince
    /// takvim geçici ada ait kayda, yani varsayılan yere konuyordu; kimlik geri
    /// gelince de içerik aynı olduğu için düzeltilmiyordu. Kimlik
    /// <see cref="KimlikBeklemeSuresi"/> boyunca gelmezse (kimliği hiç okunmayan
    /// bir ekran) eskisi gibi aygıt adıyla çizilir.
    /// </summary>
    /// <returns>Beklenecekse true; o turda hiçbir şey çizilmez.</returns>
    private bool KimlikBekleniyor(IReadOnlyList<MonitorTarget> monitors)
    {
        foreach (MonitorTarget m in monitors.Where(m => !m.KimlikOkunamadi))
        {
            _bilinenKimlikler[m.DeviceName] = m.StableId;
        }

        if (!monitors.Any(m => m.KimlikOkunamadi))
        {
            _kimlikBeklemeBasi = null;
            _aygitAdiylaCiziliyor = false;
            return false;
        }

        DateTime simdi = DateTime.UtcNow;
        if (_kimlikBeklemeBasi is null)
        {
            _kimlikBeklemeBasi = simdi;
            Log.Info("Monitör kimliği okunamadı, ekran hazır olunca çizilecek: "
                + string.Join(", ", monitors.Where(m => m.KimlikOkunamadi).Select(m => m.DeviceName)));
        }

        // "WinDisc", Windows'un hiç ekran bağlı değilken koyduğu sanal ekran:
        // monitör kapalı ya da kablosu çekili. Ona çizmenin anlamı yok; gerçek
        // monitör gelene kadar süre sınırı olmadan bekleniyor.
        if (monitors.Any(m => m.KimlikOkunamadi
                && string.Equals(m.DeviceName, BaglantisizEkran, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (simdi - _kimlikBeklemeBasi.Value < KimlikBeklemeSuresi)
        {
            return true;
        }

        if (!_aygitAdiylaCiziliyor)
        {
            _aygitAdiylaCiziliyor = true;
            Log.Info("Monitör kimliği hâlâ okunamıyor; takvim aygıt adıyla çiziliyor.");
        }

        return false;
    }

    /// <summary>
    /// Ekran koordinatındaki noktanın hangi güne denk geldiğini bulur.
    /// Gün kutusu bunu kullanır. Takvim masaüstünde değilken isabet verilmez.
    /// </summary>
    public DayHit? HitTest(Point screenPoint)
    {
        if (!TakvimGorunuyor)
        {
            return null;
        }

        foreach (EkrandakiTakvim screen in _screens)
        {
            if (!screen.Monitor.Bounds.Contains(screenPoint))
            {
                continue;
            }

            CalendarLayout? layout = screen.Layout;
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
    /// açılır, masaüstünün geri kalanı Windows'a bırakılır. Takvim masaüstünde
    /// değilken isabet verilmez.
    /// </summary>
    public CalendarBlockHit? HitTestBlock(Point screenPoint)
    {
        if (!TakvimGorunuyor)
        {
            return null;
        }

        foreach (EkrandakiTakvim screen in _screens)
        {
            if (!screen.Monitor.Bounds.Contains(screenPoint))
            {
                continue;
            }

            CalendarLayout? layout = screen.Layout;
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

    /// <summary>
    /// Ekran koordinatındaki nokta hangi ay düğmesinin üzerinde? Takvim
    /// masaüstünde değilken isabet verilmez.
    /// </summary>
    public NavButton HitTestNav(Point screenPoint)
    {
        if (!TakvimGorunuyor)
        {
            return NavButton.None;
        }

        foreach (EkrandakiTakvim screen in _screens)
        {
            if (screen.Monitor.Bounds.Contains(screenPoint))
            {
                return screen.Layout?.HitTestNav(ToLocal(screen.Monitor, screenPoint)) ?? NavButton.None;
            }
        }

        return NavButton.None;
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
    /// Takvimi duvar kâğıdından kaldırır: kullanıcının önceki duvar kâğıdı geri
    /// yüklenir. Tepsideki "Eski duvar kâğıdıma dön" ve ayarlarda "Takvimi
    /// Windows duvar kâğıdına işle"nin kapatılması bunu kullanır. Yedek yoksa ya
    /// da bir ekran geri yüklenemediyse (fotoğrafı silinmiş) o ekrana takvimsiz
    /// zemin yazılır; ekranda donmuş bir takvim kalmasın. Çağırmadan önce yazma
    /// kapatılmış olmalı, yoksa bir sonraki çizim takvimi yeniden yazar.
    /// </summary>
    /// <returns>Önceki duvar kâğıdı geri yüklendiyse true.</returns>
    public bool DuvarKagidiniGeriYukle()
    {
        _geriYukleniyor = true;
        try
        {
            bool yuklendi = WallpaperService.Restore(out bool eksikKaldi);

            if (eksikKaldi && _canvas.HasPainted)
            {
                HashSet<string> duranlar = _canvas.EkrandaDuranlar();
                if (duranlar.Count > 0)
                {
                    PaintPlainBackground(duranlar);
                }
            }

            _canvas.Birak();
            return yuklendi;
        }
        finally
        {
            _geriYukleniyor = false;
        }
    }

    /// <summary>
    /// Takvimsiz zemini duvar kâğıdına yazar. Kapanışta ve takvim duvar
    /// kâğıdından kaldırılırken çağrılıyor: takvim duvar kâğıdının parçası
    /// olduğu için, uygulama kapandıktan sonra ekranda donmuş bir takvim kalmasın.
    /// </summary>
    /// <param name="ekranlar">Yazılacak ekranların kimlikleri (hâlâ takvimi gösterenler).</param>
    private void PaintPlainBackground(HashSet<string> ekranlar)
    {
        try
        {
            AppSettings settings = _settings.Current;
            var plain = new List<ScreenSurface>();

            foreach (EkrandakiTakvim screen in _screens.Where(s => ekranlar.Contains(s.Monitor.StableId)))
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

            _canvas.Apply(plain.Select(s => new CanvasScreen(s.Monitor, s.Surface.Image)).ToList(), kaldirirken: true);

            foreach (ScreenSurface s in plain)
            {
                s.Surface.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Takvimsiz zemin yazılamadı.", ex);
        }
    }

    /// <summary>
    /// Kapanışta takvim duvar kağıdında bırakılsın mı? Güncellemede doğru:
    /// yeni sürüm birkaç saniye içinde aynısını çizecek; arada takvimin silinip
    /// geri gelmesi göz kırpması gibi görünürdü.
    /// </summary>
    public bool KeepWallpaperOnExit { get; set; }

    public void Dispose()
    {
        _disposed = true;
        _rebuildTimer.Stop();
        _watchdogTimer.Stop();
        _tazelemeTimer.Stop();

        // Takvimi duvar kağıdından siliyoruz; yoksa yarın da bugünün takvimi
        // ekranda asılı kalır. Ayara değil ekrana bakılıyor: yazma kapatılınca
        // önceden takvim hiç silinmiyordu. Önceki duvar kâğıdı geri gelen ya da
        // kullanıcının başka bir duvar kâğıdı seçtiği ekrana dokunulmaz.
        if (!KeepWallpaperOnExit && _canvas.HasPainted)
        {
            HashSet<string> duranlar = _canvas.EkrandaDuranlar();
            if (duranlar.Count > 0)
            {
                PaintPlainBackground(duranlar);
            }
        }

        _screens.Clear();
        _renderer.Dispose();
    }
}
