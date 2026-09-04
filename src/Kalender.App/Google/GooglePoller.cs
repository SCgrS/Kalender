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
    private bool _disposed;

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

    /// <summary>Şimdi yenile.</summary>
    public async Task RefreshAsync(bool force)
    {
        if (_disposed || !_provider.IsConnected)
        {
            return;
        }

        CancellationTokenSource cts = new();
        CancellationTokenSource? previous = Interlocked.Exchange(ref _running, cts);
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            (DateOnly first, DateOnly last) = _data.VisibleRange;
            bool changed = await _provider.SyncAsync(first, last, force, cts.Token).ConfigureAwait(true);

            if (changed)
            {
                _data.Rebuild();
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
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _running?.Cancel();
        _running?.Dispose();
    }
}
