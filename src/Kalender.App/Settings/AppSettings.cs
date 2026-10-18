using System.Text.Json;
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

    /// <summary>
    /// Kullanılmıyor: ekrana özel fotoğraf da genel yerleşimle
    /// (<see cref="AppearanceSettings.PhotoFit"/>) çizilir; bunu değiştiren bir
    /// denetim hiç olmadı. Eski ayar dosyalarıyla uyum için duruyor.
    /// </summary>
    public PhotoFit PhotoFit { get; set; } = PhotoFit.Fill;

    /// <summary>Bu monitörde takvim gösterilsin mi (SelectedScreens kipinde kullanılır).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Bu sürümün tanımadığı alanlar; bkz. <see cref="AppSettings.BilinmeyenAlanlar"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? BilinmeyenAlanlar { get; set; }

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

    /// <summary>Bu sürümün tanımadığı alanlar; bkz. <see cref="AppSettings.BilinmeyenAlanlar"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? BilinmeyenAlanlar { get; set; }
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

    /// <summary>Bu sürümün tanımadığı alanlar; bkz. <see cref="AppSettings.BilinmeyenAlanlar"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? BilinmeyenAlanlar { get; set; }
}

/// <summary>Google bağlantısı ve yoklama ayarları.</summary>
internal sealed class GoogleSettings : IJsonOnDeserialized
{
    private const string KimlikAdi = "Google istemci kimliği";
    private const string EpostaAdi = "Google hesabının e-posta adresi";
    private const string TakvimlerAdi = "Seçili Google takvimleri";

    private string? _clientId;
    private string? _clientIdSifreli;

    /// <summary>
    /// Kullanıcının kendi Google Cloud istemci kimliği. Çözülmüş hâli yalnızca
    /// bellekte durur; ayar dosyasına <see cref="ClientIdSifreli"/> yazılır.
    /// </summary>
    [JsonIgnore]
    public string? ClientId
    {
        get => _clientId;
        set
        {
            _clientId = value;
            _clientIdSifreli = GizliDeger.Sifrele(value, KimlikAdi);
        }
    }

    /// <summary>İstemci kimliğinin Windows DPAPI ile şifrelenmiş hâli (Base64).</summary>
    public string? ClientIdSifreli
    {
        get => _clientIdSifreli;
        set
        {
            _clientIdSifreli = value;
            _clientId = GizliDeger.Coz(value, KimlikAdi);
        }
    }

