using Kalender.Calendar;
using Kalender.Interop;
using Kalender.Settings;

namespace Kalender.Ui;

/// <summary>
/// Ayarlar penceresinin uygulamanın geri kalanına açılan dar kapısı.
/// Pencere ne masaüstü katmanını ne de duvar kağıdını doğrudan tanır;
/// yalnızca "ayarları değiştirdim, uygula" diyebilir.
/// </summary>
internal interface IAppearanceHost
{
    SettingsStore Settings { get; }

    /// <summary>Şu an bağlı monitörler.</summary>
    IReadOnlyList<MonitorTarget> Monitors { get; }

    /// <summary>
    /// Ekran listesi değişti (çözünürlük, ölçek, monitör tak/çıkar).
    /// Ayarlar penceresi satırlarını tazelemek için buna kulak verir; yoksa
    /// eski ölçülerle çalışır ve takvimi yanlış yere koyar.
    /// </summary>
    event Action? MonitorsChanged;

    /// <summary>Takvim önizlemelerinde kullanılacak gün verisi.</summary>
    CalendarDataSnapshot Data { get; }

    DateOnly Today { get; }

    /// <summary>Tatil listesinin en son ne zaman indirildiği; hiç indirilmediyse null.</summary>
    DateTimeOffset? HolidaysLastFetchUtc { get; }

    /// <summary>Önbellekteki tatil günü sayısı.</summary>
    int HolidayCount { get; }

    /// <summary>Tatil listesi yenilendi.</summary>
    event Action? HolidaysUpdated;

    /// <summary>Tatil listesini indirir. force=true süresi dolmamış olsa bile indirir.</summary>
    Task RefreshHolidaysAsync(bool force);

    /// <summary>Ayarları kaydeder, duvar kağıdını ve masaüstü çizimini tazeler.</summary>
    void ApplyAppearance();
}
