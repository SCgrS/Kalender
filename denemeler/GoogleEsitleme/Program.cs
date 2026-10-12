using System.Collections;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

// Kalender 1.10 düzeltmesinin sınaması (yansımayla):
//  1. Google bellekteki erişim anahtarını süresi dolmadan reddederse (401)
//     yenisi alınıp istek yinelenir; eşitleme beklemeden tutar.
//  2. Uykudan uyanınca bellekteki anahtar bırakılır.
//  3. Bellekten geri gelen ayın eski kopyası, iki ayın ortak günlerinde daha
//     yeni alınmış verinin üstüne yazılmaz (2026-10-01: griye çekilen işler
//     ay dönünce kırmızı göründü).
//  4. Eşitleme 5 dakika boyunca hiç tutmazsa bu, varsayılan günlük düzeyinde
//     (yalnız hatalar) de yazılır; düzelince o da yazılır.
//
// Genel kod incelemesinin (2026-10-12) Google maddeleri (numaralar kalender.md'den):
//   9. Tam gece yarısında biten saatli etkinlik ertesi güne düşmez.
//  10. Bütün takvimler kapatılınca ("hiçbiri" işareti) hiçbir etkinlik alınmaz.
//  11. Seçim eşitlemenin başında kopyalanır; zorlanmış istek süren zorlanmış
//      işe katılmaz, onu iptal edip yeniden başlar.
//  14. Bağlantı keserken süren eşitleme yerel kopyayı geri yazmaz; yenilenen
//      anahtar kesilen bağlantıya yazılmaz; bağlantı yokken kopya yüklenmez.
//  15. Onaydan sonraki zaman aşımı "onay gelmedi" değil, "Google yanıt vermedi"
//      diye bildirilir (tarayıcı açılmaz, onayı sınama verir).
//  16. "Takvimde aç" adresine bağlı hesabın e-postası (authuser) eklenir.
//  17. Ay belleği birleştirmesinde görev kimliğe göre elenir; elde aynı kaydın
//      iki kopyası olsa da eşitleme düşmez.
//  34. Saat dilimi değişince ay belleği atılır, sıradaki eşitleme (zorlanmasa
//      da) tam yapılır; saatli etkinlik yeni dilimin saatiyle saklanır. Windows'un
//      dilimi değiştirilmez: .NET'in önbelleğine sahte bir dilim konur.
//  38. Artımlı yoklamanın sınır anı son yoklamadan 2 dakika geriden sorulur.
//
// Google yerine bu programın içindeki sahte bir HTTP işleyicisi yanıt verir;
// ağa çıkılmaz. Uygulamanın klasörü (Paths.RootFolder) %TEMP% altında geçici
// bir klasöre çevrilir: kurulu Kalender'in ayarlarına, Google yetkisine,
// önbelleğine ve günlüğüne dokunulmaz. Geçici klasör sonunda silinir.
// Çıkış kodu 0 = hepsi geçti. Kullanım: dotnet run -c Release
internal static class Program
{
    private const BindingFlags Hepsi =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static int _hata;
    private static int _gecen;

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

    private static object? Oku(object o, string ad)
    {
        Type t = o.GetType();
        PropertyInfo? p = t.GetProperty(ad, Hepsi);
        if (p is not null)
        {
            return p.GetValue(o);
        }

        return t.GetField(ad, Hepsi)!.GetValue(o);
    }

    private static void Yaz(object o, string ad, object? deger)
    {
        Type t = o.GetType();
        PropertyInfo? p = t.GetProperty(ad, Hepsi);
        if (p is not null)
        {
            p.SetValue(o, deger);
            return;
        }

        t.GetField(ad, Hepsi)!.SetValue(o, deger);
    }

    private static object? Cagir(object hedef, string ad, params object?[] arg)
    {
        Type t = hedef as Type ?? hedef.GetType();
        MethodInfo m = t.GetMethod(ad, Hepsi) ?? throw new MissingMethodException(t.Name, ad);
        return m.Invoke(hedef is Type ? null : hedef, arg);
    }

    private static void Denetle(string ad, bool sonuc, string ayrinti = "")
    {
        if (sonuc)
        {
            _gecen++;
        }
        else
        {
            _hata++;
        }

        Console.WriteLine($"   {(sonuc ? "GEÇTİ" : "KALDI")}: {ad}{(ayrinti.Length > 0 ? " — " + ayrinti : "")}");
    }

    // ------------------------------------------------------------ sahte Google

    /// <summary>
    /// Google'ın yerine geçer. Yalnız kendi verdiği ve geçerli saydığı
    /// anahtarları kabul eder; ötekilere 401 döner.
    /// </summary>
    private sealed class SahteGoogle : HttpMessageHandler
    {
        public readonly HashSet<string> Gecerli = new(StringComparer.Ordinal);
        public int TokenIstegi;
        public int Ret401;
        public int Istek;

        /// <summary>Verdiği anahtarları da reddetsin mi (izin geri alınmış gibi)?</summary>
        public bool HepReddet;

        public TimeSpan Gecikme;
        public string EtkinlikEtiketi = "GRI";
        private int _sira;

        /// <summary>Yalnız anahtar adresinin (oauth2) gecikmesi.</summary>
        public TimeSpan TokenGecikmesi;

        /// <summary>Anahtar yanıtında yeni bir yenileme anahtarı da verilsin mi?</summary>
        public bool YenilemeAnahtariVer;

        /// <summary>Verilirse tarih aralıklı etkinlik listesi isteklerine bu yanıt döner.</summary>
        public string? EtkinlikYaniti;

        /// <summary>Gelen isteklerin adresleri (anahtar adresi dışında).</summary>
        public readonly List<string> Adresler = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken iptal)
        {
            if (Gecikme > TimeSpan.Zero)
            {
                await Task.Delay(Gecikme, iptal);
            }

            string url = istek.RequestUri!.ToString();

            if (url.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                if (TokenGecikmesi > TimeSpan.Zero)
                {
                    await Task.Delay(TokenGecikmesi, iptal);
                }

                Interlocked.Increment(ref TokenIstegi);
                string yeni = "YENI" + Interlocked.Increment(ref _sira);
                if (!HepReddet)
                {
                    lock (Gecerli)
                    {
                        Gecerli.Add(yeni);
                    }
                }

                return Json(200, YenilemeAnahtariVer
                    ? $$"""{"access_token":"{{yeni}}","refresh_token":"yeni-yenileme","expires_in":3599}"""
                    : $$"""{"access_token":"{{yeni}}","expires_in":3599}""");
            }

            lock (Adresler)
            {
                Adresler.Add(url);
            }

            Interlocked.Increment(ref Istek);
            string? anahtar = istek.Headers.Authorization?.Parameter;
            bool gecerli;
            lock (Gecerli)
            {
                gecerli = anahtar is not null && Gecerli.Contains(anahtar);
            }

            if (!gecerli)
            {
                Interlocked.Increment(ref Ret401);
                return Json(401, """{"error":{"code":401,"message":"Request had invalid authentication credentials.","status":"UNAUTHENTICATED"}}""");
            }

            if (url.Contains("/users/me/calendarList"))
            {
                return Json(200, """{"items":[{"id":"takvim@deneme","summary":"Deneme","backgroundColor":"#d50000","primary":true}]}""");
            }

            if (url.Contains("/colors"))
            {
                return Json(200, """{"event":{"1":{"background":"#7986cb"}}}""");
            }

            if (url.Contains("/users/@me/lists"))
            {
                return Json(200, """{"items":[]}""");
            }

            if (url.Contains("/events"))
            {
                if (url.Contains("updatedMin="))
                {
                    return Json(200, """{"items":[]}""");
                }

                if (EtkinlikYaniti is not null)
                {
                    return Json(200, EtkinlikYaniti);
                }

                return Json(200, $$$"""{"items":[{"id":"e30","summary":"Dava","status":"confirmed","colorId":"1","eventLabelId":"{{{EtkinlikEtiketi}}}","start":{"date":"2026-09-30"},"end":{"date":"2026-10-01"}}]}""");
            }

            if (url.Contains("/calendars/"))
            {
                return Json(200, """{"labelProperties":{"eventLabels":[{"id":"GRI","backgroundColor":"#7c7c7c"}]}}""");
            }

            return Json(404, "{}");
        }

