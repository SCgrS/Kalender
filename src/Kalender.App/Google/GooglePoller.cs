using System.Windows.Threading;
using Kalender.Calendar;
using Kalender.Core;
using Kalender.Settings;

namespace Kalender.Google;

/// <summary>
/// Google verisini belirli aralıklarla tazeler.
///
/// Zamanlayıcı uyku sırasında durur, uyanınca tek seferlik yenileme yapılır.
/// Bağlantı yoksa hiç çalışmaz; böylece bağlanmamış kurulumda ağa hiç
/// çıkılmaz ve boştaki tüketim sıfır kalır.
/// </summary>
internal sealed class GooglePoller : IDisposable
{
    /// <summary>Ayarlarda sunulan yoklama aralıkları (saniye).</summary>
    public static readonly (int Seconds, string Label)[] Intervals =
    {
        (30, "30 saniye"),
        (60, "1 dakika"),
        (300, "5 dakika"),
        (900, "15 dakika"),
        (86400, "24 saat"),
    };

    private readonly GoogleCalendarProvider _provider;
    private readonly CalendarDataService _data;
    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _timer;

    private CancellationTokenSource? _running;
    private Task? _current;

    /// <summary>Süren yenilemenin istediği gün aralığı.</summary>
    private (DateOnly First, DateOnly Last) _currentRange;

    private bool _disposed;

    /// <summary>
    /// Bu süre boyunca hiçbir eşitleme tutmazsa günlüğe hata olarak yazılır.
    /// Tek tek başarısızlıklar uyarı düzeyinde kalıyor ve varsayılan günlük
    /// düzeyinde (yalnız hatalar) görünmüyor; 2026-10-01'de takvim saatlerce
    /// eski veriyle kaldığı halde günlükte iz yoktu.
    /// </summary>
    internal static TimeSpan HataBildirmeSuresi { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Kesintisiz başarısızlığın başladığı an; eşitleme tutunca sıfırlanır.</summary>
    private DateTimeOffset? _ilkHataUtc;

    private bool _hataBildirildi;

    public GooglePoller(GoogleCalendarProvider provider, CalendarDataService data, SettingsStore settings)
    {
        _provider = provider;
        _data = data;
        _settings = settings;

        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += (_, _) => _ = RefreshAsync(force: false);
    }

    /// <summary>Yoklama sonucu değişti; tepsi durumu güncellenmeli.</summary>
    public event Action? StatusChanged;

    public bool IsOnline => _provider.IsOnline;

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        ApplyInterval();

        // Uykudan uyanınca ya da yeniden bağlanınca sayaç baştan: uykudan önceki
        // son başarısızlık, uyanır uyanmaz ağ hazır olmadan gelen ilk hatayla
        // birleşip "dakikalardır başarısız" sayılmasın.
        _ilkHataUtc = null;

        if (!_provider.IsConnected)
        {
            _timer.Stop();
            return;
        }

        _timer.Start();
        _ = RefreshAsync(force: false);
    }

    public void Stop() => _timer.Stop();

    /// <summary>Ayar değişince aralığı yeniden okur.</summary>
    public void ApplyInterval()
    {
        int seconds = _settings.Current.Google.PollIntervalSeconds;
        if (Intervals.All(i => i.Seconds != seconds))
        {
            seconds = 60;
        }

        _timer.Interval = TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Şimdi yenile. Süren bir yenileme varsa ve bu istek ondan fazlasını
    /// gerektirmiyorsa ona katılır; döndürülen görev süren işin bitişini bekler.
    /// Önceden her istek bir öncekini iptal ediyordu: bağlanma anında art arda
    /// gelen iki istek birbirini düşürüyor, ilk veri bir sonraki yoklamaya
    /// kalıyordu. Zorlanan (tam) yenileme süren işe hiç katılmaz, onu iptal
    /// eder; sağlayıcı kapıyı bekleyerek sıraya girer. Zorlanan istek (ör.
    /// takvim seçimi değişti) süren işin bilmediği bir değişikliği taşıyor:
    /// önceden kendinden önce başlamış zorlanmış işe katılıyor, yeni seçim
    /// 30 dakikaya kadar uygulanmıyordu.
    ///
    /// Süren iş başka bir ay içinse de katılınmaz, yerine yenisi başlar: ay
    /// düğmeleriyle gezinirken yoklama sürüyorsa yeni ayın verisi önceden bir
    /// sonraki yoklamaya (30 sn–24 saat) kalıyordu.
    /// </summary>
    public Task RefreshAsync(bool force)
    {
        if (_disposed || !_provider.IsConnected)
        {
            return Task.CompletedTask;
        }

        (DateOnly First, DateOnly Last) range = _data.VisibleRange;

        Task? running = _current;
        if (running is { IsCompleted: false } && !force && range == _currentRange)
        {
            return running;
        }

        _currentRange = range;
        _current = RunAsync(force);
        return _current;
    }

    private async Task RunAsync(bool force)
    {
        CancellationTokenSource cts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _running, cts);

        // Öncekini yalnızca iptal ediyoruz; kaynağını kendi işi bitince kendisi
        // kapatır. Hemen kapatılsaydı süren iş iptal yerine "kapatılmış nesne"
        // hatasıyla düşüyor ve günlüğe boş yere uyarı yazılıyordu.
        previous?.Cancel();

        try
        {
            (DateOnly first, DateOnly last) = _data.VisibleRange;
            bool changed = await _provider.SyncAsync(first, last, force, cts.Token).ConfigureAwait(true);

            if (changed)
            {
                _data.Rebuild();
            }

            if (!cts.IsCancellationRequested)
            {
                SonucuIzle(_provider.SonHata, DateTimeOffset.UtcNow);
            }

            StatusChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Yeni bir yenileme başladı.
        }
        catch (Exception ex)
        {
            Log.Warn("Google yoklaması başarısız.", ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _running, null, cts);
            cts.Dispose();
        }
    }

    /// <summary>
    /// Eşitleme <see cref="HataBildirmeSuresi"/> boyunca hiç tutmadıysa bunu bir
    /// kez hata olarak yazar; yeniden tutunca bunu da yazar ki kesintinin ne
    /// kadar sürdüğü günlükten okunabilsin.
    /// </summary>
    internal void SonucuIzle(string? hata, DateTimeOffset simdi)
    {
        if (hata is null)
        {
            if (_hataBildirildi && _ilkHataUtc is DateTimeOffset basladi)
            {
                Log.Error($"Google eşitlemesi yeniden çalışıyor ({Sure(simdi - basladi)} sürdü).");
            }

            _ilkHataUtc = null;
            _hataBildirildi = false;
            return;
        }

        _ilkHataUtc ??= simdi;

        if (!_hataBildirildi && simdi - _ilkHataUtc.Value >= HataBildirmeSuresi)
        {
            _hataBildirildi = true;
            Log.Error($"Google eşitlemesi {Sure(simdi - _ilkHataUtc.Value)} boyunca hiç tutmadı; takvim son "
                + $"alınan veriyle gösteriliyor. Son hata: {hata}");
        }
    }

    private static string Sure(TimeSpan sure)
        => sure.TotalHours >= 1
            ? $"{(int)sure.TotalHours} sa {sure.Minutes} dk"
            : $"{Math.Max(1, (int)Math.Round(sure.TotalMinutes))} dk";

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _running?.Cancel();
        _running?.Dispose();
    }
}
