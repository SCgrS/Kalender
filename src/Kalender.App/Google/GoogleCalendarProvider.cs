using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kalender.Core;
using Kalender.Settings;

namespace Kalender.Google;

/// <summary>
/// Google Takvim ve Görevler'den yalnız okuma yapan sağlayıcı.
///
/// Veri kullanımı bilinçli olarak kısıldı:
/// - Yalnızca görünen ayın kapsadığı gün aralığı isteniyor.
/// - Her istekte "fields" parametresiyle sadece kullandığımız alanlar isteniyor.
/// - Yoklamalar artımlı: her takvime önce "updatedMin" ile, tarih sınırı
///   koymadan "son yoklamadan beri değişen var mı" diye soruluyor. Yoksa
///   yanıt birkaç yüz bayt kalıyor ve takvim hiç indirilmiyor. Varsa yalnızca
///   o takvimin görünen aralığı baştan alınıyor; böylece aralığın dışına
///   taşınan ya da silinen bir kayıt da bir sonraki yoklamada kayboluyor.
///   Görevlerde aynı soru doğrudan değişen görevleri getiriyor.
/// - Yarım saatte bir yine de tam yenileme yapılıyor: takvim listesi ve
///   etiket renkleri yalnızca orada tazeleniyor.
///
/// Not: Google'ın syncToken'ı timeMin/timeMax ile birlikte kullanılamıyor.
/// Şartnamedeki "yalnızca görünen ay" kuralını korumak için artımlılık
/// updatedMin üzerinden sağlandı; sonuç aynı, veri kullanımı daha da düşük.
/// </summary>
internal sealed class GoogleCalendarProvider : ICalendarProvider, IDisposable
{
    private const string CalendarBase = "https://www.googleapis.com/calendar/v3";
    private const string TasksBase = "https://tasks.googleapis.com/tasks/v1";

    /// <summary>Bu süre geçtiyse artımlı değil tam yenileme yapılır.</summary>
    private static readonly TimeSpan FullSyncEvery = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Artımlı yoklamada "son yoklamadan beri değişen var mı" sorusu bu kadar
    /// geriden soruluyor. Sınır anı bilgisayarın saatinden alınıyor, Google ise
    /// kaydın değişme zamanını kendi saatine göre yazıyor: bilgisayarın saati
    /// birkaç saniye ilerideyse ya da Google değişikliği sorguya geç
    /// yansıtırsa bir yoklamanın hemen ardındaki değişiklik görülmüyor, 30
    /// dakikalık tam yenilemeyi bekliyordu. Bedeli küçük: değişen bir takvim
    /// birkaç yoklama boyunca yeniden alınır.
    /// </summary>
    private static readonly TimeSpan ArtimliPay = TimeSpan.FromMinutes(2);

    /// <summary>Bellekte tutulan, daha önce gösterilmiş başka ay sayısı.</summary>
    private const int GecmisSiniri = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly GoogleAuthenticator _auth;
    private readonly SettingsStore _settings;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _versionLogged;

    /// <summary>Süren eşitlemedeki istek sayısı ve alınan gövde toplamı; ayrıntılı günlük için.</summary>
    private int _syncRequests;
    private long _syncBodyBytes;

    /// <summary>
    /// Yakın zamanda gösterilen başka ayların verisi (en çok <see cref="GecmisSiniri"/>).
    /// Ay düğmeleriyle gezinirken ya da 30 saniye sonra bu aya dönülürken, o
    /// ayın verisi Google'dan yeniden gelene kadar (1–2 sn) noktacıklar
    /// kaybolmasın diye tutuluyor. Yalnızca zaten gösterilmiş ayların verisi;
    /// Google'dan fazladan bir şey istenmiyor, "yalnızca görünen ay" kuralı
    /// korunuyor. Diske yazılmaz. <see cref="_veriKilidi"/> ile korunur:
    /// eşitleme arka planda biter, gezinme arayüz iş parçacığında olur.
    /// </summary>
    private readonly List<ProviderBundle> _gecmis = new();

    private readonly object _veriKilidi = new();

    /// <summary>
    /// Takvim ya da görev listesi seçimi her değiştiğinde artar (bkz.
    /// <see cref="SecimDegisti"/>). Eşitleme başında okunur; bu arada değişmişse
    /// sonuç eski seçimle alınmıştır ve bir sonraki eşitleme tam yapılır.
    /// </summary>
    private int _secimKusagi;

    public GoogleCalendarProvider(GoogleAuthenticator auth, SettingsStore settings)
    {
        _auth = auth;
        _settings = settings;

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30),

