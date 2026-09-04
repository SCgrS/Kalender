using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kalender.Core;

namespace Kalender.Calendar.Holidays;

/// <summary>Diske yazılan tatil kaydı.</summary>
internal sealed class StoredHoliday
{
    public DateOnly Date { get; set; }

    public string Name { get; set; } = string.Empty;

    public HolidayKind Kind { get; set; }

    public bool DateUncertain { get; set; }
}

/// <summary>Tatil önbelleğinin dosya biçimi.</summary>
internal sealed class HolidayCacheFile
{
    public DateTimeOffset FetchedUtc { get; set; }

    /// <summary>Sunucunun verdiği sürüm etiketi; bir dahaki sefere koşullu istek atmak için.</summary>
    public string? ETag { get; set; }

    public string? LastModified { get; set; }

    public List<StoredHoliday> Days { get; set; } = new();
}

/// <summary>
/// Türkiye resmi tatillerini Google'ın herkese açık ICS akışından indirir,
/// yerel olarak saklar ve gün bazında sorgulanabilir hâle getirir.
///
/// Veri kullanımı bilinçli olarak düşük tutuldu: dosya ayda bir kez, sıkıştırılmış
/// olarak indiriliyor; sunucu sürüm etiketi verirse koşullu istek atılıyor ve
/// değişmemişse gövde hiç inmiyor. İnternet yoksa önbellekteki liste sessizce
/// kullanılmaya devam eder, kullanıcıya hata gösterilmez.
/// </summary>
internal sealed class HolidayService : IDisposable
{
    /// <summary>Şartnamede verilen, anahtar gerektirmeyen adres.</summary>
    private const string FeedUrl =
        "https://calendar.google.com/calendar/ical/tr.turkish%23holiday%40group.v.calendar.google.com/public/basic.ics";

    /// <summary>Bu süre geçmeden yeniden indirilmez.</summary>
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(30);

    /// <summary>Önbellekte tutulacak ileri dönem.</summary>
    private const int KeepMonthsAhead = 13;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http;

    private Dictionary<DateOnly, HolidayInfo> _days = new();
    private HolidayCacheFile? _cache;