    /// <summary>
    /// 1.16'ya kadar istemci kimliği "ClientId" adıyla düz metin yazılıyordu. Bu
    /// alan yalnızca o eski kaydı okumak için var (bkz. <see cref="EskiDuzMetinClientSecret"/>).
    /// </summary>
    /// <remarks>
    /// Okunan değer hemen değil, okuma bitince (<see cref="IJsonOnDeserialized.OnDeserialized"/>)
    /// uygulanır: 1.16'ya dönülüp yeniden geçilirse 1.16 tanımadığı şifreli alanı dosyanın
    /// sonuna olduğu gibi geri yazar; o eski değer 1.16'da girilen yenisini ezmemeli.
    /// Şifreleme yapılamazsa (DPAPI hatası) değer kaybolmasın diye 1.16'daki gibi düz
    /// yazılır; sonraki açılışta yeniden şifrelenmeye çalışılır.
    /// </remarks>
    [JsonPropertyName("ClientId")]
    public string? EskiDuzMetinClientId
    {
        get => _clientIdSifreli is null ? _clientId : null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _okunanDuzKimlik = value;
                DuzMetinOkundu = true;
            }
        }
    }

    private string? _okunanDuzKimlik;
    private string? _okunanDuzEposta;
    private List<string>? _okunanDuzTakvimler;

    private string? _clientSecret;
    private string? _clientSecretSifreli;

    /// <summary>
    /// Kullanıcının kendi istemci parolası. Uygulamaya gömülü değildir;
    /// Google masaüstü istemcilerinde bunu istediği için saklanır. Çözülmüş
    /// hâli yalnızca bellekte durur; ayar dosyasına
    /// <see cref="ClientSecretSifreli"/> yazılır.
    /// </summary>
    [JsonIgnore]
    public string? ClientSecret
    {
        get => _clientSecret;
        set
        {
            _clientSecret = value;
            _clientSecretSifreli = GizliDeger.Sifrele(value);
        }
    }

    /// <summary>
    /// İstemci parolasının Windows DPAPI ile şifrelenmiş hâli (Base64);
    /// yalnızca bu kullanıcı hesabı bu bilgisayarda çözebilir.
    /// </summary>
    public string? ClientSecretSifreli
    {
        get => _clientSecretSifreli;
        set
        {
            _clientSecretSifreli = value;
            _clientSecret = GizliDeger.Coz(value);
        }
    }

    /// <summary>
    /// 1.10'a kadar parola ayar dosyasına "ClientSecret" adıyla düz metin
    /// yazılıyordu. Bu alan yalnızca o eski kaydı okumak için var: okunan değer
    /// şifreli alana aktarılır, dosyaya bir daha düz metin yazılmaz
    /// (bkz. <see cref="SettingsStore.Load"/>).
    /// </summary>
    [JsonPropertyName("ClientSecret")]
    public string? EskiDuzMetinClientSecret
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                ClientSecret = value;
                DuzMetinOkundu = true;
            }
        }
    }

    /// <summary>
    /// Ayar dosyasında eski düz metin bir değer (gizli anahtar, istemci kimliği,
    /// e-posta ya da seçili takvimler) bulundu; dosya şifreli hâliyle yeniden yazılmalı.
    /// </summary>
    [JsonIgnore]
    public bool DuzMetinOkundu { get; set; }

    /// <summary>Yoklama aralığı (saniye). 30, 60, 300, 900 ya da 86400.</summary>
    public int PollIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Seçim listelerinde "hiçbiri" işareti: kullanıcı bütün anahtarları kapattı.
    /// Boş liste "hepsi" demek olduğu için ayrı bir işaret gerekiyor; önceden
    /// bütün anahtarları kapatan kullanıcı yine hepsini görüyordu. Google'da bu
    /// kimlikte bir takvim ya da görev listesi olmaz, yani hiçbiriyle eşleşmez.
    /// </summary>
    public const string HicbiriSecili = "(yok)";

    /// <summary>
    /// Gösterilecek takvimlerin kimlikleri. Boşsa hepsi, yalnız
    /// <see cref="HicbiriSecili"/> varsa hiçbiri gösterilir. Ana takvimin ve
    /// paylaşılan takvimlerin kimliği e-posta adresidir; bu yüzden liste ayar
    /// dosyasına <see cref="SelectedCalendarIdsSifreli"/> olarak yazılır. Liste
    /// yerinde değiştirildiği için (Add, Remove, Clear) şifreli hâli her yazımda
    /// listeden yeniden hesaplanır.
    /// </summary>
    [JsonIgnore]
    public List<string> SelectedCalendarIds { get; set; } = new();

    private string? _sonTakvimlerDuz;
    private string? _sonTakvimlerSifreli;

    /// <summary>
    /// Seçili takvimlerin JSON dizisi olarak Windows DPAPI ile şifrelenmiş hâli
    /// (Base64). Liste boşsa (hepsi gösteriliyor) yazılmaz. Liste değişmedikçe aynı
    /// şifreli metin verilir; dosya her kayıtta gereksiz yere değişmesin.
    /// </summary>
    public string? SelectedCalendarIdsSifreli
    {
        get
        {
            if (SelectedCalendarIds.Count == 0)
            {
                return null;
            }

            string duz = JsonSerializer.Serialize(SelectedCalendarIds);
            if (duz != _sonTakvimlerDuz || _sonTakvimlerSifreli is null)
            {
                _sonTakvimlerSifreli = GizliDeger.Sifrele(duz, TakvimlerAdi);
                _sonTakvimlerDuz = _sonTakvimlerSifreli is null ? null : duz;
            }

            _takvimlerSifrelenemedi = _sonTakvimlerSifreli is null;
            return _sonTakvimlerSifreli;
        }
        set
        {
            string? duz = GizliDeger.Coz(value, TakvimlerAdi);
            List<string>? liste = null;
            if (duz is not null)
            {
                try
                {
                    liste = JsonSerializer.Deserialize<List<string>>(duz);
                }
                catch (JsonException)
                {
                    liste = null;
                }
            }

            SelectedCalendarIds = liste ?? new List<string>();
            _sonTakvimlerDuz = liste is null ? null : duz;
            _sonTakvimlerSifreli = liste is null ? null : value;
        }
    }

    /// <summary>
    /// 1.16'ya kadar liste "SelectedCalendarIds" adıyla düz metin yazılıyordu. Bu
    /// alan yalnızca o eski kaydı okumak için var.
    /// </summary>
    /// <remarks>
    /// 1.16 şifreli seçimi okuyamadığı için listeyi hep yazar (boşsa []). Okunan liste
    /// <see cref="IJsonOnDeserialized.OnDeserialized"/>'da uygulanır. Şifreleme
    /// yapılamadıysa liste düz yazılır (bkz. <see cref="EskiDuzMetinClientId"/>);
    /// <see cref="SelectedCalendarIdsSifreli"/> ondan önce okunduğu için bayrak güncel.
    /// </remarks>
    [JsonPropertyName("SelectedCalendarIds")]
    public List<string>? EskiDuzMetinSelectedCalendarIds
    {
        get => _takvimlerSifrelenemedi && SelectedCalendarIds.Count > 0 ? SelectedCalendarIds : null;
        set
        {
            _okunanDuzTakvimler = value;
            if (value is { Count: > 0 })
            {
                DuzMetinOkundu = true;
            }
        }
    }

    private bool _takvimlerSifrelenemedi;

    /// <summary>
    /// Gösterilecek görev listelerinin kimlikleri. Boşsa hepsi, yalnız
    /// <see cref="HicbiriSecili"/> varsa hiçbiri gösterilir.
    /// </summary>
    public List<string> SelectedTaskListIds { get; set; } = new();

    /// <summary>Görev noktacıklarının rengi (#RRGGBB).</summary>
    public string TaskColor { get; set; } = "#4C8DFF";

    private string? _accountEmail;
    private string? _accountEmailSifreli;

    /// <summary>
    /// Bağlı hesabın e-posta adresi; yalnızca ayarlarda göstermek için. Çözülmüş
    /// hâli yalnızca bellekte durur; ayar dosyasına <see cref="AccountEmailSifreli"/> yazılır.
    /// </summary>
    [JsonIgnore]
    public string? AccountEmail
    {
        get => _accountEmail;
        set
        {
            _accountEmail = value;
            _accountEmailSifreli = GizliDeger.Sifrele(value, EpostaAdi);
        }
    }

    /// <summary>E-posta adresinin Windows DPAPI ile şifrelenmiş hâli (Base64).</summary>
    public string? AccountEmailSifreli
    {
        get => _accountEmailSifreli;
        set
        {
            _accountEmailSifreli = value;
            _accountEmail = GizliDeger.Coz(value, EpostaAdi);
        }
    }

    /// <summary>
    /// 1.16'ya kadar e-posta "AccountEmail" adıyla düz metin yazılıyordu. Bu alan
    /// yalnızca o eski kaydı okumak için var.
    /// </summary>
    /// <remarks>Okuma ve yazma <see cref="EskiDuzMetinClientId"/> ile aynı kurala uyar.</remarks>
    [JsonPropertyName("AccountEmail")]
    public string? EskiDuzMetinAccountEmail
    {
        get => _accountEmailSifreli is null ? _accountEmail : null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _okunanDuzEposta = value;
                DuzMetinOkundu = true;
            }
        }
    }

    /// <summary>
    /// Okuma bitti: düz değerler şifreli alanlardan önce gelir. 1.17 düz yazmaz (yalnız
    /// şifreleme yapılamadığında); dosyada düz değer varsa onu en son 1.16 ya da öncesi
    /// yazmıştır ve şifreli alanlar 1.16'nın dokunmadan geri yazdığı eski değerlerdir.
    /// </summary>
    void IJsonOnDeserialized.OnDeserialized()
    {
        // Bu noktada AccountEmail şifreli alandan çözülen değerdir (ya da null).
        bool hesapDegisti = _okunanDuzEposta is not null && AccountEmail is not null
            && !string.Equals(_okunanDuzEposta, AccountEmail, StringComparison.OrdinalIgnoreCase);

        if (_okunanDuzKimlik is not null)
        {
            ClientId = _okunanDuzKimlik;
        }

        if (_okunanDuzEposta is not null)
        {
            AccountEmail = _okunanDuzEposta;
        }

        // Boş düz liste eski şifreli seçimi yalnız hesap değiştiyse düşürür: 1.16'ya
        // dönülüp hiçbir şeye dokunulmadan geri gelinince seçim korunur.
        if (_okunanDuzTakvimler is { Count: > 0 } || (_okunanDuzTakvimler is not null && hesapDegisti))
        {
            SelectedCalendarIds = _okunanDuzTakvimler;
        }

        _okunanDuzKimlik = null;
        _okunanDuzEposta = null;
        _okunanDuzTakvimler = null;
    }

    /// <summary>Bu sürümün tanımadığı alanlar; bkz. <see cref="AppSettings.BilinmeyenAlanlar"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? BilinmeyenAlanlar { get; set; }
}

