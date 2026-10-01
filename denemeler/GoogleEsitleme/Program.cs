using System.Collections;
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

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken iptal)
        {
            if (Gecikme > TimeSpan.Zero)
            {
                await Task.Delay(Gecikme, iptal);
            }

            string url = istek.RequestUri!.ToString();

            if (url.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref TokenIstegi);
                string yeni = "YENI" + Interlocked.Increment(ref _sira);
                if (!HepReddet)
                {
                    lock (Gecerli)
                    {
                        Gecerli.Add(yeni);
                    }
                }

                return Json(200, $$"""{"access_token":"{{yeni}}","expires_in":3599}""");
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
}
