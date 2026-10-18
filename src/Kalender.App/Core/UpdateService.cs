using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
using Kalender.Settings;

namespace Kalender.Core;

/// <summary>GitHub'daki en son sürümün bu uygulamayı ilgilendiren kısmı.</summary>
/// <param name="Version">Sürüm (üç parçalı).</param>
/// <param name="DownloadUrl">Kurulum dosyasının adresi.</param>
/// <param name="Size">Kurulum dosyasının bayt cinsinden boyu; bilinmiyorsa 0.</param>
/// <param name="Sha256">GitHub'ın verdiği SHA-256 özeti (onaltılık); yoksa null.</param>
/// <param name="PageUrl">Sürümün GitHub sayfası.</param>
internal sealed record UpdateInfo(Version Version, string DownloadUrl, long Size, string? Sha256, string PageUrl);

/// <summary>Güncellemenin o anki durumu.</summary>
internal enum UpdateState
{
    /// <summary>Yeni sürüm yok ya da henüz denetlenmedi.</summary>
    Idle,

    /// <summary>Yeni sürüm var; kullanıcının tıklaması bekleniyor.</summary>
    Available,

    Downloading,

    /// <summary>İndirildi ve doğrulandı; kurucu başlatıldı, uygulama kapanıyor.</summary>
    Installing,

    /// <summary>İndirme ya da doğrulama başarısız oldu; yeniden denenebilir.</summary>
    Failed,
}