/// <summary>Resmi tatil ayarları.</summary>
internal sealed class HolidaySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Tatil listesinin en son ne zaman indirildiği.</summary>
    public DateTimeOffset? LastFetchUtc { get; set; }

    /// <summary>Bu sürümün tanımadığı alanlar; bkz. <see cref="AppSettings.BilinmeyenAlanlar"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? BilinmeyenAlanlar { get; set; }
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
    /// görünmesinin tek yolu bu olduğu için kapatmak takvimi de kaldırır:
    /// tepsideki "Eski duvar kâğıdıma dön" gibi önceki duvar kâğıdı geri gelir;
    /// kapalı kaldıkça Kalender duvar kâğıdınıza dokunmaz.
    /// </summary>
    public bool SyncSystemWallpaper { get; set; } = true;

    /// <summary>
    /// Yeni sürüm GitHub'dan denetlensin mi? Kapatılırsa sürümler sayfasına
    /// hiç bakılmaz ve güncelleme penceresi açılmaz.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>
    /// Güncelleme hatırlatma penceresinin en son gösterildiği an (UTC). Kullanıcı
    /// güncellemezse pencere bundan bir hafta sonra yeniden açılır; güncelleme
    /// kurulunca silinir.
    /// </summary>
    public DateTime? UpdatePromptShownUtc { get; set; }

    /// <summary>Günlük ayrıntı seviyesi: 0 Ayrıntı, 1 Bilgi, 2 Uyarı, 3 Hata, 4 Kapalı.</summary>
    public int LogLevel { get; set; } = 3;

    /// <summary>İlk çalıştırmada karşılama bilgisi gösterildi mi?</summary>
    public bool FirstRunDone { get; set; }

    /// <summary>
    /// Ayarlar penceresinin görünümü: null ise Windows'un uygulama teması
    /// izlenir, sonra kullanıcının seçimi (true = karanlık) saklanır.
    /// </summary>
    public bool? DarkSettingsWindow { get; set; }

    /// <summary>
    /// Bu sürümün tanımadığı alanlar (daha yeni bir sürümün yazdıkları).
    /// Ayar dosyası yeniden yazılırken olduğu gibi geri yazılır; eski bir
    /// sürüme dönülse bile yeni sürümün ayarları silinmez. İç içe her ayar
    /// sınıfında aynı alan var.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? BilinmeyenAlanlar { get; set; }

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