            // HTTP/2 isteniyor; sunucu ya da aradaki bir vekil desteklemezse
            // .NET kendiliğinden HTTP/1.1'e düşer. Yoklamaların yükü zaten
            // küçük; ölçümde saatlik tüketimin çoğu her istekte yinelenen
            // başlıklar (yetki anahtarı dahil) ve TLS payıydı. HTTP/2 aynı
            // bağlantıda başlıkları sıkıştırıp (HPACK) bu payı düşürüyor.
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Kalender/0.4 (+windows)");
    }

    public string Name => "Google Takvim";

    public bool IsConnected => _auth.IsConnected;

    public ProviderBundle Data { get; private set; } = new();

    /// <summary>Son denemede ağa ulaşılabildi mi? Tepsi durumunu bununla gösteriyoruz.</summary>
    public bool IsOnline { get; private set; } = true;

    /// <summary>
    /// Son eşitleme başarısız olduysa nedeni; başarılı olunca null. İptal edilen
    /// eşitleme bunu değiştirmez. Yoklayıcı uzun süren başarısızlığı buna bakarak
    /// varsayılan günlük düzeyinde de yazar.
    /// </summary>
    public string? SonHata { get; private set; }

    private static string CachePath => Path.Combine(Paths.CacheFolder, "google.json");

    // -------------------------------------------------------------- önbellek

    public void LoadCache()
    {
        // Bağlantı yokken yerel kopya gösterilmez. Önceki sürümlerde bağlantı
        // kesilirken süren bir eşitleme, silinen kopyayı geri yazabiliyordu;
        // o kopya yeniden bağlanılana dek her açılışta yükleniyordu. Diskte
        // kalmışsa burada silinir: yoksa başka bir hesapla bağlanıp ilk eşitleme
        // tutmadan yeniden açılınca önceki hesabın etkinlikleri yüklenirdi.
        if (!IsConnected)
        {
            ClearCache();
            return;
        }

        try
        {
            if (!File.Exists(CachePath))
            {
                return;
            }

            var bundle = JsonSerializer.Deserialize<ProviderBundle>(
                File.ReadAllText(CachePath, Encoding.UTF8), JsonOptions);

            if (bundle is not null)
            {
                // Diskteki kopya eski paletle yazılmış olabilir; ağa çıkmadan
                // önce de renkler Google Takvim'dekiyle aynı görünsün.
                RemapColors(bundle);

                Data = bundle;
                Log.Info($"Google önbelleği yüklendi: {bundle.Events.Count} etkinlik, {bundle.Tasks.Count} görev.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Google önbelleği okunamadı.", ex);
        }
    }

    /// <summary>
    /// Önbellekteki renkleri güncel Google Takvim paletine çevirir.
    /// Eşleşme bulunmayan renkler olduğu gibi kalır, ikinci kez çevrilmezler.
    /// </summary>
    private static void RemapColors(ProviderBundle bundle)
    {
        foreach (ProviderCalendar calendar in bundle.Calendars)
        {
            calendar.ColorHex = GoogleColorPalette.ToCurrent(calendar.ColorHex) ?? calendar.ColorHex;
        }

        foreach (ProviderEvent e in bundle.Events)
        {
            e.ColorHex = GoogleColorPalette.ToCurrent(e.ColorHex) ?? e.ColorHex;
        }

        foreach (string key in bundle.EventColors.Keys.ToList())
        {
            bundle.EventColors[key] = GoogleColorPalette.ToCurrent(bundle.EventColors[key])
                ?? bundle.EventColors[key];
        }
    }

    /// <summary>
    /// Görünen ay değişti: o ay yakın zamanda gösterildiyse bellekteki verisi
    /// hemen kullanılmaya başlar; bir sonraki eşitleme onu artımlı olarak
    /// tazeler (yalnız değişen takvimler yeniden alınır). Bellekte yoksa hiçbir
    /// şey değişmez; eşitleme eskisi gibi yeni ayı baştan alır.
    /// </summary>
    /// <returns>Bellekteki veri kullanılmaya başlandıysa true.</returns>
    public bool GecmistenGetir(DateOnly from, DateOnly to)
    {
        lock (_veriKilidi)
        {
            if (Data.RangeFrom == from && Data.RangeTo == to)
            {
                return true;
            }

            ProviderBundle? bulunan = _gecmis.Find(b => b.RangeFrom == from && b.RangeTo == to);
            if (bulunan is null)
            {
                return false;
            }

            _gecmis.Remove(bulunan);
            ProviderBundle giden = Data;
            Gecmise(giden);
            Data = TazesiyleBirlestir(bulunan, giden);
            return true;
        }
    }

    /// <summary>
    /// Bellekten geri getirilen ayın, giden aydan daha eski olabilecek verisine
    /// iki ayın ortak günlerinde (ızgaranın baştaki ve sondaki haftaları) daha
    /// yeni olanın kayıtlarını koyar.
    ///
    /// 2026-10-01: Ekim bir gün önce 11:48'de gösterilmişti; Eylül 22:23'te
    /// tazelenmişti. Gece ay dönünce Ekim'in eski kopyası geri geldi ve
    /// kullanıcının öğleden sonra griye çektiği 30 Eylül işleri yeniden kırmızı
    /// çizildi. Ortak günlerde artık hep en son alınan veri gösteriliyor.
    ///
    /// Eşitleme zamanları (<see cref="ProviderBundle.LastSyncUtc"/> ve
    /// <see cref="ProviderBundle.FullSyncUtc"/>) bilerek eskisinde kalıyor:
    /// bir sonraki eşitleme ayın tamamını o andan beri değişenlere göre tazeler.
    /// </summary>
    internal static ProviderBundle TazesiyleBirlestir(ProviderBundle eski, ProviderBundle yeni)
    {
        if (yeni.LastSyncUtc is null
            || (eski.LastSyncUtc is not null && eski.LastSyncUtc >= yeni.LastSyncUtc)
            || !string.Equals(eski.AccountEmail, yeni.AccountEmail, StringComparison.OrdinalIgnoreCase))
        {
            return eski;
        }

        DateOnly ilk = eski.RangeFrom > yeni.RangeFrom ? eski.RangeFrom : yeni.RangeFrom;
        DateOnly son = eski.RangeTo < yeni.RangeTo ? eski.RangeTo : yeni.RangeTo;
        if (ilk > son)
        {
            return eski;
        }

        bool Ortak(ProviderEvent e) => e.EndDate >= ilk && e.StartDate <= son;

        List<ProviderEvent> tazeEtkinlikler = yeni.Events.Where(Ortak).ToList();
        var tazeAnahtarlar = tazeEtkinlikler.Select(e => e.Id + "@" + e.CalendarId).ToHashSet(StringComparer.Ordinal);

        List<ProviderTask> tazeGorevler = yeni.Tasks.Where(t => t.Due >= ilk && t.Due <= son).ToList();

        // Görevler de etkinlikler gibi kimliğe göre eleniyor: eskide ortak
        // günlerin dışında duran, yenide ortak günlere taşınmış görevin iki
        // kopyası kalıyor, sonraki eşitleme "aynı anahtar" hatasıyla düşüyordu.
        var tazeGorevAnahtarlari = tazeGorevler.Select(t => t.Id + "@" + t.ListId).ToHashSet(StringComparer.Ordinal);

        return new ProviderBundle
        {
            AccountEmail = eski.AccountEmail,
            RangeFrom = eski.RangeFrom,
            RangeTo = eski.RangeTo,
            FullSyncUtc = eski.FullSyncUtc,
            LastSyncUtc = eski.LastSyncUtc,

            // Takvim renkleri ve etiketler de daha yeni olandan.
            Calendars = yeni.Calendars,
            TaskLists = yeni.TaskLists,
            EventColors = yeni.EventColors,
            EventLabels = yeni.EventLabels,

            Events = eski.Events
                .Where(e => !Ortak(e) && !tazeAnahtarlar.Contains(e.Id + "@" + e.CalendarId))
                .Concat(tazeEtkinlikler)
                .OrderBy(e => e.StartDate)
                .ThenBy(e => e.StartTime ?? TimeOnly.MinValue)
                .ToList(),
            Tasks = eski.Tasks
                .Where(t => (t.Due < ilk || t.Due > son) && !tazeGorevAnahtarlari.Contains(t.Id + "@" + t.ListId))
                .Concat(tazeGorevler)
                .OrderBy(t => t.Due)
                .ToList(),
        };
    }

    /// <summary>
    /// Windows'un saat dilimi değişti. Saatli etkinlikler alınırken yerel saate
    /// çevrilip öyle saklanıyor; eldekiler eski dilime göre çevrilmiş, gün
    /// kutusunda saat farkı kadar kaymış görünürler. Bellekteki öbür aylar
    /// atılır, sıradaki eşitleme (zorlanmasa da) tam yapılır: ağ yoksa ilk
    /// tutan eşitleme görünen ayı yeni dilimle baştan alır.
    /// </summary>
    public void SaatDilimiDegisti()
    {
        lock (_veriKilidi)
        {
            _gecmis.Clear();
            Data.FullSyncUtc = null;
        }
    }

    /// <summary>
    /// Ayarlarda bir takvimin ya da görev listesinin anahtarı değişti. Bellekteki
    /// aylar eski seçimle alınmıştı: yeni açılan takvimin kayıtları onlarda yok,
    /// artımlı yoklama da onları getirmiyordu (takvimde son yoklamadan beri
    /// değişiklik yoksa hiç sorulmuyor); bellekten gelen ayda 30 dakikaya kadar
    /// görünmüyorlardı. Aylar bellekte kalır, ama hepsinin tam yenileme zamanı
    /// silinir: o aya geçilince (zorlanmasa da) ay baştan alınır. Kapatılan
    /// takvimin kayıtları gösterilirken süzülüyor (bkz. CalendarDataService).
    /// Yerel kopya da yeniden yazılır: yoksa çevrim dışıyken seçim değişip
    /// uygulama yeniden açılınca diskteki eski tam yenileme zamanı yükleniyor,
    /// açılıştaki eşitleme artımlı oluyordu.
    /// </summary>
    public void SecimDegisti()
    {
        lock (_veriKilidi)
        {
            Interlocked.Increment(ref _secimKusagi);
            Data.FullSyncUtc = null;

            foreach (ProviderBundle bundle in _gecmis)
            {
                bundle.FullSyncUtc = null;
            }

            // Bağlantı kesildiyse kopya silinmiş ya da silinecek; geri yazılmaz.
            if (IsConnected)
            {
                SaveCache();
            }
        }
    }

    /// <summary>Yerinden edilen ayın verisini geçmişe ekler; en eskisi düşer. Kilit içinde çağrılır.</summary>
    private void Gecmise(ProviderBundle bundle)
    {
        if (bundle.Calendars.Count == 0 && bundle.Events.Count == 0 && bundle.Tasks.Count == 0)
        {
            return;
        }

        _gecmis.RemoveAll(b => b.RangeFrom == bundle.RangeFrom && b.RangeTo == bundle.RangeTo);
        _gecmis.Add(bundle);

        while (_gecmis.Count > GecmisSiniri)
        {
            _gecmis.RemoveAt(0);
        }
    }

    public void ClearCache()
    {
        // Dosya da kilidin içinde siliniyor: eşitleme kopyayı aynı kilidin
        // içinde yazıyor, silmenin arasına giremiyor.
        lock (_veriKilidi)
        {
            _gecmis.Clear();
            Data = new ProviderBundle();

            try
            {
                if (File.Exists(CachePath))
                {
                    File.Delete(CachePath);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Google önbelleği silinemedi.", ex);
            }
        }
    }

    private void SaveCache()
    {
        try
        {
            Directory.CreateDirectory(Paths.CacheFolder);
            string temp = CachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Data, JsonOptions), new UTF8Encoding(false));

            if (File.Exists(CachePath))
            {
                File.Replace(temp, CachePath, null);
            }
            else
            {
                File.Move(temp, CachePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Google önbelleği yazılamadı.", ex);
        }
    }

    // ---------------------------------------------------------- eşitleme

    public async Task<bool> SyncAsync(DateOnly from, DateOnly to, bool full, CancellationToken cancel)
    {
        if (!IsConnected)
        {
            return false;
        }

        // Seçim, istek geldiği anda kopyalanıyor (ilk beklemeden önce, yani
        // isteyenin iş parçacığında). Ayarlardaki liste canlı nesne: eşitleme
        // sürerken kullanıcı bir anahtarı değiştirince son süzgeç başka bir
        // seçime bakıyor, ayın bütün etkinlikleri elenebiliyordu.
        List<string> takvimSecimi = _settings.Current.Google.SelectedCalendarIds.ToList();
        List<string> gorevSecimi = _settings.Current.Google.SelectedTaskListIds.ToList();
        int secimKusagi = Volatile.Read(ref _secimKusagi);

        // Bu arada bağlantı kesilir ya da yeniden kurulursa sonuç yazılmaz.
        int kusak = _auth.BaglantiKusagi;

        // Süren bir eşitleme varsa bitmesini (ya da iptalini) bekliyoruz.
        // Önceden meşgulse hemen "değişiklik yok" dönülüyordu; bağlanma anında
        // art arda gelen iki istekten ikincisi bu yüzden atlanıyor, ilk veri
        // bir sonraki yoklamaya kalıyordu.
        await _gate.WaitAsync(cancel).ConfigureAwait(false);

        try
        {
            if (_auth.BaglantiKusagi != kusak)
            {
                return false;
            }

            string? token = await _auth.GetAccessTokenAsync(cancel).ConfigureAwait(false);
            if (token is null)
            {
                IsOnline = false;
                SonHata = "Google erişim anahtarı alınamadı.";
                return false;
            }

            // Aralık değiştiyse ya da uzun süredir tam yenileme yapılmadıysa baştan al.
            bool rangeChanged = Data.RangeFrom != from || Data.RangeTo != to;
            bool stale = Data.FullSyncUtc is null
                || DateTimeOffset.UtcNow - Data.FullSyncUtc > FullSyncEvery;

            bool doFull = full || rangeChanged || stale || Data.Calendars.Count == 0;

            DateTimeOffset started = DateTimeOffset.UtcNow;
            _syncRequests = 0;
            _syncBodyBytes = 0;

            var bundle = new ProviderBundle
            {
                AccountEmail = Data.AccountEmail,
                RangeFrom = from,
                RangeTo = to,
                EventColors = Data.EventColors,

                // Etiketler yalnızca tam yenilemede iniyor. Artımlı yoklamada
                // taşınmazsa, arada düzenlenen kayıt etiketsiz dönüştürülüyor
                // ve eski colorId'nin rengine (çoğu zaman Lavanta) düşüyor.
                EventLabels = Data.EventLabels,
                FullSyncUtc = doFull ? started : Data.FullSyncUtc,
                LastSyncUtc = started,
                Calendars = Data.Calendars,
                TaskLists = Data.TaskLists,
                Events = doFull ? new List<ProviderEvent>() : new List<ProviderEvent>(Data.Events),
                Tasks = doFull ? new List<ProviderTask>() : new List<ProviderTask>(Data.Tasks),
            };

            if (doFull)
            {
                bundle.Calendars = await FetchCalendarsAsync(token, cancel).ConfigureAwait(false);
                bundle.AccountEmail = bundle.Calendars.FirstOrDefault(c => c.Primary)?.Id ?? bundle.AccountEmail;
                bundle.TaskLists = await FetchTaskListsAsync(token, cancel).ConfigureAwait(false);

                if (bundle.EventColors.Count == 0)
                {
                    bundle.EventColors = await FetchColorsAsync(token, cancel).ConfigureAwait(false);
                }

                // Etiket renkleri kullanıcı tarafından değiştirilebiliyor;
                // her tam yenilemede tazeleniyor.
                bundle.EventLabels = await FetchEventLabelsAsync(token, bundle, takvimSecimi, cancel).ConfigureAwait(false);
            }

            DateTimeOffset? updatedMin = doFull ? null : Data.LastSyncUtc - ArtimliPay;

            await FetchEventsAsync(token, bundle, from, to, updatedMin, takvimSecimi, cancel).ConfigureAwait(false);
            await FetchTasksAsync(token, bundle, from, to, updatedMin, gorevSecimi, cancel).ConfigureAwait(false);

            // Bu arada başka bir aya geçildiyse (yoklayıcı bu işi iptal etti)
            // eski ayın verisi yenisinin üstüne yazılmasın.
            cancel.ThrowIfCancellationRequested();

            lock (_veriKilidi)
            {
                // Bu arada bağlantı kesildiyse (ya da yeniden kurulduysa) eski
                // yetkiyle alınan veri ne ekrana ne yerel kopyaya yazılıyor.
                // Önceden kesince silinen kopya bir saniye sonra geri geliyordu.
                // Kopyayı silen ClearCache de bu kilidi bekliyor.
                if (_auth.BaglantiKusagi != kusak)
                {
                    return false;
                }

                // Bu arada seçim değiştiyse veri eski seçimle alındı; yine
                // yazılıyor, ama bu ayın bir sonraki eşitlemesi tam yapılır.
                if (Volatile.Read(ref _secimKusagi) != secimKusagi)
                {
                    bundle.FullSyncUtc = null;
                }

                if (Data.RangeFrom != bundle.RangeFrom || Data.RangeTo != bundle.RangeTo)
                {
                    Gecmise(Data);
                }

                Data = bundle;
                SaveCache();
            }

            IsOnline = true;
            SonHata = null;

            _auth.UpdateAccountEmail(bundle.AccountEmail);

            Log.Debug($"Google eşitlendi ({(doFull ? "tam" : "artımlı")}): "
                + $"{bundle.Events.Count} etkinlik, {bundle.Tasks.Count} görev; "
                + $"{_syncRequests} istek, {_syncBodyBytes / 1024.0:0.0} KB gövde (sıkıştırma açılmış).");

            return true;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return false;
        }
        catch (OperationCanceledException)
        {
            // İptal istenmediyse bu HttpClient'ın 30 saniyelik zaman aşımı.
            IsOnline = false;
            SonHata = "Google yanıt vermedi (zaman aşımı).";
            return false;
        }
        catch (HttpRequestException ex)
        {
            IsOnline = false;
            SonHata = ex.StatusCode is null
                ? "Google'a ulaşılamadı: " + ex.Message
                : ex.Message;

            if (ex.StatusCode is null)
            {
                // Ağ yok: kullanıcıyı rahatsız etmiyoruz, önbellek gösterilmeye devam eder.
                Log.Debug("Google'a ulaşılamadı: " + ex.Message);
            }
            else
            {
                // Sunucudan hata geldi (API kapalı, kota, izin). Bu sessizce
                // geçilmemeli; sorunu ancak günlükten anlayabiliriz.
                Log.Warn($"Google isteği reddedildi ({(int)ex.StatusCode}): {ex.Message}");
            }

            return false;
        }
        catch (Exception ex)
        {
            IsOnline = false;
            SonHata = ex.GetType().Name + ": " + ex.Message;
            Log.Warn("Google eşitlemesi başarısız.", ex);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    // ------------------------------------------------------------- istekler

    private async Task<List<ProviderCalendar>> FetchCalendarsAsync(string token, CancellationToken cancel)
    {
        var result = new List<ProviderCalendar>();
        string? pageToken = null;

        do
        {
            string url = $"{CalendarBase}/users/me/calendarList"
                + "?minAccessRole=reader&maxResults=250"
                + "&fields=" + Uri.EscapeDataString(
                    "items(id,summary,summaryOverride,backgroundColor,primary,deleted),nextPageToken")
                + Page(pageToken);

            CalendarListResponse? page = await GetAsync<CalendarListResponse>(url, token, cancel)
                .ConfigureAwait(false);

            foreach (CalendarListEntry item in page?.Items ?? new List<CalendarListEntry>())
            {
                if (string.IsNullOrEmpty(item.Id) || item.Deleted == true)
                {
                    continue;
                }

                result.Add(new ProviderCalendar
                {
                    Id = item.Id,
                    Name = item.SummaryOverride ?? item.Summary ?? item.Id,
                    ColorHex = Normalize(item.BackgroundColor) ?? "#4C8DFF",
                    Primary = item.Primary == true,
                });
            }

            pageToken = page?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    private async Task<Dictionary<string, string>> FetchColorsAsync(string token, CancellationToken cancel)
    {
        var map = new Dictionary<string, string>();

        ColorsResponse? colors = await GetAsync<ColorsResponse>(
            $"{CalendarBase}/colors?fields=event", token, cancel).ConfigureAwait(false);

        foreach ((string id, ColorEntry entry) in colors?.Event ?? new Dictionary<string, ColorEntry>())
        {
            string? hex = Normalize(entry.Background);
            if (hex is not null)
            {
                map[id] = hex;
            }
        }

        return map;
    }

    /// <summary>
    /// Takvimlerdeki etkinlik etiketlerini ve gerçek renklerini toplar.
    ///
    /// Google, eski 11 renklik listeyi "etkinlik etiketi" sistemiyle değiştirdi:
    /// kullanıcı takvime kendi renklerini tanımlıyor. Etkinlikte yalnızca
    /// etiketin kimliği geliyor; rengi burada, takvim kaynağında duruyor.
    /// Eski colorId alanı bu etkinliklerde en yakın palet rengini gösteriyor,
    /// yani ona bakarsak ekranda Google'dakinden başka bir renk çiziyoruz.
    /// </summary>
    private async Task<Dictionary<string, string>> FetchEventLabelsAsync(string token, ProviderBundle bundle,
        List<string> secim, CancellationToken cancel)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyList<string> wanted = SelectedCalendarIds(bundle, secim);

        foreach (ProviderCalendar calendar in bundle.Calendars)
        {
            if (!wanted.Contains(calendar.Id))
            {
                continue;
            }

            string url = $"{CalendarBase}/calendars/{Uri.EscapeDataString(calendar.Id)}"
                + "?fields=" + Uri.EscapeDataString("labelProperties(eventLabels(id,backgroundColor))");

            CalendarResource? resource;
            try
            {
                resource = await GetAsync<CalendarResource>(url, token, cancel).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound
                or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                // Etiket alanını tanımayan ya da erişilemeyen takvimi atlıyoruz;
                // o takvimde eski renk davranışı sürer.
                Log.Debug($"Etkinlik etiketleri alınamadı ({calendar.Name}): {ex.StatusCode}");
                continue;
            }

            foreach (EventLabelEntry label in resource?.LabelProperties?.EventLabels
                ?? new List<EventLabelEntry>())
            {
                if (string.IsNullOrEmpty(label.Id))
                {
                    continue;
                }

                // Etiket rengi kullanıcının kendi seçtiği güncel değer;
                // eski palet çevrimine sokulmaz.
                string hex = (label.BackgroundColor ?? string.Empty).Trim();
                if (hex.Length > 0)
                {
                    map[label.Id] = hex.StartsWith('#') ? hex : "#" + hex;
                }
            }
        }

        Log.Debug($"Etkinlik etiketi rengi: {map.Count} adet.");
        return map;
    }

    private async Task<List<ProviderTaskList>> FetchTaskListsAsync(string token, CancellationToken cancel)
    {
        var result = new List<ProviderTaskList>();
        string? pageToken = null;

        do
        {
            string url = $"{TasksBase}/users/@me/lists?maxResults=100"
                + "&fields=" + Uri.EscapeDataString("items(id,title),nextPageToken")
                + Page(pageToken);

            TaskListsResponse? page;
            try
            {
                page = await GetAsync<TaskListsResponse>(url, token, cancel).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden)
            {
                // Tasks API etkinleştirilmemiş olabilir; takvim yine de çalışsın.
                Log.Warn("Google Görevler'e erişilemedi. Google Cloud projesinde Tasks API etkin mi?");
                return result;
            }

            foreach (TaskListEntry item in page?.Items ?? new List<TaskListEntry>())
            {
                if (string.IsNullOrEmpty(item.Id))
                {
                    continue;
                }

                result.Add(new ProviderTaskList
                {
                    Id = item.Id,
                    Name = item.Title ?? item.Id,
                });
            }

            pageToken = page?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return result;
    }

    private async Task FetchEventsAsync(string token, ProviderBundle bundle,
        DateOnly from, DateOnly to, DateTimeOffset? updatedMin, List<string> secim, CancellationToken cancel)
    {
        // Şartname: görünen ay ± 1 gün.
        string timeMin = Rfc3339(from.AddDays(-1).ToDateTime(TimeOnly.MinValue));
        string timeMax = Rfc3339(to.AddDays(2).ToDateTime(TimeOnly.MinValue));

        // ToDictionary değil: elde aynı kaydın iki kopyası olursa eşitleme
        // "aynı anahtar" hatasıyla düşmesin, sonuncusu kalsın.
        var byId = new Dictionary<string, ProviderEvent>();
        foreach (ProviderEvent e in bundle.Events)
        {
            byId[e.Id + "@" + e.CalendarId] = e;
        }

        IReadOnlyList<string> wanted = SelectedCalendarIds(bundle, secim);
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        // Tam yenilemede etiketler az önce indi; artımlı yoklamada ise en fazla
        // bir kez, tanınmayan bir etiket görülürse yeniden isteniyor.
        bool labelsFresh = updatedMin is null;

        foreach (ProviderCalendar calendar in bundle.Calendars)
        {
            if (!wanted.Contains(calendar.Id))
            {
                continue;
            }

            if (updatedMin is not null)
            {
                if (!await HasChangedSinceAsync(token, calendar, updatedMin.Value, cancel).ConfigureAwait(false))
                {
                    continue;
                }

                Log.Debug($"{calendar.Name}: son yoklamadan beri değişiklik var; aralık yeniden alınıyor.");

                // Değişiklik var: bu takvimin aralığı baştan alınıyor. Aralığın
                // dışına taşınan ya da silinen kayıt yeni listede olmadığı için
                // kendiliğinden düşüyor. Sınırlı bir artımlı sorgu bunu göremiyordu:
                // Google taşınan kaydı yeni tarihine göre süzüp hiç döndürmüyor.
                foreach (string stale in byId.Where(p => p.Value.CalendarId == calendar.Id)
                    .Select(p => p.Key).ToList())
                {
                    byId.Remove(stale);
                }
            }

            string? pageToken = null;

            do
            {
                string url = $"{CalendarBase}/calendars/{Uri.EscapeDataString(calendar.Id)}/events"
                    + $"?timeMin={Uri.EscapeDataString(timeMin)}&timeMax={Uri.EscapeDataString(timeMax)}"
                    + "&singleEvents=true&maxResults=2500"

                    // Etkinlik etiketlerinin kimliği ancak bu parametreyle geliyor;
                    // yoksa Google eski colorId'yi, yani en yakın palet rengini veriyor.
                    + "&eventLabelVersion=1"
                    + "&showDeleted=false"
                    + "&fields=" + Uri.EscapeDataString(
                        "items(id,summary,status,colorId,eventLabelId,eventType,start,end),nextPageToken")
                    + Page(pageToken);

                EventsResponse? page;
                try
                {
                    page = await GetAsync<EventsResponse>(url, token, cancel).ConfigureAwait(false);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    Log.Debug($"Takvim atlandı ({calendar.Name}): {ex.StatusCode}");
                    break;
                }

                foreach (EventEntry item in page?.Items ?? new List<EventEntry>())
                {
                    if (string.IsNullOrEmpty(item.Id))
                    {
                        continue;
                    }

                    string key = item.Id + "@" + calendar.Id;
                    seenTypes.Add(item.EventType ?? "(yok)");

                    if (string.Equals(item.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                    {
                        byId.Remove(key);
                        continue;
                    }

                    // Google, Görevler'i de takvim etkinliği olarak döndürüyor.
                    // Onları Tasks API'sinden zaten alıyoruz; buradan geçerse
                    // aynı kayıt iki kez görünüyor. Çalışma konumu kayıtları da
                    // randevu değil, gösterilmiyor.
                    if (item.EventType is "task" or "workingLocation")
                    {
                        byId.Remove(key);
                        continue;
                    }

                    // Google'da yeni tanımlanan bir etiket, bir sonraki tam
                    // yenilemeyi beklemeden doğru renkte görünsün.
                    if (!labelsFresh && !string.IsNullOrEmpty(item.EventLabelId)
                        && !bundle.EventLabels.ContainsKey(item.EventLabelId))
                    {
                        labelsFresh = true;
                        Log.Debug("Tanınmayan etkinlik etiketi; etiket renkleri yeniden alınıyor.");
                        bundle.EventLabels = await FetchEventLabelsAsync(token, bundle, secim, cancel).ConfigureAwait(false);
                    }

                    ProviderEvent? converted = Convert(item, calendar, bundle.EventColors, bundle.EventLabels);
                    if (converted is not null)
                    {
                        byId[key] = converted;
                    }
                }

                pageToken = page?.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));
        }

        Log.Debug("Görülen etkinlik türleri: " + string.Join(", ", seenTypes));

        // Aralık dışına düşmüş ya da artık seçili olmayan takvimlerin kayıtlarını at.
        bundle.Events = byId.Values
            .Where(e => wanted.Contains(e.CalendarId) && e.EndDate >= from && e.StartDate <= to)
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.StartTime ?? TimeOnly.MinValue)
            .ToList();
    }

    /// <summary>
    /// Takvimde son yoklamadan beri değişen (taşınan, silinen, düzenlenen) bir
    /// kayıt var mı? Tarih sınırı bilerek konmuyor: görünen ayın dışına taşınan
    /// kayıt da "değişti" sayılsın. Tek bir kimlik istendiği için yanıt küçük.
    /// </summary>
    private async Task<bool> HasChangedSinceAsync(string token, ProviderCalendar calendar,
        DateTimeOffset updatedMin, CancellationToken cancel)
    {
        string url = $"{CalendarBase}/calendars/{Uri.EscapeDataString(calendar.Id)}/events"
            + $"?updatedMin={Uri.EscapeDataString(Rfc3339(updatedMin.UtcDateTime))}"
            + "&showDeleted=true&maxResults=1"
            + "&fields=" + Uri.EscapeDataString("items(id),nextPageToken");

        EventsResponse? page;
        try
        {
            page = await GetAsync<EventsResponse>(url, token, cancel).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is not null)
        {
            // Bu soruyu kabul etmeyen bir takvim olursa değişmiş sayıp aralığını
            // baştan alıyoruz; en kötü ihtimalle eski (her yoklamada indirme)
            // davranışına döneriz, kayıt kaçırmayız.
            Log.Debug($"Değişiklik sorusu reddedildi ({calendar.Name}): {ex.StatusCode}");
            return true;
        }

        // Google boş bir sayfayla birlikte devam anahtarı da verebiliyor;
        // o da daha fazla sonuç, yani değişiklik olduğu anlamına geliyor.
        return (page?.Items?.Count ?? 0) > 0 || !string.IsNullOrEmpty(page?.NextPageToken);
    }

    private async Task FetchTasksAsync(string token, ProviderBundle bundle,
        DateOnly from, DateOnly to, DateTimeOffset? updatedMin, List<string> secim, CancellationToken cancel)
    {
        string dueMin = Rfc3339(from.ToDateTime(TimeOnly.MinValue));
        string dueMax = Rfc3339(to.AddDays(1).ToDateTime(TimeOnly.MinValue));

        // ToDictionary değil: ay belleğinden gelen pakette aynı görevin iki
        // kopyası olursa eşitleme "aynı anahtar" hatasıyla düşmesin.
        var byId = new Dictionary<string, ProviderTask>();
        foreach (ProviderTask t in bundle.Tasks)
        {
            byId[t.Id + "@" + t.ListId] = t;
        }

        IReadOnlyList<string> wanted = SelectedTaskListIds(bundle, secim);

        foreach (ProviderTaskList list in bundle.TaskLists)
        {
            if (!wanted.Contains(list.Id))
            {
                continue;
            }

            string? pageToken = null;

            do
            {
                string url = $"{TasksBase}/lists/{Uri.EscapeDataString(list.Id)}/tasks?maxResults=100"
                    // Artımlı yoklamada tarih sınırı konmuyor: görünen ayın dışına
                    // taşınan görev de yeni tarihiyle gelsin, sondaki aralık süzgeci
                    // onu düşürsün. Sınır olunca Google onu hiç döndürmüyordu.
                    + (updatedMin is null
                        ? $"&dueMin={Uri.EscapeDataString(dueMin)}&dueMax={Uri.EscapeDataString(dueMax)}"
                        : string.Empty)
                    // Tamamlanmış görevler de isteniyor: Google Takvim onları
                    // gösteriyor, Kalender de göstersin (gri olarak). Tamamlanan
                    // bir görev Google'da "hidden" işaretlendiği için showHidden
                    // olmadan hiç gelmiyor.
                    + "&showCompleted=true&showHidden=true"
                    + (updatedMin is null
                        ? "&showDeleted=false"
                        : "&showDeleted=true"
                          + $"&updatedMin={Uri.EscapeDataString(Rfc3339(updatedMin.Value.UtcDateTime))}")
                    + "&fields=" + Uri.EscapeDataString("items(id,title,due,status,deleted,hidden),nextPageToken")
                    + Page(pageToken);

                TasksResponse? page;
                try
                {
                    page = await GetAsync<TasksResponse>(url, token, cancel).ConfigureAwait(false);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                {
                    Log.Debug($"Görev listesi atlandı ({list.Name}): {ex.StatusCode}");
                    break;
                }

                foreach (TaskEntry item in page?.Items ?? new List<TaskEntry>())
                {
                    if (string.IsNullOrEmpty(item.Id))
                    {
                        continue;
                    }

                    string key = item.Id + "@" + list.Id;

                    // "hidden" eleme ölçütü değil: Google tamamlanan görevleri
                    // ve yinelenen görevlerin kopyalarını gizli işaretliyor.
                    // Tamamlanmışlığa "status" alanından karar veriyoruz ve
                    // eleme değil, renk sebebi sayıyoruz.
                    if (item.Deleted == true || !TryParseDue(item.Due, out DateOnly due))
                    {
                        byId.Remove(key);
                        continue;
                    }

                    byId[key] = new ProviderTask
                    {
                        Id = item.Id,
                        ListId = list.Id,
                        Title = (item.Title ?? string.Empty).Trim(),
                        Due = due,
                        Completed = string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase),
                    };
                }

                pageToken = page?.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));
        }

        bundle.Tasks = byId.Values
            .Where(t => wanted.Contains(t.ListId) && t.Due >= from && t.Due <= to)
            .Where(t => !string.IsNullOrWhiteSpace(t.Title))
            .OrderBy(t => t.Due)
            .ToList();

        // Google Görevler API'si yinelenen görevlerin ileri tarihli kopyalarını
        // her zaman vermiyor; hangi günlerin geldiğini görebilmek için yazıyoruz.
        Log.Debug($"Görevler ({from:dd.MM}-{to:dd.MM}): {bundle.Tasks.Count} görev "
            + $"({bundle.Tasks.Count(t => t.Completed)} tamamlanmış) · "
            + string.Join(", ", bundle.Tasks.Select(t => t.Due.ToString("dd.MM"))));
    }

    // ------------------------------------------------------------- yardımcı

    /// <summary>
    /// Gösterilecek takvimler. Boş seçim "hepsi" demek; yalnız
    /// <see cref="GoogleSettings.HicbiriSecili"/> içeren seçim hiçbir takvimle
    /// eşleşmez, yani "hiçbiri".
    /// </summary>
    private static IReadOnlyList<string> SelectedCalendarIds(ProviderBundle bundle, List<string> chosen)
        => chosen.Count == 0 ? bundle.Calendars.Select(c => c.Id).ToList() : chosen;

    private static IReadOnlyList<string> SelectedTaskListIds(ProviderBundle bundle, List<string> chosen)
        => chosen.Count == 0 ? bundle.TaskLists.Select(l => l.Id).ToList() : chosen;

    private static ProviderEvent? Convert(EventEntry item, ProviderCalendar calendar,
        IReadOnlyDictionary<string, string> colors, IReadOnlyDictionary<string, string> labels)
    {
        // Renk önceliği: etkinlik etiketi > eski renk kimliği > takvimin rengi.
        // Etiket varken eski colorId en yakın palet rengini gösteriyor, yani
        // ona bakarsak Google'da gördüğünüzden başka bir renk çizeriz.
        string color = calendar.ColorHex;

        if (!string.IsNullOrEmpty(item.ColorId) && colors.TryGetValue(item.ColorId, out string? own))
        {
            color = own;
        }

        if (!string.IsNullOrEmpty(item.EventLabelId) && labels.TryGetValue(item.EventLabelId, out string? label))
        {
            color = label;
        }

        // Tüm gün etkinlikler
        if (!string.IsNullOrEmpty(item.Start?.Date))
        {
            if (!DateOnly.TryParse(item.Start.Date, CultureInfo.InvariantCulture, out DateOnly startDate))
            {
                return null;
            }

            DateOnly endDate = startDate;
            if (!string.IsNullOrEmpty(item.End?.Date)
                && DateOnly.TryParse(item.End.Date, CultureInfo.InvariantCulture, out DateOnly exclusive)
                && exclusive > startDate)
            {
                endDate = exclusive.AddDays(-1);
            }

            return new ProviderEvent
            {
                Id = item.Id!,
                CalendarId = calendar.Id,
                Title = (item.Summary ?? "(başlıksız)").Trim(),
                AllDay = true,
                StartDate = startDate,
                EndDate = endDate,
                ColorHex = color,
            };
        }

        // Saatli etkinlikler
        if (item.Start?.DateTime is null)
        {
            return null;
        }

        DateTimeOffset start = item.Start.DateTime.Value.ToLocalTime();
        DateTimeOffset end = (item.End?.DateTime ?? item.Start.DateTime.Value).ToLocalTime();

        // Google'da bitiş anı dışlayıcı: 22:00–24:00 etkinliğin bitişi ertesi gün
        // 00:00, ama etkinlik o gün sürmüyor. Bitiş tam gece yarısıysa son gün bir
        // önceki gün; yoksa ertesi güne de saatsiz bir kayıt düşüyordu.
        DateOnly lastDay = DateOnly.FromDateTime(end.DateTime);
        if (end > start && end.TimeOfDay == TimeSpan.Zero)
        {
            lastDay = lastDay.AddDays(-1);
        }

        return new ProviderEvent
        {
            Id = item.Id!,
            CalendarId = calendar.Id,
            Title = (item.Summary ?? "(başlıksız)").Trim(),
            AllDay = false,
            StartDate = DateOnly.FromDateTime(start.DateTime),
            StartTime = TimeOnly.FromDateTime(start.DateTime),
            EndDate = lastDay,
            ColorHex = color,
        };
    }

    /// <summary>
    /// Google Tasks son tarihi RFC3339 olarak verir ama yalnızca gün kısmı
    /// anlamlıdır. Saat dilimine çevirirsek gün kayabilir; o yüzden metinden
    /// doğrudan gün alınıyor.
    /// </summary>
    private static bool TryParseDue(string? due, out DateOnly date)
    {
        date = default;

        if (string.IsNullOrEmpty(due) || due.Length < 10)
        {
            return false;
        }

        return DateOnly.TryParseExact(due[..10], "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private async Task<T?> GetAsync<T>(string url, string token, CancellationToken cancel, bool yenilendi = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url)
        {
            // HttpClient'ın varsayılan sürümü yalnızca kendi kurduğu isteklere
            // (GetAsync gibi) uygulanıyor; elle kurulan iletide açıkça verilmezse
            // HTTP/1.1 ile gidiyor. Günlük 1.6.1'in ilk derlemesinde bunu gösterdi.
            Version = _http.DefaultRequestVersion,
            VersionPolicy = _http.DefaultVersionPolicy,
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await _http.SendAsync(request, cancel).ConfigureAwait(false);

        if (!_versionLogged)
        {
            // Bir kez: HTTP/2 kabul edildi mi, yoksa 1.1'e mi düşüldü?
            _versionLogged = true;
            Log.Info($"Google'a HTTP/{response.Version} ile bağlanıldı.");
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && !yenilendi)
        {
            // Erişim anahtarı eskimiş ya da Google onu süresinden önce geçersiz
            // saymış olabilir. Reddedilen anahtar bırakılıp yenisiyle bir kez daha
            // deneniyor. Önceden bellekteki anahtar "süresi dolmadı" diye yine
            // veriliyordu; her yoklama 401 ile düşüyor, takvim saatlerce eski
            // veriyle kalıyordu (2026-10-01).
            _auth.ErisimAnahtariniBirak(token);
            string? fresh = await _auth.GetAccessTokenAsync(cancel).ConfigureAwait(false);
            if (fresh is not null && fresh != token)
            {
                return await GetAsync<T>(url, fresh, cancel, yenilendi: true).ConfigureAwait(false);
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            throw new HttpRequestException(
                $"Google isteği başarısız ({(int)response.StatusCode}): {body[..Math.Min(200, body.Length)]}",
                null,
                response.StatusCode);
        }

        // Gövde bir kerede okunuyor ki veri kullanımı günlüğe sayılabilsin;
        // yanıtlar küçük (görünen ayın kayıtları), belleğe sığar.
        byte[] payload = await response.Content.ReadAsByteArrayAsync(cancel).ConfigureAwait(false);
        _syncRequests++;
        _syncBodyBytes += payload.Length;

        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    private static string Page(string? token)
        => string.IsNullOrEmpty(token) ? string.Empty : "&pageToken=" + Uri.EscapeDataString(token);

    private static string Rfc3339(DateTime value)
        => value.Kind == DateTimeKind.Utc
            ? value.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
            : new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value))
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>
    /// Google'dan gelen rengi kullanılabilir hâle getirir: başına # koyar ve
    /// API'nin hâlâ döndürdüğü eski paleti, Google Takvim'in ekranda
    /// kullandığı güncel renge çevirir.
    /// </summary>
    private static string? Normalize(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        return GoogleColorPalette.ToCurrent(hex);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