/// <summary>
/// Yeni sürümü GitHub'daki sürümler sayfasından denetler, istenince indirip kurar.
///
/// Denetim açılıştan bir dakika sonra, ardından 12 saatte bir yapılır. Yeni
/// sürüm bulunursa <see cref="PromptRequested"/> ile haber verilir; uygulama
/// güncelleme penceresini açar. Kullanıcı güncellemezse pencere haftada bir
/// kez yeniden gösterilir (bkz. <see cref="ReminderInterval"/>).
///
/// En son sürümün bilgisi okunur, kullanıcı isterse kurulum dosyası
/// indirilir. İsteklerde sürüm numarası dışında hiçbir bilgi gitmez; Google
/// hesabıyla ilgisi yoktur. Ayarlardan kapatılabilir.
///
/// Kurulum mevcut kurucu üzerinden yürür: indirilen dosya
/// <see cref="InstallSwitch"/> ile başlatılır, bu süreç kapanınca kendini
/// kurulum klasörüne kopyalar ve yeni sürümü açar. Ayarlar, Google girişi ve
/// yerel veri kopyası kurulum klasörünün dışında durduğu için korunur.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    /// <summary>Sürümlerin yayınlandığı depo.</summary>
    public const string Repository = "SCgrS/Kalender";

    /// <summary>Sürümdeki kurulum dosyasının adı; yayin.ps1 bu adla üretir.</summary>
    public const string AssetName = "Kalender-Kurulum.exe";

    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";

    /// <summary>İndirilen kurucuya "pencere açmadan güncelle" diyen anahtar.</summary>
    public const string InstallSwitch = "--guncelle";

    /// <summary>Kurucunun, kapanmasını beklemesi gereken sürecin kimliğini veren anahtar.</summary>
    public const string WaitSwitch = "--bekle";

    /// <summary>Güncellemeden sonra açılan yeni sürüme verilen anahtar.</summary>
    public const string UpdatedSwitch = "--guncellendi";

    /// <summary>
    /// Güncelleme hatırlatma penceresinden başlatıldı: yeni sürüm açılınca
    /// ayarlar penceresini açmaz, yalnızca bir bildirimle haber verir.
    /// </summary>
    public const string NoticeSwitch = "--bildirim";

    /// <summary>Kullanıcı güncellemezse hatırlatma penceresinin yeniden gösterilme aralığı.</summary>
    public static readonly TimeSpan ReminderInterval = TimeSpan.FromDays(7);

    /// <summary>En son sürümün sorulduğu adres. Sınamada yerel bir sunucuya çevrilebilsin diye sabit değil.</summary>
    internal static string LatestReleaseApi { get; set; } =
        "https://api.github.com/repos/" + Repository + "/releases/latest";

    /// <summary>Açılışta ilk denetimden önceki bekleme; açılış işleriyle yarışmasın.</summary>
    internal static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(12);

    /// <summary>Ayarlar penceresi açılınca son deneme bundan eskiyse yeniden bakılır.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// İndirmede hiç veri gelmeden geçebilecek en uzun süre. Toplam süreye
    /// sınır yok: yavaş ama ilerleyen bir bağlantıda indirme sürer, asılı kalan
    /// bir bağlantı ise bu süre dolunca kesilir. Sınamada kısaltılabilsin diye
    /// sabit değil.
    /// </summary>
    internal static TimeSpan DownloadStallTimeout { get; set; } = TimeSpan.FromSeconds(60);

    // Süreler her istekte ayrı verildiği için istemcinin kendi sınırı kapalı.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly SettingsStore _settings;
    private readonly DispatcherTimer _timer;
    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private bool _checking;

    /// <summary>
    /// İndirme ya da kurulum sürüyor. Bu sırada <see cref="InstallAsync"/> yeniden
    /// çağrılırsa (ikinci tıklama) hiçbir şey yapmaz; durum ne gösterirse göstersin.
    /// </summary>
    private bool _kuruluyor;

    /// <summary>Eski açılım klasörlerinin temizliği bu açılışta başlatıldı mı?</summary>
    private bool _acilimTemizligiBasladi;

    public UpdateService(SettingsStore settings)
    {
        _settings = settings;

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            EskiAcilimKlasorleriniTemizle();
            await CheckAsync(remind: true);
        };
    }

    /// <summary>Durum, ilerleme ya da bulunan sürüm değişti.</summary>
    public event Action? Changed;

    /// <summary>
    /// Yeni sürüm var ve kullanıcıya pencereyle haber verme zamanı geldi.
    /// Yalnızca zamanlanmış denetimlerden doğar; ayarlar penceresinin kendi
    /// denetimi pencere açtırmaz, çünkü orada bildirim zaten görünür.
    /// </summary>
    public event Action? PromptRequested;

    /// <summary>Kurucu başlatıldı; dosyası değiştirilebilsin diye uygulama kapanmalı.</summary>
    public event Action? ExitRequested;

    /// <summary>Çalışan sürüm, üç parçalı.</summary>
    public static Version CurrentVersion { get; } =
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));

    /// <summary>İndirilen kurulum dosyalarının klasörü.</summary>
    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "Kalender guncelleme");

    public UpdateState State { get; private set; }

    /// <summary>Bulunan yeni sürüm; yoksa null.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>İndirme yüzdesi (0-100).</summary>
    public int Progress { get; private set; }

    /// <summary>Son indirme ya da kurma hatasının açıklaması.</summary>
    public string? Error { get; private set; }

    /// <summary>Son başarılı denetimin zamanı (yerel saat); hiç olmadıysa null.</summary>
    public DateTime? LastChecked { get; private set; }

    /// <summary>Son denetim başarısız mı oldu (ağ yok, GitHub'a ulaşılamadı…)?</summary>
    public bool LastCheckFailed { get; private set; }

    /// <summary>Denetim şu an sürüyor mu? ("Şimdi denetle" düğmesi bu sırada kapalı.)</summary>
    public bool Checking => _checking;

    /// <summary>
    /// Bu oturumda "Şimdi denetle"ye basıldı mı? Otomatik denetim kapalıyken de elle
    /// bulunan sürüm bildirilir.
    /// </summary>
    public bool ElleDenetlendi { get; private set; }

    public void Start() => _timer.Start();

    /// <summary>Son deneme eskiyse hemen denetler. Ayarlar penceresi açılırken çağrılır.</summary>
    public void CheckIfStale()
    {
        if (DateTime.UtcNow - _lastAttemptUtc > StaleAfter)
        {
            _ = CheckAsync();
        }
    }

    /// <summary>
    /// Hatırlatma penceresini gösterme zamanı mı? Hiç gösterilmediyse ya da
    /// son gösterimin üzerinden <see cref="ReminderInterval"/> geçtiyse evet.
    /// Kayıtlı zaman gelecekteyse (saat geri alınmış) yine evet; yoksa pencere
    /// saat o güne gelene kadar hiç çıkmazdı.
    /// </summary>
    internal static bool ReminderDue(DateTime? lastShownUtc, DateTime nowUtc)
        => lastShownUtc is not DateTime last || last > nowUtc || nowUtc - last >= ReminderInterval;

    /// <summary>Hatırlatma penceresi gösterildi; bir hafta boyunca yeniden açılmaz.</summary>
    public void MarkPromptShown() => _settings.Update(s => s.UpdatePromptShownUtc = DateTime.UtcNow);

    /// <summary>
    /// Güncelleme kuruldu: sonraki yeni sürüm, son hatırlatmadan bir hafta
    /// geçmesi beklenmeden haber verilsin.
    /// </summary>
    public void ClearPromptShown()
    {
        if (_settings.Current.UpdatePromptShownUtc is not null)
        {
            _settings.Update(s => s.UpdatePromptShownUtc = null);
        }
    }

    /// <summary>
    /// En son sürümü sorar. Ağ yoksa ya da GitHub yanıt vermezse sessizce
    /// geçer; bir sonraki denetimde yeniden denenir.
    /// </summary>
    /// <param name="remind">Yeni sürüm varsa ve zamanı geldiyse pencereyle haber verilsin mi?</param>
    /// <param name="elle">
    /// Kullanıcı "Şimdi denetle"ye bastı: otomatik denetim kapalı olsa da denetlenir.
    /// </param>
    public async Task CheckAsync(bool remind = false, bool elle = false)
    {
        if (_checking || (!elle && !_settings.Current.CheckForUpdates)
            || State is UpdateState.Downloading or UpdateState.Installing)
        {
            return;
        }

        _checking = true;
        _lastAttemptUtc = DateTime.UtcNow;
        if (elle)
        {
            ElleDenetlendi = true;
            Changed?.Invoke();
        }

        try
        {
            CleanDownloads();

            UpdateInfo? latest = await FetchLatestAsync();
            LastChecked = DateTime.Now;
            LastCheckFailed = false;

            if (State is UpdateState.Downloading or UpdateState.Installing)
            {
                // Yanıt beklenirken kullanıcı güncellemeyi başlatmış (yavaş bağlantıda
                // denetim 30 saniyeyi bulabilir). Durum ve bulunan sürüm olduğu gibi
                // kalır; yoksa bildirim yeniden "İndirip kurmak için tıklayın" der, ikinci
                // tıklama ikinci bir indirme başlatırdı.
                return;
            }

            if (latest is not null && latest.Version > CurrentVersion)
            {
                if (Available?.Version != latest.Version)
                {
                    Log.Info($"Yeni sürüm bulundu: {Installer.VersionText(latest.Version)}");
                }

                Available = latest;
                State = UpdateState.Available;
                Error = null;
            }
            else
            {
                Available = null;
                State = UpdateState.Idle;
            }
        }
        catch (Exception ex)
        {
            // Ağ yok, GitHub'a ulaşılamıyor ya da depo herkese açık değil; uygulama için hata değil.
            LastCheckFailed = true;
            Log.Info("Güncelleme denetlenemedi: " + ex.Message);
        }
        finally
        {
            _checking = false;
            Changed?.Invoke();
        }

        if (remind && State == UpdateState.Available
            && ReminderDue(_settings.Current.UpdatePromptShownUtc, DateTime.UtcNow))
        {
            PromptRequested?.Invoke();
        }
    }

    /// <summary>
    /// Bulunan sürümü indirir, doğrular ve kurucuyu başlatır. Başarılı olursa
    /// <see cref="ExitRequested"/> tetiklenir; kurucu bu süreç kapanınca
    /// kurar ve yeni sürümü açar.
    /// </summary>
    /// <param name="fromPrompt">Hatırlatma penceresinden mi başlatıldı? (bkz. <see cref="NoticeSwitch"/>)</param>
    public async Task InstallAsync(bool fromPrompt = false)
    {
        UpdateInfo? info = Available;
        if (info is null || _kuruluyor || State is UpdateState.Downloading or UpdateState.Installing)
        {
            return;
        }

        _kuruluyor = true;
        State = UpdateState.Downloading;
        Progress = 0;
        Error = null;
        Changed?.Invoke();

        try
        {
            string file = await DownloadAsync(info);
            Verify(file, info);

            State = UpdateState.Installing;
            Changed?.Invoke();

            StartInstaller(file, fromPrompt);
            Log.Info($"Güncelleme başlatıldı: {Installer.VersionText(CurrentVersion)} -> {Installer.VersionText(info.Version)}");

            ExitRequested?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn("Güncelleme kurulamadı.", ex);
            _kuruluyor = false;
            State = UpdateState.Failed;
            Error = UserMessage(ex);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Eski sürümlerin Temp altında bıraktığı açılım klasörlerini ilk denetimle
    /// birlikte (açılıştan bir dakika sonra) arka planda bir kez siler (bkz.
    /// <see cref="Installer.EskiAcilimKlasorleriniSil()"/>). Güncelleme denetimi
    /// kapalıysa da yapılır. Yalnız kurulu kopyada: elle açılan bir kopya kurulu
    /// uygulamanın klasörlerine dokunmasın.
    /// </summary>
    private void EskiAcilimKlasorleriniTemizle()
    {
        if (_acilimTemizligiBasladi || !Installer.RunningInstalled)
        {
            return;
        }

        _acilimTemizligiBasladi = true;
        _ = Task.Run(() => Installer.EskiAcilimKlasorleriniSil());
    }

    /// <summary>
    /// İndirme ya da kurma hatasının kullanıcıya gösterilecek sade Türkçe
    /// açıklaması. .NET'in kendi iletileri İngilizce ve teknik; bu sınıfın
    /// attığı hatalar (eksik dosya, tutmayan özet, yanlış sürüm) zaten Türkçe.
    /// </summary>
    internal static string UserMessage(Exception ex) => ex switch
    {
        OperationCanceledException =>
            $"İndirme {DownloadStallTimeout.TotalSeconds:0} saniye boyunca hiç ilerlemediği için durduruldu. "
            + "İnternet bağlantınız kopmuş ya da çok yavaşlamış olabilir.",
        HttpRequestException { StatusCode: HttpStatusCode code } =>
            $"Yeni sürüm indirilemedi: sunucu dosyayı vermedi (HTTP {(int)code}).",
        HttpRequestException or HttpIOException =>
            "Yeni sürüm indirilemedi: sunucuya bağlanılamadı ya da bağlantı indirme sırasında koptu.",
        InvalidDataException => ex.Message,
        // Ağdan okurken kopan bağlantı buraya düşmez: HttpIOException'a çevrilir (bkz. ReadFromNetworkAsync).
        IOException or UnauthorizedAccessException => "İndirilen dosya diske yazılamadı.",
        Win32Exception or InvalidOperationException => "İndirilen kurulum dosyası başlatılamadı.",
        _ => "Yeni sürüm indirilemedi.",
    };

    // ---------------------------------------------------------------- GitHub

    private static async Task<UpdateInfo?> FetchLatestAsync()
    {
        using var cts = new CancellationTokenSource(CheckTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.Add(UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        using HttpResponseMessage response = await Http.SendAsync(request, cts.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Yayınlanmış sürüm yok ya da depo herkese açık değil.
            throw new HttpRequestException("Sürüm bilgisi bulunamadı (HTTP 404).");
        }

        response.EnsureSuccessStatusCode();

        await using Stream body = await response.Content.ReadAsStreamAsync(cts.Token);
        using JsonDocument doc = await JsonDocument.ParseAsync(body, cancellationToken: cts.Token);
        return Parse(doc.RootElement);
    }

    /// <summary>
    /// GitHub'ın "en son sürüm" yanıtından sürümü ve kurulum dosyasını çıkarır.
    /// Etiket "v1.2.0" biçiminde olmalı ve sürümde <see cref="AssetName"/>
    /// bulunmalı; yoksa null döner.
    /// </summary>
    internal static UpdateInfo? Parse(JsonElement release)
    {
        string? tag = String(release, "tag_name");
        if (tag is null || !Version.TryParse(tag.TrimStart('v', 'V'), out Version? version))
        {
            return null;
        }

        if (!release.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            if (!string.Equals(String(asset, "name"), AssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? url = String(asset, "browser_download_url");
            if (url is null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            long size = asset.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long n) ? n : 0;

            // GitHub dosyanın özetini "sha256:<onaltılık>" olarak veriyor; eski sürümlerde alan yok.
            string? digest = String(asset, "digest");
            string? sha256 = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..]
                : null;

            return new UpdateInfo(Normalize(version), url, size, sha256, String(release, "html_url") ?? ReleasesUrl);
        }

        return null;
    }

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static ProductInfoHeaderValue UserAgent => new("Kalender", Installer.VersionText(CurrentVersion));

    // --------------------------------------------------------------- indirme

    private async Task<string> DownloadAsync(UpdateInfo info)
    {
        Directory.CreateDirectory(DownloadFolder);

        // Adında "Kurulum" geçiyor: anahtarı tanımayan bir sürüm çıkarsa bile
        // dosya çift tıklanmış gibi kurulum penceresini açar.
        string target = Path.Combine(DownloadFolder, $"Kalender-Kurulum-{Installer.VersionText(info.Version)}.exe");
        string partial = target + ".indiriliyor";

        // Süre her okumadan sonra baştan kurulur: indirmenin tamamı değil, veri
        // gelmeden geçen süre sınırlı (bkz. DownloadStallTimeout).
        using var cts = new CancellationTokenSource(DownloadStallTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, info.DownloadUrl);
        request.Headers.UserAgent.Add(UserAgent);

        using HttpResponseMessage response = await Http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? info.Size;
        long read = 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await using (Stream source = await response.Content.ReadAsStreamAsync(cts.Token))
        await using (var dest = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                         1 << 16, useAsync: true))
        {
            byte[] buffer = new byte[1 << 16];
            int n;
            while ((n = await ReadFromNetworkAsync(source, buffer, cts.Token)) > 0)
            {
                cts.CancelAfter(DownloadStallTimeout);
                await dest.WriteAsync(buffer.AsMemory(0, n), cts.Token);
                hash.AppendData(buffer, 0, n);
                read += n;

                int percent = total > 0 ? (int)Math.Min(100, read * 100 / total) : 0;
                if (percent != Progress)
                {
                    Progress = percent;
                    Changed?.Invoke();
                }
            }
        }

        if (info.Size > 0 && read != info.Size)
        {
            throw new InvalidDataException($"İndirilen dosya eksik ({read} / {info.Size} bayt).");
        }

        string actual = Convert.ToHexString(hash.GetHashAndReset());
        if (info.Sha256 is not null && !string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("İndirilen dosyanın SHA-256 özeti sürüm sayfasındakiyle uyuşmuyor.");
        }

        File.Move(partial, target, overwrite: true);
        return target;
    }

    /// <summary>
    /// İndirilen dosyanın bir sonraki parçasını ağdan okur. Bağlantı gövde
    /// okunurken koparsa (karşı taraf bağlantıyı sıfırladı, Wi-Fi gitti, şifreli
    /// bağlantı yarıda kaldı) .NET düz bir IOException verir; bu, disk hatasıyla
    /// karışmasın diye bağlantı hatasına çevrilir. İsteğin kendisindeki hatalar
    /// (SendAsync) zaten HttpRequestException olarak gelir.
    /// </summary>
    private static async ValueTask<int> ReadFromNetworkAsync(Stream source, Memory<byte> buffer, CancellationToken token)
    {
        try
        {
            return await source.ReadAsync(buffer, token);
        }
        catch (IOException ex) when (ex is not HttpIOException)
        {
            throw new HttpIOException(HttpRequestError.Unknown, ex.Message, ex);
        }
    }

    /// <summary>İndirilen dosya gerçekten beklenen sürümün Kalender'i mi?</summary>
    private static void Verify(string file, UpdateInfo info)
    {
        FileVersionInfo v = FileVersionInfo.GetVersionInfo(file);
        var version = new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);

        if (!string.Equals(v.ProductName, "Kalender", StringComparison.Ordinal) || version != info.Version)
        {
            throw new InvalidDataException(
                $"İndirilen dosya beklenen Kalender {Installer.VersionText(info.Version)} değil "
                + $"({v.ProductName} {Installer.VersionText(version)}).");
        }
    }

    /// <summary>Kurucuyu, bu sürecin kapanmasını bekleyecek şekilde başlatır.</summary>
    private static void StartInstaller(string file, bool fromPrompt)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(file)!,
        };

        start.ArgumentList.Add(InstallSwitch);
        start.ArgumentList.Add(WaitSwitch);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (fromPrompt)
        {
            start.ArgumentList.Add(NoticeSwitch);
        }

        using Process? process = Process.Start(start);
        if (process is null)
        {
            throw new InvalidOperationException("Kurucu başlatılamadı.");
        }
    }

    /// <summary>Kaldırmada güncelleme indirme klasörünü bütünüyle siler; yoksa bir şey yapmaz.</summary>
    public static void RemoveDownloadFolder()
    {
        try
        {
            if (Directory.Exists(DownloadFolder))
            {
                Directory.Delete(DownloadFolder, recursive: true);
                Log.Info("Güncelleme indirme klasörü silindi.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Güncelleme indirme klasörü silinemedi.", ex);
        }
    }

    /// <summary>
    /// Önceki güncellemelerden kalan kurulum dosyalarını siler. Kurucu çalışırken
    /// kendi dosyasını silemediği için bu iş bir sonraki denetime kalır.
    /// </summary>
    private static void CleanDownloads()
    {
        try
        {
            if (!Directory.Exists(DownloadFolder))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(DownloadFolder))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Hâlâ çalışan bir kurucu olabilir; sonra silinir.
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Eski güncelleme dosyaları silinemedi: " + ex.Message);
        }
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    public void Dispose() => _timer.Stop();
}