        private static HttpResponseMessage Json(int durum, string govde) => new((HttpStatusCode)durum)
        {
            Content = new StringContent(govde, Encoding.UTF8, "application/json"),
        };
    }

    // ------------------------------------------------------------ kurulum

    private static object _ayarlar = null!;

    private static (object Yetki, object Saglayici, SahteGoogle Google) Kur(TimeSpan? zamanAsimi = null)
    {
        var google = new SahteGoogle();

        object yapilandirma = Activator.CreateInstance(Tip("Kalender.Google.SettingsOAuthConfig"), _ayarlar)!;
        object yetki = Activator.CreateInstance(Tip("Kalender.Google.GoogleAuthenticator"), yapilandirma)!;

        object kayit = Activator.CreateInstance(Tip("Kalender.Google.StoredToken"))!;
        Yaz(kayit, "RefreshToken", "yenileme-anahtari");
        Yaz(kayit, "ClientId", "deneme-istemci");
        Yaz(kayit, "AccountEmail", "takvim@deneme");
        Yaz(yetki, "_token", kayit);
        Yaz(yetki, "_http", new HttpClient(google) { Timeout = TimeSpan.FromSeconds(30) });

        object saglayici = Activator.CreateInstance(Tip("Kalender.Google.GoogleCalendarProvider"), yetki, _ayarlar)!;
        Yaz(saglayici, "_http", new HttpClient(google) { Timeout = zamanAsimi ?? TimeSpan.FromSeconds(30) });

        return (yetki, saglayici, google);
    }

    /// <summary>Bellekte "süresi dolmamış" görünen, ama Google'ın tanımadığı bir anahtar.</summary>
    private static void EskiAnahtarKoy(object yetki, string anahtar = "ESKI")
    {
        Yaz(yetki, "_accessToken", anahtar);
        Yaz(yetki, "_accessTokenExpiry", DateTimeOffset.UtcNow.AddMinutes(50));
    }

    private static async Task<bool> Esitle(object saglayici, bool tam = true, CancellationToken iptal = default)
    {
        var gorev = (Task<bool>)Cagir(saglayici, "SyncAsync",
            new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8), tam, iptal)!;
        return await gorev;
    }

    private static string? Renk(object saglayici, string id)
    {
        object veri = Oku(saglayici, "Data")!;
        foreach (object e in (IEnumerable)Oku(veri, "Events")!)
        {
            if ((string)Oku(e, "Id")! == id)
            {
                return (string)Oku(e, "ColorHex")!;
            }
        }

        return null;
    }

    // ------------------------------------------------------------ paket kurucuları

    private static object Etkinlik(string id, DateOnly bas, DateOnly son, string renk, string takvim = "takvim@deneme")
    {
        object e = Activator.CreateInstance(Tip("Kalender.Google.ProviderEvent"))!;
        Yaz(e, "Id", id);
        Yaz(e, "CalendarId", takvim);
        Yaz(e, "Title", id);
        Yaz(e, "AllDay", true);
        Yaz(e, "StartDate", bas);
        Yaz(e, "EndDate", son);
        Yaz(e, "ColorHex", renk);
        return e;
    }

    private static object Gorev(string id, DateOnly gun, bool bitti)
    {
        object g = Activator.CreateInstance(Tip("Kalender.Google.ProviderTask"))!;
        Yaz(g, "Id", id);
        Yaz(g, "ListId", "liste");
        Yaz(g, "Title", id);
        Yaz(g, "Due", gun);
        Yaz(g, "Completed", bitti);
        return g;
    }

    private static object Takvim(string renk)
    {
        object t = Activator.CreateInstance(Tip("Kalender.Google.ProviderCalendar"))!;
        Yaz(t, "Id", "takvim@deneme");
        Yaz(t, "Name", "Deneme");
        Yaz(t, "ColorHex", renk);
        return t;
    }

    private static IList Liste(string tip, IEnumerable<object> ogeler)
    {
        var liste = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip(tip)))!;
        foreach (object o in ogeler)
        {
            liste.Add(o);
        }

        return liste;
    }

    private static object Paket(DateOnly ilk, DateOnly son, DateTimeOffset? esitleme, string hesap,
        IEnumerable<object> etkinlikler, IEnumerable<object>? gorevler = null, string takvimRengi = "#d50000")
    {
        object p = Activator.CreateInstance(Tip("Kalender.Google.ProviderBundle"))!;
        Yaz(p, "AccountEmail", hesap);
        Yaz(p, "RangeFrom", ilk);
        Yaz(p, "RangeTo", son);
        Yaz(p, "FullSyncUtc", esitleme);
        Yaz(p, "LastSyncUtc", esitleme);
        Yaz(p, "Calendars", Liste("Kalender.Google.ProviderCalendar", new[] { Takvim(takvimRengi) }));
        Yaz(p, "Events", Liste("Kalender.Google.ProviderEvent", etkinlikler));
        Yaz(p, "Tasks", Liste("Kalender.Google.ProviderTask", gorevler ?? Array.Empty<object>()));
        return p;
    }

    private static Dictionary<string, string> Renkler(object paket)
    {
        var d = new Dictionary<string, string>();
        foreach (object e in (IEnumerable)Oku(paket, "Events")!)
        {
            d[(string)Oku(e, "Id")!] = (string)Oku(e, "ColorHex")!;
        }

        return d;
    }

    private static Dictionary<string, bool> Gorevler(object paket)
    {
        var d = new Dictionary<string, bool>();
        foreach (object g in (IEnumerable)Oku(paket, "Tasks")!)
        {
            d[(string)Oku(g, "Id")!] = (bool)Oku(g, "Completed")!;
        }

        return d;
    }

    // ------------------------------------------------------------ ana akış

    private static async Task<int> Main()
    {
        string gecici = Path.Combine(Path.GetTempPath(), "kalender-sinama-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(gecici);

        Type yollar = Tip("Kalender.Core.Paths");
        string gercekKok = (string)yollar.GetProperty("RootFolder", Hepsi)!.GetValue(null)!;
        yollar.GetProperty("RootFolder", Hepsi)!.SetValue(null, gecici);
        Cagir(yollar, "EnsureFolders");
        Console.WriteLine("Uygulama klasörü sınama için: " + gecici);
        Console.WriteLine("(Kurulu Kalender'in klasörü, dokunulmuyor: " + gercekKok + ")");

        _ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"))!;
        object ayar = Oku(_ayarlar, "Current")!;
        object googleAyar = Oku(ayar, "Google")!;
        Yaz(googleAyar, "ClientId", "deneme-istemci");
        Yaz(googleAyar, "ClientSecret", "deneme-sir");

        try
        {
            await AnahtarReddi();
            await IzinGeriAlindi();
            Uyaninca();
            await ZamanAsimi();
            Birlestirme();
            GecmistenGetir();
            GunlugeYazma(gecici);

            // Genel kod incelemesi (2026-10-12), Google eşitleme maddeleri.
            await Bolum(Madde9);
            await Bolum(Madde10);
            await Bolum(Madde11);
            await Bolum(Madde14);
            await Bolum(Madde15);
            await Bolum(() =>
            {
                Madde16();
                return Task.CompletedTask;
            });
            await Bolum(Madde17);
            await Bolum(Madde38);
            await Bolum(Madde34);
        }
        finally
        {
            try
            {
                Directory.Delete(gecici, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Geçici klasör silinemedi: " + ex.Message);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    // 1 -------------------------------------------------------------------------

    private static async Task AnahtarReddi()
    {
        Console.WriteLine();
        Console.WriteLine("1) Google bellekteki anahtarı süresinden önce reddediyor");

        var (yetki, saglayici, google) = Kur();
        EskiAnahtarKoy(yetki);

        bool sonuc = await Esitle(saglayici);
        Denetle("eşitleme tuttu", sonuc, $"istek {google.Istek}, 401 {google.Ret401}");
        Denetle("yeni anahtar bir kez alındı", google.TokenIstegi == 1, $"token isteği {google.TokenIstegi}");
        Denetle("bellekte artık yeni anahtar var", (string?)Oku(yetki, "_accessToken") == "YENI1");
        Denetle("griye çekilen iş gri çiziliyor", Renk(saglayici, "e30") == "#7c7c7c", Renk(saglayici, "e30") ?? "yok");
        Denetle("çevrimiçi ve hata yok", (bool)Oku(saglayici, "IsOnline")! && Oku(saglayici, "SonHata") is null);
        Denetle("önbellek geçici klasöre yazıldı",
            File.Exists(Path.Combine((string)Tip("Kalender.Core.Paths").GetProperty("CacheFolder")!.GetValue(null)!, "google.json")));

        // Sonraki yoklama yeni anahtarla, 401'siz gider.
        int oncekiRet = google.Ret401;
        bool ikinci = await Esitle(saglayici, tam: false);
        Denetle("sonraki yoklama 401 almadan geçti", ikinci && google.Ret401 == oncekiRet && google.TokenIstegi == 1,
            $"401 {google.Ret401 - oncekiRet}, token isteği {google.TokenIstegi}");
    }

    private static async Task IzinGeriAlindi()
    {
        Console.WriteLine();
        Console.WriteLine("1b) Google her anahtarı reddediyor; döngüye girilmiyor, sonra düzeliyor");

        var (yetki, saglayici, google) = Kur();
        EskiAnahtarKoy(yetki);
        google.HepReddet = true;

        var gorev = Esitle(saglayici);
        bool bitti = await Task.WhenAny(gorev, Task.Delay(TimeSpan.FromSeconds(20))) == gorev;
        Denetle("eşitleme takılmadan bitti", bitti);
        if (!bitti)
        {
            return;
        }

        string? hata = (string?)Oku(saglayici, "SonHata");
        Denetle("başarısız sayıldı, nedeni 401", !gorev.Result && hata?.Contains("401") == true, hata ?? "yok");
        Denetle("istek başına en çok bir yenileme", google.TokenIstegi == 1, $"token isteği {google.TokenIstegi}");
        Denetle("çevrimdışı gösteriliyor", !(bool)Oku(saglayici, "IsOnline")!);

        // Google yeniden kabul etmeye başlayınca, bellekteki anahtarın süresi dolmasını beklemeden düzelir.
        google.HepReddet = false;
        bool sonra = await Esitle(saglayici);
        Denetle("düzelince ilk yoklamada tuttu", sonra && Oku(saglayici, "SonHata") is null,
            $"token isteği {google.TokenIstegi}");
        Denetle("ve iş gri", Renk(saglayici, "e30") == "#7c7c7c");
    }

    // 2 -------------------------------------------------------------------------

    private static void Uyaninca()
    {
        Console.WriteLine();
        Console.WriteLine("2) Uykudan uyanınca bellekteki anahtar bırakılıyor");

        var (yetki, _, _) = Kur();

        EskiAnahtarKoy(yetki, "A");
        Cagir(yetki, "ErisimAnahtariniBirak", new object?[] { null });
        Denetle("koşulsuz bırakma anahtarı siliyor", Oku(yetki, "_accessToken") is null);

        EskiAnahtarKoy(yetki, "B");
        Cagir(yetki, "ErisimAnahtariniBirak", "A");
        Denetle("arada yenisi alınmışsa reddedilen eski anahtar yenisini silmiyor", (string?)Oku(yetki, "_accessToken") == "B");

        Cagir(yetki, "ErisimAnahtariniBirak", "B");
        Denetle("reddedilen anahtar bellekteyse siliniyor", Oku(yetki, "_accessToken") is null);

        // AppHost.OnResumed bırakmayı poller'ı başlatmadan önce yapıyor mu? Kaynakta bak.
        string kaynak = File.ReadAllText(Path.Combine(DepoKoku(), "src", "Kalender.App", "Core", "AppHost.cs"));
        int uyanis = kaynak.IndexOf("private void OnResumed()", StringComparison.Ordinal);
        int birak = kaynak.IndexOf("_googleAuth?.ErisimAnahtariniBirak();", uyanis, StringComparison.Ordinal);
        int baslat = kaynak.IndexOf("_poller?.Start();", uyanis, StringComparison.Ordinal);
        Denetle("OnResumed önce anahtarı bırakıyor, sonra yoklamayı başlatıyor", uyanis > 0 && birak > uyanis && birak < baslat);
    }

    private static string DepoKoku()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Kalender.App")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    private static async Task ZamanAsimi()
    {
        Console.WriteLine();
        Console.WriteLine("2b) Zaman aşımı hata sayılıyor, gerçek iptal sayılmıyor");

        var (yetki, saglayici, google) = Kur(zamanAsimi: TimeSpan.FromSeconds(1));
        EskiAnahtarKoy(yetki, "GECERLI");
        google.Gecerli.Add("GECERLI");
        google.Gecikme = TimeSpan.FromSeconds(3);

        bool sonuc = await Esitle(saglayici);
        string? hata = (string?)Oku(saglayici, "SonHata");
        Denetle("zaman aşımı başarısızlık olarak kaydedildi", !sonuc && hata?.Contains("zaman aşımı") == true, hata ?? "yok");

        var (yetki2, saglayici2, google2) = Kur();
        EskiAnahtarKoy(yetki2, "GECERLI");
        google2.Gecerli.Add("GECERLI");
        google2.Gecikme = TimeSpan.FromSeconds(3);
        using var iptal = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        bool sonuc2 = await Esitle(saglayici2, iptal: iptal.Token);
        Denetle("iptal edilen eşitleme hata bırakmıyor", !sonuc2 && Oku(saglayici2, "SonHata") is null);
    }

    // 3 -------------------------------------------------------------------------

    private static void Birlestirme()
    {
        Console.WriteLine();
        Console.WriteLine("3) Eski ay kopyası daha yeni verinin üstüne yazılmıyor");

        const string kirmizi = "#D50000";
        const string gri = "#7c7c7c";
        var dun1148 = new DateTimeOffset(2026, 9, 30, 8, 48, 34, TimeSpan.Zero);
        var dun2223 = new DateTimeOffset(2026, 9, 30, 19, 23, 24, TimeSpan.Zero);

        // Ekim, dün öğlen gösterilmiş: 30 Eylül işleri henüz kırmızı.
        object ekim = Paket(new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8), dun1148, "hesap@deneme",
            new[]
            {
                Etkinlik("e30", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), kirmizi),
                Etkinlik("silinen", new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), kirmizi),
                Etkinlik("tasan", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 13), kirmizi),
                Etkinlik("e15", new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), kirmizi),
            },
            new[] { Gorev("g1", new DateOnly(2026, 10, 1), false), Gorev("g20", new DateOnly(2026, 10, 20), false) },
            takvimRengi: "#111111");

        // Eylül, dün gece tazelendi: 30 Eylül işi gri, 2 Ekim'deki iş silinmiş, görev bitmiş.
        object eylul = Paket(new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 11), dun2223, "hesap@deneme",
            new[]
            {
                Etkinlik("e29", new DateOnly(2026, 9, 29), new DateOnly(2026, 9, 29), gri),
                Etkinlik("e30", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), gri),
                Etkinlik("tasan", new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 13), gri),
                Etkinlik("eylul5", new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 5), gri),
            },
            new[] { Gorev("g1", new DateOnly(2026, 10, 1), true) },
            takvimRengi: "#222222");

        Type saglayiciTipi = Tip("Kalender.Google.GoogleCalendarProvider");
        object sonuc = Cagir(saglayiciTipi, "TazesiyleBirlestir", ekim, eylul)!;
        Dictionary<string, string> r = Renkler(sonuc);

        Denetle("30 Eylül işi gri (yeni veriden)", r.GetValueOrDefault("e30") == gri, r.GetValueOrDefault("e30") ?? "yok");
        Denetle("29 Eylül işi de geldi (ortak gün)", r.GetValueOrDefault("e29") == gri);
        Denetle("yeni veride silinen iş gösterilmiyor", !r.ContainsKey("silinen"));
        Denetle("ortak günlerden taşan çok günlü iş yeni hâliyle", r.GetValueOrDefault("tasan") == gri);
        Denetle("yalnız Ekim'e ait gün eski kopyadan korunuyor", r.GetValueOrDefault("e15") == kirmizi);
        Denetle("Ekim'in dışındaki Eylül günü eklenmedi", !r.ContainsKey("eylul5"));
        Denetle("aynı iş iki kez yok", ((IList)Oku(sonuc, "Events")!).Count == 4, $"{((IList)Oku(sonuc, "Events")!).Count} kayıt");

        Dictionary<string, bool> g = Gorevler(sonuc);
        Denetle("ortak gündeki görev yeni hâliyle (bitti)", g.GetValueOrDefault("g1") == true);
        Denetle("Ekim'e ait görev korunuyor", g.ContainsKey("g20"));

        Denetle("aralık Ekim'in", (DateOnly)Oku(sonuc, "RangeFrom")! == new DateOnly(2026, 9, 28)
            && (DateOnly)Oku(sonuc, "RangeTo")! == new DateOnly(2026, 11, 8));
        Denetle("eşitleme zamanı eskide kaldı (sonraki eşitleme ayı baştan tazeler)",
            (DateTimeOffset?)Oku(sonuc, "LastSyncUtc") == dun1148 && (DateTimeOffset?)Oku(sonuc, "FullSyncUtc") == dun1148);
        object takvim = ((IList)Oku(sonuc, "Calendars")!)[0]!;
        Denetle("takvim rengi yeni veriden", (string)Oku(takvim, "ColorHex")! == "#222222");
        Denetle("eski paket değiştirilmedi", Renkler(ekim)["e30"] == kirmizi && ((IList)Oku(ekim, "Events")!).Count == 4);

        // Geri getirilen kopya daha yeniyse dokunulmuyor.
        object ayni = Cagir(saglayiciTipi, "TazesiyleBirlestir", eylul, ekim)!;
        Denetle("geri gelen kopya daha yeniyse olduğu gibi kullanılıyor", ReferenceEquals(ayni, eylul));

        // Başka hesabın verisi karıştırılmıyor.
        object baska = Paket(new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 11), dun2223, "baska@deneme",
            new[] { Etkinlik("e30", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), gri) });
        Denetle("başka hesabın verisi karıştırılmıyor", ReferenceEquals(Cagir(saglayiciTipi, "TazesiyleBirlestir", ekim, baska), ekim));

        // Hiç eşitlenmemiş (boş) veri bir şey ezmiyor.
        object bos = Paket(new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 11), null, "hesap@deneme", Array.Empty<object>());
        Denetle("hiç eşitlenmemiş veri bir şey ezmiyor", ReferenceEquals(Cagir(saglayiciTipi, "TazesiyleBirlestir", ekim, bos), ekim));

        // Ortak gün yoksa (ör. Ağustos ↔ Ekim) dokunulmuyor.
        object agustos = Paket(new DateOnly(2026, 7, 27), new DateOnly(2026, 9, 6), dun2223, "hesap@deneme",
            new[] { Etkinlik("a1", new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 3), gri) });
        Denetle("ortak gün yoksa dokunulmuyor", ReferenceEquals(Cagir(saglayiciTipi, "TazesiyleBirlestir", ekim, agustos), ekim));
    }

    private static void GecmistenGetir()
    {
        Console.WriteLine();
        Console.WriteLine("3b) Gece ay dönünce (GecmistenGetir) bellekteki Ekim tazelenmiş Eylül'le birleşiyor");

        var (_, saglayici, _) = Kur();
        var dun1148 = new DateTimeOffset(2026, 9, 30, 8, 48, 34, TimeSpan.Zero);
        var dun2223 = new DateTimeOffset(2026, 9, 30, 19, 23, 24, TimeSpan.Zero);

        object ekim = Paket(new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8), dun1148, "hesap@deneme",
            new[] { Etkinlik("e30", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), "#D50000") });
        object eylul = Paket(new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 11), dun2223, "hesap@deneme",
            new[] { Etkinlik("e30", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), "#7c7c7c") });

        Yaz(saglayici, "Data", eylul);
        var gecmis = (IList)Oku(saglayici, "_gecmis")!;
        gecmis.Add(ekim);

        bool bulundu = (bool)Cagir(saglayici, "GecmistenGetir", new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8))!;
        object veri = Oku(saglayici, "Data")!;
        Denetle("Ekim bellekten geldi", bulundu && (DateOnly)Oku(veri, "RangeFrom")! == new DateOnly(2026, 9, 28));
        Denetle("30 Eylül işi gri kaldı (önceden kırmızıya dönüyordu)", Renk(saglayici, "e30") == "#7c7c7c",
            Renk(saglayici, "e30") ?? "yok");
        Denetle("Eylül geçmişe kondu", gecmis.Count == 1 && ReferenceEquals(gecmis[0], eylul));
    }

    // 4 -------------------------------------------------------------------------

    private static void GunlugeYazma(string gecici)
    {
        Console.WriteLine();
        Console.WriteLine("4) 5 dakika boyunca hiç tutmayan eşitleme varsayılan günlüğe yazılıyor");

        Type yoklayiciTipi = Tip("Kalender.Google.GooglePoller");
        object yoklayici = RuntimeHelpers.GetUninitializedObject(yoklayiciTipi);
        string gunlukKlasoru = Path.Combine(gecici, "logs");

        Type gunlukTipi = Tip("Kalender.Core.Log");
        object duzey = gunlukTipi.GetProperty("Level", Hepsi)!.GetValue(null)!;
        Denetle("varsayılan günlük düzeyi yalnız hatalar", duzey.ToString() == "Error", duzey.ToString()!);

        string Gunluk() => string.Join("\n", Directory.Exists(gunlukKlasoru)
            ? Directory.GetFiles(gunlukKlasoru).Select(File.ReadAllText)
            : Array.Empty<string>());

        var t0 = new DateTimeOffset(2026, 10, 1, 6, 16, 22, TimeSpan.Zero);
        string hata = "Google isteği başarısız (401): …";

        Cagir(yoklayici, "SonucuIzle", hata, t0);
        Cagir(yoklayici, "SonucuIzle", hata, t0.AddSeconds(30));
        Cagir(yoklayici, "SonucuIzle", hata, t0.AddMinutes(4.5));
        Denetle("5 dakikadan önce yazılmıyor", !Gunluk().Contains("hiç tutmadı"));

        Cagir(yoklayici, "SonucuIzle", hata, t0.AddMinutes(5));
        string g1 = Gunluk();
        Denetle("5. dakikada bir kez hata olarak yazıldı",
            g1.Contains("[HATA]") && g1.Contains("5 dk boyunca hiç tutmadı") && g1.Contains("401"),
            g1.Split('\n').LastOrDefault(l => l.Contains("tutmadı"))?.Trim() ?? "yok");

        Cagir(yoklayici, "SonucuIzle", hata, t0.AddMinutes(6));
        Cagir(yoklayici, "SonucuIzle", hata, t0.AddMinutes(30));
        Denetle("sonraki başarısızlıklar tekrar yazılmıyor",
            Gunluk().Split("hiç tutmadı").Length - 1 == 1);

        Cagir(yoklayici, "SonucuIzle", null, t0.AddMinutes(35));
        string g2 = Gunluk();
        Denetle("düzelince bu da yazılıyor, süresiyle", g2.Contains("yeniden çalışıyor (35 dk sürdü)"),
            g2.Split('\n').LastOrDefault(l => l.Contains("yeniden"))?.Trim() ?? "yok");

        Cagir(yoklayici, "SonucuIzle", null, t0.AddMinutes(36));
        Cagir(yoklayici, "SonucuIzle", hata, t0.AddMinutes(37));
        Cagir(yoklayici, "SonucuIzle", null, t0.AddMinutes(38));
        Denetle("kısa kesinti yazılmıyor", Gunluk().Split("yeniden çalışıyor").Length - 1 == 1);

        // Yeniden başlatma (uyanış) sayacı sıfırlıyor: uykudan önceki hata ile uyanınca gelen ilk hata birleşmiyor.
        Cagir(yoklayici, "SonucuIzle", hata, t0.AddMinutes(40));
        Yaz(yoklayici, "_ilkHataUtc", null);
        Cagir(yoklayici, "SonucuIzle", hata, t0.AddHours(9));
        Denetle("sayaç sıfırlanınca uyanıştaki ilk hata yazılmıyor", Gunluk().Split("hiç tutmadı").Length - 1 == 1);

        string kaynak = File.ReadAllText(Path.Combine(DepoKoku(), "src", "Kalender.App", "Google", "GooglePoller.cs"));
        int basla = kaynak.IndexOf("public void Start()", StringComparison.Ordinal);
        int sifir = kaynak.IndexOf("_ilkHataUtc = null;", basla, StringComparison.Ordinal);
        int bitis = kaynak.IndexOf("public void Stop()", StringComparison.Ordinal);
        Denetle("Start() sayacı sıfırlıyor", basla > 0 && sifir > basla && sifir < bitis);
    }

    // ------------------------------------------- genel kod incelemesi (2026-10-12)

    /// <summary>Bir bölümü çalıştırır; beklenmeyen hata öbür bölümleri durdurmaz.</summary>
    private static async Task Bolum(Func<Task> bolum)
    {
        try
        {
            await bolum();
        }
        catch (Exception ex)
        {
            _hata++;
            Console.WriteLine("   BEKLENMEYEN HATA: " + (ex.InnerException ?? ex).Message);
        }
    }

    /// <summary>Seçimdeki "hiçbiri" işareti (düzeltmeden önceki kodla sınanırken de aynı değer).</summary>
    private static string Hicbiri
        => Tip("Kalender.Settings.GoogleSettings").GetField("HicbiriSecili", Hepsi)?.GetValue(null) as string ?? "(yok)";

    /// <summary>Yetkinin bağlantı kuşağı; bu özellik yoksa (düzeltmeden önceki kod) -1.</summary>
    private static int Kusak(object yetki) => yetki.GetType().GetProperty("BaglantiKusagi", Hepsi)?.GetValue(yetki) as int? ?? -1;

    /// <summary>Ayarlardaki canlı takvim seçimi listesi.</summary>
    private static List<string> TakvimSecimi
        => (List<string>)Oku(Oku(Oku(_ayarlar, "Current")!, "Google")!, "SelectedCalendarIds")!;

    private static string Klasor(string ad) => (string)Tip("Kalender.Core.Paths").GetProperty(ad, Hepsi)!.GetValue(null)!;

    private static IList Etkinlikler(object saglayici) => (IList)Oku(Oku(saglayici, "Data")!, "Events")!;

    private static object? EtkinlikBul(object saglayici, string id)
        => Etkinlikler(saglayici).Cast<object>().FirstOrDefault(e => (string)Oku(e, "Id")! == id);

    /// <summary>Google'ın tanıdığı geçerli bir anahtarla kurulmuş sağlayıcı.</summary>
    private static (object Yetki, object Saglayici, SahteGoogle Google) Bagli()
    {
        var kurulan = Kur();
        EskiAnahtarKoy(kurulan.Yetki, "GECERLI");
        kurulan.Google.Gecerli.Add("GECERLI");
        return kurulan;
    }

    /// <summary>Ekim 2026'yı gösteren takvim içeriği (tatilsiz).</summary>
    private static object VeriHizmeti(object saglayici)
    {
        object tatiller = Activator.CreateInstance(Tip("Kalender.Calendar.Holidays.HolidayService"))!;
        object veri = Activator.CreateInstance(Tip("Kalender.Calendar.CalendarDataService"), tatiller, saglayici, _ayarlar)!;
        Cagir(veri, "SetViewMonth", new DateOnly(2026, 10, 1));
        return veri;
    }

    /// <summary>Takvim içeriğinde o günün kayıtlarının başlıkları ve saatleri.</summary>
    private static List<(string Baslik, TimeOnly? Saat)> GunKayitlari(object veri, DateOnly gun)
    {
        object? icerik = Cagir(Oku(veri, "Current")!, "For", gun);
        return icerik is null
            ? new List<(string, TimeOnly?)>()
            : ((IEnumerable)Oku(icerik, "Entries")!).Cast<object>()
                .Select(k => ((string)Oku(k, "Title")!, (TimeOnly?)Oku(k, "Start"))).ToList();
    }

    /// <summary>Yerel saatte bir an, Google'ın yazdığı biçimde (ör. 2026-10-10T22:00:00+03:00).</summary>
    private static string Yerel(DateTime an)
        => new DateTimeOffset(an, TimeZoneInfo.Local.GetUtcOffset(an)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>Yerel saatteki anın UTC yazılışı (ör. 2026-10-16T21:00:00Z).</summary>
    private static string Utc(DateTime an)
        => new DateTimeOffset(an, TimeZoneInfo.Local.GetUtcOffset(an)).UtcDateTime
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Saatli(string id, string bas, string son)
        => $$$"""{"id":"{{{id}}}","summary":"{{{id}}}","status":"confirmed","start":{"dateTime":"{{{bas}}}"},"end":{"dateTime":"{{{son}}}"}}""";

    // 9 -------------------------------------------------------------------------

    private static async Task Madde9()
    {
        Console.WriteLine();
        Console.WriteLine("9) Tam gece yarısında biten saatli etkinlik ertesi güne düşmüyor");

        var (_, saglayici, google) = Bagli();
        google.EtkinlikYaniti = "{\"items\":[" + string.Join(",",
            Saatli("yemek", Yerel(new DateTime(2026, 10, 10, 22, 0, 0)), Yerel(new DateTime(2026, 10, 11, 0, 0, 0))),
            Saatli("tasan", Yerel(new DateTime(2026, 10, 10, 22, 0, 0)), Yerel(new DateTime(2026, 10, 11, 1, 0, 0))),
            Saatli("tamgun", Yerel(new DateTime(2026, 10, 12, 0, 0, 0)), Yerel(new DateTime(2026, 10, 13, 0, 0, 0))),
            Saatli("anlik", Yerel(new DateTime(2026, 10, 14, 0, 0, 0)), Yerel(new DateTime(2026, 10, 14, 0, 0, 0))),
            Saatli("utc", Utc(new DateTime(2026, 10, 16, 22, 0, 0)), Utc(new DateTime(2026, 10, 17, 0, 0, 0)))) + "]}";

        bool sonuc = await Esitle(saglayici);
        Denetle("eşitleme tuttu", sonuc, (string?)Oku(saglayici, "SonHata") ?? "hata yok");

        string Gunler(string id)
        {
            object? e = EtkinlikBul(saglayici, id);
            return e is null ? "yok" : $"{(DateOnly)Oku(e, "StartDate")!:dd.MM}-{(DateOnly)Oku(e, "EndDate")!:dd.MM}";
        }

        Denetle("22:00–00:00 etkinlik yalnız 10 Ekim'de", Gunler("yemek") == "10.10-10.10", Gunler("yemek"));
        Denetle("gece yarısını geçen (01:00'de biten) etkinlik ertesi güne de düşüyor", Gunler("tasan") == "10.10-11.10", Gunler("tasan"));
        Denetle("00:00–00:00 (24 saat) etkinlik tek gün", Gunler("tamgun") == "12.10-12.10", Gunler("tamgun"));
        Denetle("süresiz 00:00 etkinliği kendi gününde", Gunler("anlik") == "14.10-14.10", Gunler("anlik"));
        Denetle("UTC yazılmış, yerel gece yarısında biten etkinlik de tek gün", Gunler("utc") == "16.10-16.10", Gunler("utc"));

        object veri = VeriHizmeti(saglayici);
        List<(string Baslik, TimeOnly? Saat)> on = GunKayitlari(veri, new DateOnly(2026, 10, 10));
        List<(string Baslik, TimeOnly? Saat)> onbir = GunKayitlari(veri, new DateOnly(2026, 10, 11));
        Denetle("takvimde 10 Ekim'de saatiyle görünüyor", on.Contains(("yemek", new TimeOnly(22, 0))));
        Denetle("11 Ekim'de \"Tüm gün\" kaydı ve noktacığı yok", !onbir.Any(k => k.Baslik == "yemek"),
            string.Join(", ", onbir.Select(k => k.Baslik)));
        Denetle("01:00'de biten etkinlik 11 Ekim'de duruyor", onbir.Any(k => k.Baslik == "tasan"));
        Cagir(saglayici, "ClearCache");
    }

    // 10 ------------------------------------------------------------------------

    private static async Task Madde10()
    {
        Console.WriteLine();
        Console.WriteLine("10) Bütün takvimler kapatılınca hiçbir etkinlik alınmıyor");

        var (_, saglayici, google) = Bagli();
        try
        {
            TakvimSecimi.Clear();
            TakvimSecimi.Add(Hicbiri);
            bool sonuc = await Esitle(saglayici);

            int liste;
            int etiket;
            lock (google.Adresler)
            {
                liste = google.Adresler.Count(a => a.Contains("/events?timeMin=", StringComparison.Ordinal));
                etiket = google.Adresler.Count(a => a.Contains("labelProperties", StringComparison.Ordinal));
            }

            Denetle("eşitleme tuttu, hiç etkinlik yok", sonuc && Etkinlikler(saglayici).Count == 0,
                $"{Etkinlikler(saglayici).Count} etkinlik");
            Denetle("takvimin etkinlikleri ve etiketleri hiç istenmedi", liste == 0 && etiket == 0,
                $"{liste} etkinlik, {etiket} etiket isteği");
            Denetle("takvim listesi yine alındı (ayarlarda gösterilsin)",
                ((IList)Oku(Oku(saglayici, "Data")!, "Calendars")!).Count == 1);

            TakvimSecimi.Clear();
            bool hepsi = await Esitle(saglayici);
            Denetle("boş seçim (eski ayar dosyası) yine \"hepsi\" demek", hepsi && Etkinlikler(saglayici).Count == 1);
        }
        finally
        {
            TakvimSecimi.Clear();
            Cagir(saglayici, "ClearCache");
        }
    }

    // 11 ------------------------------------------------------------------------

    private static async Task Madde11()
    {
        Console.WriteLine();
        Console.WriteLine("11) Seçim eşitlemenin başında kopyalanıyor; zorlanmış istek süren zorlanmış işe katılmıyor");

        try
        {
            // Liste eşitleme sürerken değişiyor, yeni istek gelmiyor: eşitleme
            // başladığı andaki seçimle bitiyor (önceden son süzgeç canlı listeye bakıyordu).
            var (_, saglayici, google) = Bagli();
            google.Gecikme = TimeSpan.FromMilliseconds(150);
            TakvimSecimi.Clear();
            Task<bool> gorev = Esitle(saglayici);
            await Task.Delay(250);
            TakvimSecimi.Add(Hicbiri);
            bool sonuc = await gorev;
            Denetle("eşitleme başladığı andaki seçimle bitti", sonuc && Etkinlikler(saglayici).Count == 1,
                $"{Etkinlikler(saglayici).Count} etkinlik");
            TakvimSecimi.Clear();
            Cagir(saglayici, "ClearCache");

            // Yoklayıcı üzerinden, ayarlar penceresindeki gibi: her anahtar değişikliği zorlanmış yenileme ister.
            var (_, saglayici2, google2) = Bagli();
            google2.Gecikme = TimeSpan.FromMilliseconds(150);
            object veri = VeriHizmeti(saglayici2);
            object yoklayici = Activator.CreateInstance(Tip("Kalender.Google.GooglePoller"), saglayici2, veri, _ayarlar)!;
            Task Yenile(bool zorla) => (Task)Cagir(yoklayici, "RefreshAsync", zorla)!;

            Task ilk = Yenile(true);
            await Task.Delay(250);
            TakvimSecimi.Add(Hicbiri);
            Task ikinci = Yenile(true);
            Task ucuncu = Yenile(false);
            Denetle("zorlanmış istek süren zorlanmış işe katılmadı, yeni iş başladı", !ReferenceEquals(ilk, ikinci));
            Denetle("zorlanmamış istek süren işe katıldı", ReferenceEquals(ikinci, ucuncu));
            await Task.WhenAll(ilk, ikinci);
            Denetle("son seçim uygulandı (hiçbiri): etkinlik yok", Etkinlikler(saglayici2).Count == 0,
                $"{Etkinlikler(saglayici2).Count} etkinlik");

            // Yanlışlıkla kapatılıp 1–2 saniye içinde yeniden açılan takvim.
            TakvimSecimi.Clear();
            TakvimSecimi.Add(Hicbiri);
            Task kapat = Yenile(true);
            await Task.Delay(250);
            TakvimSecimi.Clear();
            Task ac = Yenile(true);
            await Task.WhenAll(kapat, ac);
            Denetle("kapatılıp hemen açılan takvimin etkinlikleri geldi", Etkinlikler(saglayici2).Count == 1,
                $"{Etkinlikler(saglayici2).Count} etkinlik");
            Denetle("ve takvimde görünüyor", GunKayitlari(veri, new DateOnly(2026, 9, 30)).Any(k => k.Baslik == "Dava"));
            ((IDisposable)yoklayici).Dispose();
            Cagir(saglayici2, "ClearCache");
        }
        finally
        {
            TakvimSecimi.Clear();
        }
    }

    // 14 ------------------------------------------------------------------------

    private static async Task Madde14()
    {
        Console.WriteLine();
        Console.WriteLine("14) Bağlantı keserken süren eşitleme yerel kopyayı geri yazmıyor");

        string onbellek = Path.Combine(Klasor("CacheFolder"), "google.json");
        string yetkiDosyasi = Path.Combine(Klasor("RootFolder"), "google-token.dat");

        var (yetki, saglayici, google) = Bagli();
        Cagir(saglayici, "ClearCache");
        google.Gecikme = TimeSpan.FromMilliseconds(150);
        int kusak = Kusak(yetki);
        Task<bool> gorev = Esitle(saglayici);
        await Task.Delay(400);

        // "Bağlantıyı kes": AppHost önce yetkiyi siler, sonra yoklamayı durdurup kopyayı siler.
        Cagir(yetki, "SignOut");
        Cagir(saglayici, "ClearCache");
        bool sonuc = await gorev;
        Denetle("bağlantı kesilince kuşak arttı", kusak >= 0 && Kusak(yetki) == kusak + 1);
        Denetle("süren eşitlemenin sonucu atıldı", !sonuc);
        Denetle("ekrana yazılmadı (bellekteki veri boş)", Etkinlikler(saglayici).Count == 0
            && ((IList)Oku(Oku(saglayici, "Data")!, "Calendars")!).Count == 0, $"{Etkinlikler(saglayici).Count} etkinlik");
        Denetle("yerel kopya silinmiş kaldı", !File.Exists(onbellek));

        // Erişim anahtarı yenilenirken bağlantı kesiliyor.
        foreach (bool yenilemeAnahtari in new[] { false, true })
        {
            var (yetki2, _, google2) = Kur();
            google2.TokenGecikmesi = TimeSpan.FromMilliseconds(300);
            google2.YenilemeAnahtariVer = yenilemeAnahtari;
            var anahtar = (Task<string?>)Cagir(yetki2, "GetAccessTokenAsync", CancellationToken.None)!;
            await Task.Delay(100);
            Cagir(yetki2, "SignOut");
            string? alinan = await anahtar;
            string ek = yenilemeAnahtari ? " (Google yeni yenileme anahtarı da verdi)" : string.Empty;
            Denetle("yenilenen anahtar kesilen bağlantıya verilmedi" + ek,
                alinan is null && Oku(yetki2, "_accessToken") is null, alinan ?? (string?)Oku(yetki2, "_accessToken") ?? "yok");
            Denetle("yetki dosyası yeniden yazılmadı" + ek, !File.Exists(yetkiDosyasi));
        }

        // Açılış: bağlıyken kopya yükleniyor, bağlı değilken diskte kalmış kopya yüklenmiyor.
        var (_, saglayici3, _) = Bagli();
        bool yazildi = await Esitle(saglayici3);
        Denetle("bağlıyken yerel kopya yazıldı", yazildi && File.Exists(onbellek));

        var (_, saglayici4, _) = Kur();
        Cagir(saglayici4, "LoadCache");
        Denetle("bağlıyken açılışta kopya yükleniyor", Etkinlikler(saglayici4).Count == 1);

        var (yetki5, saglayici5, _) = Kur();
        Yaz(yetki5, "_token", null);
        Cagir(saglayici5, "LoadCache");
        Denetle("bağlı değilken diskte kalmış kopya yüklenmiyor", Etkinlikler(saglayici5).Count == 0);
        Cagir(saglayici3, "ClearCache");
    }

    // 15 ------------------------------------------------------------------------

    /// <summary>
    /// Tarayıcının yerine geçer: onay adresindeki dönüş adresine ve durum
    /// değerine bakıp yerel dinleyiciye "onaylandı" yanıtını gönderir.
    /// </summary>
    private static async Task OnayVer(string adres)
    {
        var sorgu = System.Web.HttpUtility.ParseQueryString(new Uri(adres).Query);
        using var istemci = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            await istemci.GetStringAsync($"{sorgu["redirect_uri"]}/?code=deneme-kodu&state={Uri.EscapeDataString(sorgu["state"]!)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("   (onay isteği gönderilemedi: " + ex.Message + ")");
        }
    }

    private static async Task<(bool Basari, string Ileti)> Baglan(object yetki, CancellationToken iptal = default)
    {
        var gorev = (Task)Cagir(yetki, "SignInAsync", iptal)!;
        await gorev;
        object sonuc = gorev.GetType().GetProperty("Result")!.GetValue(gorev)!;
        return ((bool)Oku(sonuc, "Success")!, (string)Oku(sonuc, "Message")!);
    }

    private static async Task Madde15()
    {
        Console.WriteLine();
        Console.WriteLine("15) Onaydan sonraki zaman aşımı \"onay gelmedi\" diye bildirilmiyor (tarayıcı açılmıyor)");

        // Tarayıcıyı açan kanca yoksa bağlanma hiç denenmez: gerçek tarayıcı açılırdı.
        PropertyInfo? acici = Tip("Kalender.Google.GoogleAuthenticator").GetProperty("TarayiciAcici", Hepsi);
        Denetle("tarayıcıyı açan adım sınamada değiştirilebiliyor", acici is not null);
        if (acici is null)
        {
            return;
        }

        object asil = acici.GetValue(null)!;
        int acilan = 0;
        acici.SetValue(null, new Action<string>(adres =>
        {
            Interlocked.Increment(ref acilan);
            _ = Task.Run(() => OnayVer(adres));
        }));

        try
        {
            // Google'ın anahtar adresi 3 sn'de yanıt veriyor, istemcinin sabrı 1 sn.
            var (yetki, _, google) = Kur();
            Yaz(yetki, "_http", new HttpClient(google) { Timeout = TimeSpan.FromSeconds(1) });
            google.TokenGecikmesi = TimeSpan.FromSeconds(3);
            var (basari, ileti) = await Baglan(yetki);
            Denetle("onay geldi, alışveriş zaman aşımına düştü: \"Google yanıt vermedi\" deniyor",
                !basari && ileti.Contains("Google yanıt vermedi", StringComparison.Ordinal)
                && !ileti.Contains("onay gelmedi", StringComparison.Ordinal), ileti);

            // Kullanıcı alışveriş sürerken vazgeçiyor.
            var (yetki2, _, google2) = Kur();
            google2.TokenGecikmesi = TimeSpan.FromSeconds(3);
            using var vazgec = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            var (basari2, ileti2) = await Baglan(yetki2, vazgec.Token);
            Denetle("vazgeçince \"vazgeçildi\" deniyor", !basari2 && ileti2 == "Bağlanmaktan vazgeçildi.", ileti2);

            // Olağan bağlanma: kuşak artıyor (eski bağlantının süren eşitlemesi yazılmasın).
            var (yetki3, _, google3) = Kur();
            google3.YenilemeAnahtariVer = true;
            int kusak = Kusak(yetki3);
            var (basari3, ileti3) = await Baglan(yetki3);
            Denetle("olağan bağlanma tutuyor, kuşak artıyor", basari3 && Kusak(yetki3) == kusak + 1, ileti3);
            Cagir(yetki3, "SignOut");

            Denetle("gerçek tarayıcı açılmadı, onayı sınama verdi", acilan == 3, $"{acilan} kez");
        }
        finally
        {
            acici.SetValue(null, asil);
        }
    }

    // 16 ------------------------------------------------------------------------

    private static void Madde16()
    {
        Console.WriteLine();
        Console.WriteLine("16) \"Takvimde aç\" bağlı hesabın takvimini açıyor");

        Type link = Tip("Kalender.Google.GoogleLinks");
        string gun = (string)Cagir(link, "Day", new DateOnly(2026, 10, 10))!;
        string ay = (string)Cagir(link, "Month", new DateOnly(2026, 10, 1))!;
        string Hesapli(string adres, string? hesap) => (string)Cagir(link, "HesapIle", adres, hesap)!;

        Denetle("gün adresine hesap ekleniyor, yol bozulmuyor",
            Hesapli(gun, "ad.soyad+is@gmail.com")
                == "https://calendar.google.com/calendar/u/0/r/day/2026/10/10?authuser=ad.soyad%2Bis%40gmail.com",
            Hesapli(gun, "ad.soyad+is@gmail.com"));
        Denetle("ay adresine de", Hesapli(ay, "hesap@deneme") == ay + "?authuser=hesap%40deneme");
        Denetle("hesap bilinmiyorsa adres eskisi gibi", Hesapli(gun, null) == gun && Hesapli(gun, " ") == gun);

        string kaynak = File.ReadAllText(Path.Combine(DepoKoku(), "src", "Kalender.App", "Core", "AppHost.cs"));
        int menu = kaynak.IndexOf("case MenuGoToDay:", StringComparison.Ordinal);
        int hesapIle = kaynak.IndexOf("GoogleLinks.HesapIle(", Math.Max(0, menu), StringComparison.Ordinal);
        int eposta = kaynak.IndexOf("GoogleAccountEmail", Math.Max(0, hesapIle), StringComparison.Ordinal);
        int son = kaynak.IndexOf("break;", Math.Max(0, menu), StringComparison.Ordinal);
        Denetle("sağ tık menüsü adresi bağlı hesabın e-postasıyla açıyor",
            menu > 0 && hesapIle > menu && eposta > hesapIle && eposta < son);
    }

    // 17 ------------------------------------------------------------------------

    private static async Task Madde17()
    {
        Console.WriteLine();
        Console.WriteLine("17) Ay belleğinde tarihi değişen görev iki kez kalmıyor, eşitleme düşmüyor");

        var dun1148 = new DateTimeOffset(2026, 9, 30, 8, 48, 34, TimeSpan.Zero);
        var dun2223 = new DateTimeOffset(2026, 9, 30, 19, 23, 24, TimeSpan.Zero);

        // Kasım (26 Ekim–6 Aralık) önce gösterildi: "itiraz" 20 Kasım'da.
        object kasim = Paket(new DateOnly(2026, 10, 26), new DateOnly(2026, 12, 6), dun1148, "hesap@deneme",
            Array.Empty<object>(),
            new[] { Gorev("itiraz", new DateOnly(2026, 11, 20), false), Gorev("kasim5", new DateOnly(2026, 12, 5), false) });

        // Ekim sonra tazelendi: görev telefondan 2 Kasım'a (Ekim ızgarasında) alınmış.
        object ekim = Paket(new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8), dun2223, "hesap@deneme",
            Array.Empty<object>(), new[] { Gorev("itiraz", new DateOnly(2026, 11, 2), false) });

        object sonuc = Cagir(Tip("Kalender.Google.GoogleCalendarProvider"), "TazesiyleBirlestir", kasim, ekim)!;
        List<object> gorevler = ((IList)Oku(sonuc, "Tasks")!).Cast<object>().ToList();
        List<DateOnly> itiraz = gorevler.Where(g => (string)Oku(g, "Id")! == "itiraz").Select(g => (DateOnly)Oku(g, "Due")!).ToList();
        Denetle("taşınan görev bir kez, yeni tarihinde", itiraz.Count == 1 && itiraz[0] == new DateOnly(2026, 11, 2),
            string.Join(", ", itiraz.Select(d => d.ToString("dd.MM", CultureInfo.InvariantCulture))));
        Denetle("yalnız Kasım'a ait görev korunuyor", gorevler.Any(g => (string)Oku(g, "Id")! == "kasim5"));

        // Elde aynı kaydın iki kopyası varken artımlı eşitleme düşmüyor.
        var (_, saglayici, _) = Bagli();
        object cift = Paket(new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8), DateTimeOffset.UtcNow.AddMinutes(-1),
            "takvim@deneme",
            new[]
            {
                Etkinlik("e1", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), "#7c7c7c"),
                Etkinlik("e1", new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2), "#7c7c7c"),
            },
            new[] { Gorev("g1", new DateOnly(2026, 10, 1), false), Gorev("g1", new DateOnly(2026, 10, 3), false) });
        Yaz(saglayici, "Data", cift);
        bool tuttu = await Esitle(saglayici, tam: false);
        Denetle("aynı anahtarlı iki kopya artımlı eşitlemeyi düşürmüyor", tuttu && Oku(saglayici, "SonHata") is null,
            (string?)Oku(saglayici, "SonHata") ?? "hata yok");
        Denetle("etkinliğin tek kopyası kaldı", Etkinlikler(saglayici).Count == 1, $"{Etkinlikler(saglayici).Count} kayıt");
        Cagir(saglayici, "ClearCache");
    }

    // 38 ------------------------------------------------------------------------

    private static async Task Madde38()
    {
        Console.WriteLine();
        Console.WriteLine("38) Artımlı yoklama son yoklamadan 2 dakika geriden soruyor");

        var (_, saglayici, google) = Bagli();
        DateTimeOffset son = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60);
        Yaz(saglayici, "Data", Paket(new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8), son, "takvim@deneme",
            new[] { Etkinlik("e1", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1), "#7c7c7c") }));

        bool tuttu = await Esitle(saglayici, tam: false);
        List<string> sorulan;
        lock (google.Adresler)
        {
            sorulan = google.Adresler.Where(a => a.Contains("updatedMin=", StringComparison.Ordinal))
                .Select(Uri.UnescapeDataString).ToList();
        }

        string beklenen = "updatedMin=" + son.AddMinutes(-2).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        Denetle("artımlı yoklama yapıldı", tuttu && sorulan.Count > 0, $"{sorulan.Count} soru");
        Denetle("sınır anı son yoklamadan 2 dakika önce", sorulan.Count > 0 && sorulan.All(a => a.Contains(beklenen, StringComparison.Ordinal)),
            sorulan.FirstOrDefault() is string ilk ? ilk[ilk.IndexOf("updatedMin=", StringComparison.Ordinal)..].Split('&')[0] : "yok");
        Denetle("sonraki yoklamanın sınırı yine bu eşitlemenin başladığı an (pay yalnız soruda)",
            (DateTimeOffset?)Oku(Oku(saglayici, "Data")!, "LastSyncUtc") > son.AddSeconds(30));
        Cagir(saglayici, "ClearCache");
    }

    // 34 ------------------------------------------------------------------------

    /// <summary>
    /// Süreç saat dilimini önbelleğe almış, Windows'ta dilim sonradan değişmiş
    /// gibi yapar: .NET'in dilim önbelleğine (TimeZoneInfo.s_cachedData) verilen
    /// farkla sahte bir dilim konur. Windows'un ayarına dokunulmaz;
    /// TimeZoneInfo.ClearCachedData önbelleği atınca gerçek dilim geri gelir.
    /// </summary>
    private static void SahteDilimKoy(TimeSpan fark)
    {
        FieldInfo alan = typeof(TimeZoneInfo).GetField("s_cachedData", Hepsi)!;
        object onbellek = Activator.CreateInstance(alan.FieldType, nonPublic: true)!;
        alan.FieldType.GetField("_localTimeZone", Hepsi)!.SetValue(onbellek,
            TimeZoneInfo.CreateCustomTimeZone("Sahte Dilim", fark, "Sahte Dilim", "Sahte Dilim"));

        // DateTime.Now, Windows'ta dilimden ayrı olarak bu yılın farkını da önbelleğe alıyor.
        Type yilinFarki = typeof(TimeZoneInfo).GetNestedType("OffsetAndRule", BindingFlags.NonPublic)!;
        alan.FieldType.GetField("_oneYearLocalFromUtc", Hepsi)!.SetValue(onbellek,
            Activator.CreateInstance(yilinFarki, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object?[] { DateTime.UtcNow.Year, fark, null }, null));
        alan.SetValue(null, onbellek);
    }

    private static async Task Madde34()
    {
        Console.WriteLine();
        Console.WriteLine("34) Saat dilimi değişince gün kutusundaki saatler yeni dilime göre");

        TimeZoneInfo.ClearCachedData();
        var an = new DateTime(2026, 10, 14, 7, 0, 0, DateTimeKind.Utc);
        TimeSpan gercekFark = TimeZoneInfo.Local.GetUtcOffset(an);
        TimeSpan sahteFark = gercekFark == TimeSpan.FromHours(9) ? TimeSpan.FromHours(-5) : TimeSpan.FromHours(9);
        TimeOnly Saat(TimeSpan fark) => TimeOnly.FromDateTime(an + fark);

        var (_, saglayici, google) = Bagli();
        google.EtkinlikYaniti = "{\"items\":[" + Saatli("durusma", "2026-10-14T07:00:00Z", "2026-10-14T08:00:00Z") + "]}";
        TimeOnly? Baslangic() => EtkinlikBul(saglayici, "durusma") is object e ? (TimeOnly?)Oku(e, "StartTime") : null;

        try
        {
            // Süreç eski dilimdeyken alınan etkinlik o dilimin saatiyle saklanıyor.
            SahteDilimKoy(sahteFark);
            bool ilk = await Esitle(saglayici);
            Denetle("sınama düzeni: etkinlik eski dilimin saatiyle saklandı", ilk && Baslangic() == Saat(sahteFark),
                $"{Baslangic()} (eski dilimde {Saat(sahteFark)})");

            // Ay belleğinde eski dilimle çevrilmiş başka bir ay da var.
            ((IList)Oku(saglayici, "_gecmis")!).Add(Paket(new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 11),
                DateTimeOffset.UtcNow, "takvim@deneme", Array.Empty<object>()));

            // Windows'ta dilim değişti: gün izleyicisi .NET'in önbelleğini atıyor,
            // uygulama sağlayıcıya haber veriyor.
            TimeZoneInfo.ClearCachedData();
            Cagir(saglayici, "SaatDilimiDegisti");
            Denetle("ay belleğindeki eski dilimli aylar atılıyor", ((IList)Oku(saglayici, "_gecmis")!).Count == 0);

            int once;
            lock (google.Adresler)
            {
                once = google.Adresler.Count;
            }

            // Zorlanmamış bir yoklama da (ör. zorlanmış yenileme ağ yokken düştüyse) tam yapılıyor.
            bool ikinci = await Esitle(saglayici, tam: false);
            List<string> sorulan;
            lock (google.Adresler)
            {
                sorulan = google.Adresler.Skip(once).ToList();
            }

            Denetle("sıradaki eşitleme zorlanmasa da tam (etkinlikler baştan alınıyor)",
                ikinci && sorulan.Any(a => a.Contains("/events", StringComparison.Ordinal))
                && !sorulan.Any(a => a.Contains("updatedMin=", StringComparison.Ordinal)),
                $"{sorulan.Count} istek");
            Denetle("gün kutusundaki saat yeni dilime göre", Baslangic() == Saat(gercekFark),
                $"{Baslangic()} (beklenen {Saat(gercekFark)})");
        }
        finally
        {
            TimeZoneInfo.ClearCachedData();
            Cagir(saglayici, "ClearCache");
        }
    }
}