    public HolidayService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Kalender/0.3 (+windows)");
    }

    /// <summary>Liste değişti (indirildi ya da önbellekten yüklendi).</summary>
    /// <remarks>Arka plan iş parçacığından tetiklenebilir.</remarks>
    public event Action? Updated;

    /// <summary>Günden tatile bakış. Değiştirilemez; her yenilemede yenisiyle değişir.</summary>
    public IReadOnlyDictionary<DateOnly, HolidayInfo> Days => _days;

    public DateTimeOffset? LastFetchUtc => _cache?.FetchedUtc;

    public int Count => _days.Count;

    /// <summary>Diskteki listeyi yükler. Açılışta çağrılır, ağa çıkmaz.</summary>
    public void LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                return;
            }

            var cache = JsonSerializer.Deserialize<HolidayCacheFile>(
                File.ReadAllText(CachePath, Encoding.UTF8), JsonOptions);

            if (cache is null)
            {
                return;
            }

            _cache = cache;
            _days = Build(cache.Days);
            Log.Info($"Tatil listesi önbellekten yüklendi: {_days.Count} gün.");
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Warn("Tatil önbelleği okunamadı.", ex);
        }
    }

    /// <summary>
    /// Gerekiyorsa listeyi yeniler.
    /// </summary>
    /// <param name="force">Süresi dolmamış olsa bile indir.</param>
    public async Task RefreshAsync(bool force = false, CancellationToken cancel = default)
    {
        if (!await _gate.WaitAsync(0, cancel).ConfigureAwait(false))
        {
            // Zaten bir yenileme sürüyor.
            return;
        }

        try
        {
            if (!force && _cache is not null
                && DateTimeOffset.UtcNow - _cache.FetchedUtc < RefreshAfter
                && _days.Count > 0)
            {
                Log.Debug("Tatil listesi güncel; indirilmedi.");
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, FeedUrl);

            if (_cache is not null && _days.Count > 0)
            {
                if (!string.IsNullOrWhiteSpace(_cache.ETag))
                {
                    request.Headers.TryAddWithoutValidation("If-None-Match", _cache.ETag);
                }
                else if (!string.IsNullOrWhiteSpace(_cache.LastModified))
                {
                    request.Headers.TryAddWithoutValidation("If-Modified-Since", _cache.LastModified);
                }
            }

            using HttpResponseMessage response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancel)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                Log.Info("Tatil listesi değişmemiş (304).");
                Touch();
                return;
            }

            response.EnsureSuccessStatusCode();

            string ics = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            IReadOnlyList<IcsEvent> events = IcsParser.Parse(ics);
            List<StoredHoliday> days = Extract(events);

            if (days.Count == 0)
            {
                Log.Warn($"Tatil akışı okundu ama hiç tatil bulunamadı ({events.Count} kayıt). Önbellek korunuyor.");
                return;
            }

            var cache = new HolidayCacheFile
            {
                FetchedUtc = DateTimeOffset.UtcNow,
                ETag = response.Headers.ETag?.ToString(),
                LastModified = response.Content.Headers.LastModified?.ToString("R"),
                Days = days,
            };

            _cache = cache;
            _days = Build(days);
            Save(cache);

            Log.Info($"Tatil listesi güncellendi: {_days.Count} gün.");
            Updated?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Kapanış ya da zaman aşımı; sessizce geç.
        }
        catch (Exception ex)
        {
            // İnternet yoksa kullanıcıyı rahatsız etmiyoruz; önbellek kullanılmaya devam eder.
            Log.Warn("Tatil listesi indirilemedi; önbellek kullanılıyor.", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CachePath => Path.Combine(Paths.CacheFolder, "tatiller.json");

    /// <summary>304 sonrası tarihi ileri alır ki bir ay daha istek atılmasın.</summary>
    private void Touch()
    {
        if (_cache is null)
        {
            return;
        }

        _cache.FetchedUtc = DateTimeOffset.UtcNow;
        Save(_cache);
    }

    private static void Save(HolidayCacheFile cache)
    {
        try
        {
            Directory.CreateDirectory(Paths.CacheFolder);
            string temp = CachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(cache, JsonOptions), new UTF8Encoding(false));

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
            Log.Warn("Tatil önbelleği yazılamadı.", ex);
        }
    }

    /// <summary>
    /// ICS kayıtlarından resmi tatilleri süzer.
    ///
    /// Akışta üç tür kayıt var: "Resmi tatil", yarım günlük tatiller ve
    /// "Kutlama" (Anneler Günü gibi anma günleri). Kutlamalar kırmızıya
    /// boyanmamalı, o yüzden dışarıda bırakılıyor.
    /// </summary>
    private static List<StoredHoliday> Extract(IReadOnlyList<IcsEvent> events)
    {
        DateOnly from = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        DateOnly to = from.AddMonths(KeepMonthsAhead + 1);

        var result = new Dictionary<DateOnly, StoredHoliday>();
        int celebrations = 0;
        int unknown = 0;

        foreach (IcsEvent e in events)
        {
            string description = e.Description;

            bool isCelebration = description.Contains("Kutlama", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Observance", StringComparison.OrdinalIgnoreCase);

            bool isHoliday = description.Contains("Resmi tatil", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Resmî tatil", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Public holiday", StringComparison.OrdinalIgnoreCase);

            if (isCelebration)
            {
                celebrations++;
                continue;
            }

            if (!isHoliday)
            {
                unknown++;
                continue;
            }

            HolidayKind kind = description.Contains("yarım gün", StringComparison.OrdinalIgnoreCase)
                || description.Contains("half day", StringComparison.OrdinalIgnoreCase)
                ? HolidayKind.HalfDay
                : HolidayKind.Full;

            bool uncertain = description.Contains("geçici", StringComparison.OrdinalIgnoreCase)
                || description.Contains("subject to change", StringComparison.OrdinalIgnoreCase);

            for (DateOnly day = e.Start; day < e.EndExclusive; day = day.AddDays(1))
            {
                if (day < from || day >= to)
                {
                    continue;
                }

                var record = new StoredHoliday
                {
                    Date = day,
                    Name = e.Summary.Trim(),
                    Kind = kind,
                    DateUncertain = uncertain,
                };

                // Aynı güne birden çok kayıt düşerse tam gün olan kazanır.
                if (!result.TryGetValue(day, out StoredHoliday? existing)
                    || (existing.Kind == HolidayKind.HalfDay && kind == HolidayKind.Full))
                {
                    result[day] = record;
                }
            }
        }

        Log.Debug($"Tatil süzgeci: {result.Count} gün, {celebrations} kutlama atlandı, {unknown} tanınmayan kayıt.");
        return result.Values.OrderBy(d => d.Date).ToList();
    }

    private static Dictionary<DateOnly, HolidayInfo> Build(IEnumerable<StoredHoliday> days)
    {
        var map = new Dictionary<DateOnly, HolidayInfo>();

        foreach (StoredHoliday d in days)
        {
            map[d.Date] = new HolidayInfo(d.Date, d.Name, d.Kind, d.DateUncertain);
        }

        return map;
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
