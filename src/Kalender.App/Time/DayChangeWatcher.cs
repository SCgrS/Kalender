using System.Windows.Threading;
using Kalender.Core;

namespace Kalender.Time;

/// <summary>
/// Gün ve ay değişimini izler. Tek bir zamanlayıcı kurulur ve gece yarısına
/// kalan süre kadar bekler; sürekli yoklama yapılmaz, bu yüzden boştaki
/// işlemci kullanımı sıfırdır.
///
/// Sistem saatinin ya da saat diliminin değişmesi, uykudan uyanma gibi
/// durumlarda dışarıdan <see cref="Recheck"/> çağrılır; hesap yeniden yapılır.
/// </summary>
internal sealed class DayChangeWatcher : IDisposable
{
    /// <summary>Zamanlayıcı en fazla bu kadar uzun beklesin; uzun uykulardan sonra kendini toparlasın diye.</summary>
    private static readonly TimeSpan MaxInterval = TimeSpan.FromHours(6);

    private readonly DispatcherTimer _timer;
    private DateOnly _current;

    public DayChangeWatcher()
    {
        _current = DateOnly.FromDateTime(DateTime.Now);
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += (_, _) => Recheck();
        Arm();
    }

    /// <summary>Gün değiştiğinde yeni tarihle tetiklenir.</summary>
    public event Action<DateOnly>? DayChanged;

    /// <summary>
    /// Windows'un saat dilimi (ya da yaz saati kuralı) değişti. Gün de
    /// değiştiyse <see cref="DayChanged"/>'den sonra tetiklenir.
    /// </summary>
    public event Action? TimeZoneChanged;

    public DateOnly Today => _current;

    /// <summary>Tarihi yeniden okur; değiştiyse haber verir ve zamanlayıcıyı kurar.</summary>
    public void Recheck()
    {
        // .NET yerel saat dilimini ilk kullanımda önbelleğe alıyor ve kendiliğinden
        // tazelemiyor: Windows'ta dilim değişince DateTime.Now uygulama yeniden
        // açılana dek eski dilimin farkıyla dönüyor, gün ve gece yarısı
        // zamanlayıcısı saat farkı kadar geç (ya da erken) dönüyordu. Önbellek
        // her yeniden hesapta temizleniyor; dilim Windows'tan yeniden okunur.
        TimeZoneInfo onceki = TimeZoneInfo.Local;
        TimeZoneInfo.ClearCachedData();
        TimeZoneInfo simdiki = TimeZoneInfo.Local;
        bool dilimDegisti = onceki.Id != simdiki.Id || !onceki.HasSameRules(simdiki);
        if (dilimDegisti)
        {
            Log.Info($"Saat dilimi değişti: {onceki.Id} → {simdiki.Id}");
        }

        DateOnly now = DateOnly.FromDateTime(DateTime.Now);
        if (now != _current)
        {
            _current = now;
            Log.Info($"Gün değişti: {now:yyyy-MM-dd}");
            DayChanged?.Invoke(now);
        }

        Arm();

        if (dilimDegisti)
        {
            TimeZoneChanged?.Invoke();
        }
    }

    private void Arm()
    {
        DateTime now = DateTime.Now;
        DateTime nextMidnight = now.Date.AddDays(1).AddSeconds(2);
        TimeSpan wait = nextMidnight - now;

        if (wait <= TimeSpan.Zero || wait > MaxInterval)
        {
            wait = MaxInterval;
        }

        _timer.Stop();
        _timer.Interval = wait;
        _timer.Start();
    }

    public void Dispose() => _timer.Stop();
}
