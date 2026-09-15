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

    // ------------------------------------------------------------------ Google

    bool GoogleConnected { get; }

    bool GoogleOnline { get; }

    string? GoogleAccountEmail { get; }

    IReadOnlyList<Google.ProviderCalendar> GoogleCalendars { get; }

    IReadOnlyList<Google.ProviderTaskList> GoogleTaskLists { get; }

    int GoogleEventCount { get; }

    int GoogleTaskCount { get; }

    /// <summary>Google bağlantı durumu ya da verisi değişti.</summary>
    event Action? GoogleStateChanged;

    /// <summary>
    /// Tarayıcıda onay açarak bağlanır ve ilk veriyi alır. Aşama değiştikçe
    /// <paramref name="progress"/> ile kullanıcıya gösterilecek metin verilir.
    /// </summary>
    Task<Google.SignInResult> ConnectGoogleAsync(Action<string>? progress, CancellationToken cancel);

    /// <summary>Bağlantıyı keser ve yerel veriyi siler.</summary>
    void DisconnectGoogle();

    Task RefreshGoogleAsync(bool force);

    /// <summary>Yoklama aralığı ayarı değişti.</summary>
    void ApplyPollInterval();

    /// <summary>Ayarları kaydeder, duvar kağıdını ve masaüstü çizimini tazeler.</summary>
    void ApplyAppearance();
}
