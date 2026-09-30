using System.Text.Json.Serialization;

namespace Kalender.Settings;

/// <summary>Zeminin nasıl doldurulacağı.</summary>
internal enum BackgroundMode
{
    /// <summary>Düz renk.</summary>
    Solid = 0,

    /// <summary>Kullanıcının seçtiği fotoğraf.</summary>
    Photo = 1,
}

/// <summary>Fotoğrafın ekrana nasıl yerleştirileceği.</summary>
internal enum PhotoFit
{
    /// <summary>Kapla: en-boy oranı korunur, ekranı doldurur, taşan kısım kırpılır.</summary>
    Fill = 0,

    /// <summary>Sığdır: en-boy oranı korunur, tamamı görünür, kenarlarda boşluk kalır.</summary>
    Fit = 1,

    /// <summary>Ortala: özgün boyutunda ortalanır.</summary>
    Center = 2,

    /// <summary>Döşe: özgün boyutunda yan yana tekrarlanır.</summary>
    Tile = 3,
}

/// <summary>Yazı renginin nasıl belirleneceği.</summary>
internal enum TextColorMode
{
    /// <summary>Zeminin parlaklığına göre koyu ya da açık seçilir.</summary>
    Auto = 0,

    /// <summary>Kullanıcının seçtiği renk kullanılır.</summary>
    Manual = 1,
}

/// <summary>Takvimin hangi ekranlarda gösterileceği.</summary>
internal enum DisplayTarget
{
    AllScreens = 0,
    PrimaryOnly = 1,
    SelectedScreens = 2,
}

/// <summary>Bir monitöre özel yerleşim ve zemin ayarları.</summary>
internal sealed class MonitorPlacement
{
    /// <summary>
    /// Takvim bloğunun sol üst köşesi, monitör genişliğine oranla (0-1).
    /// Asıl varsayılan ekrana göre <see cref="PlacementDefaults"/>'ta
    /// hesaplanır; buradaki değer onun 2560 × 1440 ekrandaki karşılığı.
    /// </summary>
    public double XRatio { get; set; } = 0.6934;

    /// <summary>Takvim bloğunun sol üst köşesi, monitör yüksekliğine oranla (0-1).</summary>
    public double YRatio { get; set; } = 0.4514;

    /// <summary>Takvim boyutu yüzdesi (80-150).</summary>
    public int ScalePercent { get; set; } = 100;

    /// <summary>Bu monitör için fotoğraf yolu. Boşsa genel ayar kullanılır.</summary>
    public string? PhotoPath { get; set; }

    /// <summary>Bu monitördeki fotoğrafın yerleşim biçimi.</summary>
    public PhotoFit PhotoFit { get; set; } = PhotoFit.Fill;

    /// <summary>Bu monitörde takvim gösterilsin mi (SelectedScreens kipinde kullanılır).</summary>
    public bool Enabled { get; set; } = true;

    public MonitorPlacement Clone() => (MonitorPlacement)MemberwiseClone();
}

/// <summary>
/// Takvim yazılarının renkleri. Her biri boş bırakılabilir; boşken renk
/// zemine ve yazı rengine göre kendiliğinden hesaplanır (varsayılan davranış).
///
/// Noktacıkların rengi burada yok: onlar Google Takvim'deki takvim ve
/// etkinlik renklerinden gelir, o yüzden elle değiştirilemez.
/// </summary>
internal sealed class CalendarTextColors
{
    /// <summary>Ay adı, gün adları ve içinde bulunulan ayın normal günleri.</summary>
    public string? Weekday { get; set; }

    /// <summary>Hafta sonu ve resmi tatil günlerinin rakamları ile gün adları.</summary>
    public string? Holiday { get; set; }

    /// <summary>Önceki ve sonraki aya taşan (soluk) günler.</summary>
    public string? Muted { get; set; }

    /// <summary>Bugünün arkasındaki dolu daire.</summary>
    public string? TodayCircle { get; set; }
}

/// <summary>Görünüm ayarları.</summary>
internal sealed class AppearanceSettings
{
    public BackgroundMode BackgroundMode { get; set; } = BackgroundMode.Solid;

    /// <summary>Düz renk kipindeki zemin rengi (#RRGGBB).</summary>
    public string SolidColor { get; set; } = "#2E3238";

    /// <summary>Seçili hazır temanın kimliği; özel renkte boş kalır.</summary>
    public string? PresetId { get; set; } = "komur";

    public TextColorMode TextColorMode { get; set; } = TextColorMode.Auto;

    /// <summary>Elle seçilmiş yazı rengi (#RRGGBB).</summary>
    public string TextColor { get; set; } = "#FFFFFF";

    /// <summary>Takvim yazılarının renk seçimleri; boş alanlar otomatik hesaplanır.</summary>
    public CalendarTextColors Colors { get; set; } = new();

    /// <summary>Cumartesi de pazar gibi kırmızı gösterilsin mi?</summary>
    public bool SaturdayIsRed { get; set; } = true;

    /// <summary>Fotoğraf kipinde takvimin arkasına uygulanan karartma (0-100).</summary>
    public int BackdropDim { get; set; }

    /// <summary>
    /// Gün kutusunun zemin opaklığı (40-100). Yalnızca kutunun zeminini ve
    /// kenarlığını etkiler; yazılar her değerde tam opak kalır, yoksa saydam
    /// zeminde okunmuyorlar.
    /// </summary>
    public int DayPopupOpacity { get; set; } = 95;

    /// <summary>Genel fotoğraf yolu (monitöre özel yol yoksa bu kullanılır).</summary>
    public string? PhotoPath { get; set; }

    public PhotoFit PhotoFit { get; set; } = PhotoFit.Fill;

    public DisplayTarget DisplayTarget { get; set; } = DisplayTarget.AllScreens;
}

/// <summary>Google bağlantısı ve yoklama ayarları.</summary>
internal sealed class GoogleSettings
{
    /// <summary>Kullanıcının kendi Google Cloud istemci kimliği.</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Kullanıcının kendi istemci parolası. Uygulamaya gömülü değildir;
    /// Google masaüstü istemcilerinde bunu istediği için saklanır.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>Yoklama aralığı (saniye). 30, 60, 300, 900 ya da 86400.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>Gösterilecek takvimlerin kimlikleri. Boşsa hepsi gösterilir.</summary>
    public List<string> SelectedCalendarIds { get; set; } = new();

    /// <summary>Gösterilecek görev listelerinin kimlikleri. Boşsa hepsi gösterilir.</summary>
    public List<string> SelectedTaskListIds { get; set; } = new();

    /// <summary>Görev noktacıklarının rengi (#RRGGBB).</summary>
    public string TaskColor { get; set; } = "#4C8DFF";

    /// <summary>Bağlı hesabın e-posta adresi; yalnızca ayarlarda göstermek için.</summary>
    public string? AccountEmail { get; set; }
}

/// <summary>Resmi tatil ayarları.</summary>
internal sealed class HolidaySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Tatil listesinin en son ne zaman indirildiği.</summary>
    public DateTimeOffset? LastFetchUtc { get; set; }
}

/// <summary>Tüm uygulama ayarları. Diskte ayarlar.json olarak durur.</summary>
internal sealed class AppSettings
{
    /// <summary>Ayar dosyası biçim sürümü; ileride göç gerekirse kullanılır.</summary>
    public int Version { get; set; } = 1;

    public AppearanceSettings Appearance { get; set; } = new();

    public GoogleSettings Google { get; set; } = new();

    public HolidaySettings Holidays { get; set; } = new();

    /// <summary>Monitör kimliği -> yerleşim.</summary>
    public Dictionary<string, MonitorPlacement> Monitors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Windows açılışında kendiliğinden başlasın mı?</summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Takvim çizimi geçici olarak gizlendi mi?</summary>
    public bool CalendarHidden { get; set; }

    /// <summary>
    /// Takvim Windows duvar kağıdına işlensin mi? Takvimin masaüstünde
    /// görünmesinin tek yolu bu olduğu için kapatmak takvimi de kaldırır;
    /// karşılığında Kalender duvar kağıdınıza hiç dokunmaz.
    /// </summary>
    public bool SyncSystemWallpaper { get; set; } = true;

    /// <summary>Günlük ayrıntı seviyesi: 0 Ayrıntı, 1 Bilgi, 2 Uyarı, 3 Hata, 4 Kapalı.</summary>
    public int LogLevel { get; set; } = 3;

    /// <summary>İlk çalıştırmada karşılama bilgisi gösterildi mi?</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>
    /// Ayarlar penceresinin görünümü: null ise Windows'un uygulama teması
    /// izlenir, sonra kullanıcının seçimi (true = karanlık) saklanır.
    /// </summary>
    public bool? DarkSettingsWindow { get; set; }

    [JsonIgnore]
    public MonitorPlacement DefaultPlacement { get; } = new();

    /// <summary>Verilen monitör için kayıtlı yerleşimi getirir, yoksa oluşturur.</summary>
    public MonitorPlacement PlacementFor(string monitorId)
    {
        if (!Monitors.TryGetValue(monitorId, out var placement))
        {
            placement = new MonitorPlacement();
            Monitors[monitorId] = placement;
        }

        return placement;
    }
}
