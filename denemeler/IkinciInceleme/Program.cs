using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

// Kalender'de ikinci genel kod incelemesinin (2026-10-15) düzeltmelerinin
// sınaması (yansımayla). Madde numaraları kalender.md'deki listeden:
//   1. Ramazan ve Kurban bayramları, tablonun kapsadığı yıllarda (2026–2035)
//      Diyanet'in tarihleriyle: 2027–2029 Ramazan Bayramı ve arifeleri doğru
//      günde, Google'ın fazladan günü tatil değil. Akıştaki bayram kayıtları
//      (bayrama eklenen idari izin günleri dâhil) atılır; sabit tatiller hiç
//      atılmaz; akışın kapsamadığı yıllara bayram eklenmez. 1.13'ün ön belleği
//      yeniden indirilmeden düzelir; yeni dosyanın biçimi aynı, 1.13 okuyabilir.
//  20. Tablonun dışındaki yıllarda İngilizce ya da yanlış yazılmış bayram
//      adları Türkçe ("Kurban Bayrami Day 2" → "Kurban Bayramı 2. gün",
//      "Sacrifice Feast Holiday" → "Kurban Bayramı"); "(kesin değil)" eki kalır.
//  21. Aynı güne düşen iki tatil de saklanır ve gün kutusunda ayrı satırlarda
//      görünür (tam gün kırmızı, arife olağan renkte); gün, tam gün bir tatil
//      varsa kırmızı.
//
// Google'ın tatil akışı yerine bu programdaki sahtesi kullanılır, ağa çıkılmaz.
// Uygulamanın klasörü (Paths.RootFolder) %TEMP% altında geçici bir klasöre
// çevrilir: kurulu Kalender'in ayarlarına ve tatil ön belleğine dokunulmaz
// (sonda denetlenir). Gün kutusu penceresi kurulur ama gösterilmez. Kurucu ve
// kurulu uygulama başlatılmaz. Geçici klasör sonunda silinir.
// Çıkış kodu 0 = hepsi geçti.
// Kullanım: dotnet run -c Release
internal static class Program
{
    private const BindingFlags Hepsi =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static int _hata;
    private static int _gecen;

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

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

    // ------------------------------------------------------ yansıma yardımcıları

    private static object? Oku(object o, string ad) => o.GetType().GetProperty(ad, Hepsi)!.GetValue(o);

    private static object AlanOku(object o, string ad) => o.GetType().GetField(ad, Hepsi)!.GetValue(o)!;

    private static void AlanAta(object o, string ad, object? deger) => o.GetType().GetField(ad, Hepsi)!.SetValue(o, deger);

    private static object? Cagir(object o, string ad, params object?[] a) => o.GetType().GetMethod(ad, Hepsi)!.Invoke(o, a);

    private static object? Statik(Type t, string ad, params object?[] a) => t.GetMethod(ad, Hepsi)!.Invoke(null, a);

    private static string? DosyaOku(string yol) => File.Exists(yol) ? Convert.ToHexString(File.ReadAllBytes(yol)) : null;

    /// <summary>Bir bölümü çalıştırır; beklenmeyen hata öbür bölümleri durdurmaz.</summary>
    private static void Bolum(Action bolum)
    {
        try
        {
            bolum();
        }
        catch (Exception ex)
        {
            _hata++;
            Console.WriteLine("   BEKLENMEYEN HATA: " + ex);
        }
    }

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        PropertyInfo kok = Tip("Kalender.Core.Paths").GetProperty("RootFolder", Hepsi)!;
        string gercekKok = (string)kok.GetValue(null)!;
        string ayarDosyasi = Path.Combine(gercekKok, "ayarlar.json");
        string tatilDosyasi = Path.Combine(gercekKok, "cache", "tatiller.json");
        string? ayarOnce = DosyaOku(ayarDosyasi);
        string? tatilOnce = DosyaOku(tatilDosyasi);

        string gecici = Path.Combine(Path.GetTempPath(), "Kalender İkinci İnceleme ğüşıöç " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gecici);
        kok.SetValue(null, gecici);

        try
        {
            Bolum(Madde1);
            Bolum(Madde20);
            Bolum(Madde21);
        }
        finally
        {
            kok.SetValue(null, gercekKok);
            try
            {
                Directory.Delete(gecici, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   (deneme klasörü silinemedi: " + ex.Message + ")");
            }
        }

        Denetle("kurulu Kalender'in ayar dosyasına ve tatil ön belleğine dokunulmadı",
            DosyaOku(ayarDosyasi) == ayarOnce && DosyaOku(tatilDosyasi) == tatilOnce);

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    // ------------------------------------------------- tatiller (1, 20, 21. maddeler)

    /// <summary>
    /// Google'ın tatil akışının sahtesi: gövdeyi ya da "değişmedi" (304)
    /// yanıtını bu program verir, ağa çıkılmaz. İsteklerin koşul başlıkları
    /// kaydedilir (KodIncelemesi'nin 18. maddesindeki gibi).
    /// </summary>
    private sealed class SahteTatilAkisi : HttpMessageHandler
    {
        public string Govde = string.Empty;
        public readonly List<string> Kosullar = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken iptal)
        {
            Kosullar.Add(string.Join(";", istek.Headers
                .Where(b => b.Key is "If-None-Match" or "If-Modified-Since")
                .Select(b => b.Key + "=" + string.Join(",", b.Value))));

            var yanit = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Govde, Encoding.UTF8, "text/calendar"),
            };
            yanit.Headers.TryAddWithoutValidation("ETag", "\"yeni\"");
            return Task.FromResult(yanit);
        }
    }

    /// <summary>1.13'ün tatil ön belleğinin biçimi (yazmak ve yeni dosyayı 1.13 gibi okumak için).</summary>
    private sealed class EskiOnbellek
    {
        public DateTimeOffset FetchedUtc { get; set; }

        public string? ETag { get; set; }

        public string? LastModified { get; set; }

        public bool FullFeed { get; set; }

        public List<EskiTatil> Days { get; set; } = new();
    }

    private sealed class EskiTatil
    {
        public DateOnly Date { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public bool DateUncertain { get; set; }
    }

    // Açıklamalar Google'ın akışındaki gibi (ICS kaçışlarıyla).
    private const string Tam = "Resmi tatil";
    private const string Yarim = @"Resmi tatil\nBu\, yarım günlük bir tatildir.";
    private const string TamBelirsiz = @"Resmi tatil\nTarih\, geçicidir ve değişebilir.";
    private const string YarimBelirsiz = @"Resmi tatil\nBu\, yarım günlük bir tatildir.\nTarih\, geçicidir ve değişebilir.";

    /// <summary>Tüm gün kayıtlardan bir ICS metni (bitiş günü dâhil değil).</summary>
    private static string TatilAkisi(params (string Bas, string Son, string Ad, string Aciklama)[] kayitlar)
    {
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n");
        foreach ((string bas, string son, string ad, string aciklama) in kayitlar)
        {
            sb.Append("BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:").Append(bas)
                .Append("\r\nDTEND;VALUE=DATE:").Append(son)
                .Append("\r\nSUMMARY:").Append(ad)
                .Append("\r\nDESCRIPTION:").Append(aciklama)
                .Append("\r\nEND:VEVENT\r\n");
        }

        return sb.Append("END:VCALENDAR\r\n").ToString();
    }

    /// <summary>
    /// Google'ın Türkiye akışının 2026-10'daki kayıtlarından bir seçki, adları
    /// ve tarihleriyle: 2027–2029 Ramazan Bayramı bir gün geç ve "(kesin
    /// değil)" ekli, Kurban Bayramı'nın 2.–4. günleri "Kurban Bayrami Day N",
    /// aynı güne iki tatil düşen günler (23 Nisan 2023, 19 Mayıs 2027, 23 Nisan
    /// 2029). 2036 tablonun dışında bir yıl (uydurma kayıt).
    /// </summary>
    private static string GoogleAkisi() => TatilAkisi(
        // 2023 (tablonun dışında)
        ("20230420", "20230421", "Ramazan Bayramı Arifesi (yarım gün)", Yarim),
        ("20230421", "20230424", "Ramazan Bayramı", Tam),
        ("20230423", "20230424", "Ulusal Egemenlik ve Çocuk Bayramı", Tam),
        ("20230626", "20230627", "Kurban Bayrami", Tam),
        ("20230627", "20230628", "Kurban Bayramı Arifesi (yarım gün)", Yarim),
        ("20230628", "20230629", "Kurban Bayramı", Tam),
        ("20230629", "20230630", "Kurban Bayrami Day 2", Tam),
        ("20230630", "20230701", "Kurban Bayrami Day 3", Tam),
        ("20230701", "20230702", "Kurban Bayrami Day 4", Tam),
        // 2024–2025 (tablonun dışında)
        ("20240620", "20240621", "Sacrifice Feast Holiday", Tam),
        ("20240621", "20240622", "Eid al Adha Public Sector Holiday", Tam),
        ("20250607", "20250608", "Kurban Bayrami Day 2", Tam),
        // 2026
        ("20260525", "20260526", "Sacrifice Feast Holiday", Tam),
        ("20260526", "20260527", "Kurban Bayramı Arifesi (yarım gün)", Yarim),
        ("20260527", "20260528", "Kurban Bayramı", Tam),
        ("20260528", "20260529", "Kurban Bayrami Day 2", Tam),
        ("20261028", "20261029", "Cumhuriyet Bayramı Arifesi (yarım gün)", Yarim),
        ("20261029", "20261030", "Cumhuriyet Bayramı", Tam),
        // 2027
        ("20270101", "20270102", "Yılbaşı", Tam),
        ("20270309", "20270310", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20270310", "20270313", "Ramazan Bayramı (kesin değil)", TamBelirsiz),
        ("20270423", "20270424", "Ulusal Egemenlik ve Çocuk Bayramı", Tam),
        ("20270501", "20270502", "Emek ve Dayanışma Günü", Tam),
        ("20270509", "20270510", "Anneler Günü", @"Kutlama\nKutlama günlerini gizlemek için Google Takvim Ayarları"),
        ("20270515", "20270516", "Kurban Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20270516", "20270517", "Kurban Bayramı (kesin değil)", TamBelirsiz),
        ("20270517", "20270518", "Kurban Bayrami Day 2 (kesin değil)", TamBelirsiz),
        ("20270518", "20270519", "Kurban Bayrami Day 3 (kesin değil)", TamBelirsiz),
        ("20270519", "20270520", "Atatürk’ü Anma, Gençlik ve Spor Bayramı", Tam),
        ("20270519", "20270520", "Kurban Bayrami Day 4 (kesin değil)", TamBelirsiz),
        ("20270715", "20270716", "Demokrasi ve Millî Birlik Günü", Tam),
        ("20270830", "20270831", "Zafer Bayramı", Tam),
        ("20271028", "20271029", "Cumhuriyet Bayramı Arifesi (yarım gün)", Yarim),
        ("20271029", "20271030", "Cumhuriyet Bayramı", Tam),
        // 2028
        ("20280226", "20280227", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20280227", "20280301", "Ramazan Bayramı (kesin değil)", TamBelirsiz),
        ("20280519", "20280520", "Atatürk’ü Anma, Gençlik ve Spor Bayramı", Tam),
        // 2029
        ("20290214", "20290215", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20290215", "20290218", "Ramazan Bayramı (kesin değil)", TamBelirsiz),
        ("20290423", "20290424", "Ulusal Egemenlik ve Çocuk Bayramı", Tam),
        ("20290423", "20290424", "Kurban Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20290424", "20290425", "Kurban Bayramı (kesin değil)", TamBelirsiz),
        ("20290425", "20290426", "Kurban Bayrami Day 2 (kesin değil)", TamBelirsiz),
        // 2036 (tablonun dışında)
        ("20360101", "20360102", "Yılbaşı", Tam),
        ("20360208", "20360209", "Kurban Bayrami Day 2 (kesin değil)", TamBelirsiz));

    private static string OnbellekYolu
        => Path.Combine((string)Tip("Kalender.Core.Paths").GetProperty("CacheFolder", Hepsi)!.GetValue(null)!, "tatiller.json");

    private static void OnbellegiSil()
    {
        if (File.Exists(OnbellekYolu))
        {
            File.Delete(OnbellekYolu);
        }
    }

    /// <summary>Ağa sahte akıştan çıkan bir tatil servisi.</summary>
    private static object TatilServisi(SahteTatilAkisi sahte)
    {
        object servis = Activator.CreateInstance(Tip("Kalender.Calendar.Holidays.HolidayService"), true)!;
        ((IDisposable)AlanOku(servis, "_http")).Dispose();
        AlanAta(servis, "_http", new HttpClient(sahte));
        return servis;
    }

    private static void Yenile(object servis, bool zorla)
        => ((Task)Cagir(servis, "RefreshAsync", zorla, CancellationToken.None)!).GetAwaiter().GetResult();

    /// <summary>Servisin o güne koyduğu tatiller: ad, yarım gün mü, tarihi kesin değil mi.</summary>
    private static List<(string Ad, bool Yarim, bool Belirsiz)> GunTatilleri(object servis, DateOnly gun)
    {
        var gunler = (IDictionary)Oku(servis, "Days")!;
        if (!gunler.Contains(gun))
        {
            return new();
        }

        return ((IEnumerable)gunler[gun]!).Cast<object>()
            .Select(t => ((string)Oku(t, "Name")!, Oku(t, "Kind")!.ToString() == "HalfDay", (bool)Oku(t, "DateUncertain")!))
            .ToList();
    }

    /// <summary>O günde tek tatil var, adı ve türü bu, tarihi kesin.</summary>
    private static bool Yalniz(object servis, DateOnly gun, string ad, bool yarim)
        => GunTatilleri(servis, gun) is [var t] && t.Ad == ad && t.Yarim == yarim && !t.Belirsiz;

    private static string Yazi(object servis, DateOnly gun)
        => $"{gun:yyyy-MM-dd}: " + string.Join(" | ", GunTatilleri(servis, gun)
            .Select(t => t.Ad + (t.Yarim ? " [yarım]" : "") + (t.Belirsiz ? " [belirsiz]" : "")));

    /// <summary>Takvimin o gün için gördüğü içerik (görünen ay o gün; tatil yoksa null).</summary>
    private static object? GunIcerigi(object servis, DateOnly gun)
    {
        object depo = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"), true)!;
        object veri = Activator.CreateInstance(Tip("Kalender.Calendar.CalendarDataService"), servis, null, depo)!;
        Cagir(veri, "SetViewMonth", new DateOnly(gun.Year, gun.Month, 1));
        return Cagir(Oku(veri, "Current")!, "For", gun);
    }

    private static bool Kirmizi(object? icerik) => icerik is not null && (bool)Oku(icerik, "IsHoliday")!;

    private static bool TablodakiBayramMi(string ad, DateOnly gun)
        => (bool)Statik(Tip("Kalender.Calendar.Holidays.DiniBayramlar"), "TablodakiBayramMi", ad, gun)!;

    private static string AdiDuzelt(string ad)
        => (string)Statik(Tip("Kalender.Calendar.Holidays.DiniBayramlar"), "AdiDuzelt", ad)!;

    // ------------------------------------------------------------------ 1. madde

    private static void Madde1()
    {
        Console.WriteLine("1) Ramazan ve Kurban bayramları Diyanet'in tarihleriyle; sabit tatiller korunuyor");
        OnbellegiSil();
        var sahte = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis = TatilServisi(sahte);
        try
        {
            Yenile(servis, true);
            Denetle("sınama düzeni: sahte akış indirildi", sahte.Kosullar.Count == 1 && (int)Oku(servis, "Count")! > 0);

            // Diyanet: 2027'de 9–11 Mart (arife 8 Mart), 2028'de 26–28 Şubat
            // (arife 25 Şubat), 2029'da 14–16 Şubat (arife 13 Şubat). Google bir gün geç.
            foreach (DateOnly ilk in new[] { new DateOnly(2027, 3, 9), new DateOnly(2028, 2, 26), new DateOnly(2029, 2, 14) })
            {
                DateOnly arife = ilk.AddDays(-1);
                DateOnly fazla = ilk.AddDays(3);
                Denetle($"{ilk.Year}: arife {arife:yyyy-MM-dd} yarım gün, Diyanet'e göre",
                    Yalniz(servis, arife, "Ramazan Bayramı Arifesi (yarım gün)", yarim: true), Yazi(servis, arife));
                Denetle($"{ilk.Year}: Ramazan Bayramı {ilk:yyyy-MM-dd} – {ilk.AddDays(2):yyyy-MM-dd} tam gün, eksiz",
                    Enumerable.Range(0, 3).All(i => Yalniz(servis, ilk.AddDays(i), "Ramazan Bayramı", yarim: false)),
                    string.Join(" ; ", Enumerable.Range(0, 3).Select(i => Yazi(servis, ilk.AddDays(i)))));
                Denetle($"{ilk.Year}: Google'ın fazladan günü ({fazla:yyyy-MM-dd}) tatil değil",
                    GunTatilleri(servis, fazla).Count == 0, Yazi(servis, fazla));
            }

            Denetle("2027 Kurban Bayramı da tablodan: arife 15 Mayıs, 16 Mayıs, 17 Mayıs 2. gün, 18 Mayıs 3. gün",
                Yalniz(servis, new DateOnly(2027, 5, 15), "Kurban Bayramı Arifesi (yarım gün)", yarim: true)
                && Yalniz(servis, new DateOnly(2027, 5, 16), "Kurban Bayramı", yarim: false)
                && Yalniz(servis, new DateOnly(2027, 5, 17), "Kurban Bayramı 2. gün", yarim: false)
                && Yalniz(servis, new DateOnly(2027, 5, 18), "Kurban Bayramı 3. gün", yarim: false),
                Yazi(servis, new DateOnly(2027, 5, 17)));

            var bayramlar = Enumerable.Range(0, 3 * 366).Select(i => new DateOnly(2027, 1, 1).AddDays(i))
                .Where(g => g.Year <= 2029)
                .SelectMany(g => GunTatilleri(servis, g))
                .Where(t => t.Ad.Contains("Ramazan") || t.Ad.Contains("Kurban"))
                .ToList();
            Denetle("tablodan gelenlerde \"(kesin değil)\" yok, tarihleri kesin",
                bayramlar.Count > 0 && bayramlar.All(t => !t.Ad.Contains("kesin değil") && !t.Belirsiz),
                bayramlar.Count + " bayram günü");

            // Sabit tatiller hep akıştan: bayramın içine düşenler de (19 Mayıs 2027, 23 Nisan 2029).
            var sabitler = new (DateOnly Gun, string Ad, bool Yarim)[]
            {
                (new(2027, 1, 1), "Yılbaşı", false),
                (new(2027, 4, 23), "Ulusal Egemenlik ve Çocuk Bayramı", false),
                (new(2027, 5, 1), "Emek ve Dayanışma Günü", false),
                (new(2027, 5, 19), "Atatürk’ü Anma, Gençlik ve Spor Bayramı", false),
                (new(2027, 7, 15), "Demokrasi ve Millî Birlik Günü", false),
                (new(2027, 8, 30), "Zafer Bayramı", false),
                (new(2027, 10, 28), "Cumhuriyet Bayramı Arifesi (yarım gün)", true),
                (new(2027, 10, 29), "Cumhuriyet Bayramı", false),
                (new(2028, 5, 19), "Atatürk’ü Anma, Gençlik ve Spor Bayramı", false),
                (new(2029, 4, 23), "Ulusal Egemenlik ve Çocuk Bayramı", false),
                (new(2026, 10, 28), "Cumhuriyet Bayramı Arifesi (yarım gün)", true),
            };
            var eksik = sabitler.Where(s => !GunTatilleri(servis, s.Gun).Contains((s.Ad, s.Yarim, false))).ToList();
            Denetle("sabit tatillerin hepsi adıyla ve türüyle yerinde", eksik.Count == 0,
                string.Join(" ; ", eksik.Select(s => Yazi(servis, s.Gun))));

            Denetle("tablonun yılında bayrama eklenen idari izin günü (25 Mayıs 2026) de tabloya bırakılıyor",
                GunTatilleri(servis, new DateOnly(2026, 5, 25)).Count == 0
                && Yalniz(servis, new DateOnly(2026, 5, 26), "Kurban Bayramı Arifesi (yarım gün)", yarim: true)
                && Yalniz(servis, new DateOnly(2026, 5, 30), "Kurban Bayramı 4. gün", yarim: false),
                Yazi(servis, new DateOnly(2026, 5, 25)));

            Denetle("tablonun dışındaki yılda (2023) bayram akıştan, akıştaki günlerinde",
                Yalniz(servis, new DateOnly(2023, 4, 20), "Ramazan Bayramı Arifesi (yarım gün)", yarim: true)
                && Yalniz(servis, new DateOnly(2023, 4, 21), "Ramazan Bayramı", yarim: false)
                && GunTatilleri(servis, new DateOnly(2023, 4, 23)).Contains(("Ramazan Bayramı", false, false))
                && Yalniz(servis, new DateOnly(2023, 6, 28), "Kurban Bayramı", yarim: false),
                Yazi(servis, new DateOnly(2023, 4, 23)));

            var gunler = (IDictionary)Oku(servis, "Days")!;
            Denetle("akışın kapsamadığı yıllara (2030, 2032) bayram eklenmiyor",
                !gunler.Contains(new DateOnly(2030, 2, 4)) && !gunler.Contains(new DateOnly(2030, 4, 13))
                && !gunler.Contains(new DateOnly(2032, 1, 14)));

            // Adla ve tarihle tanıma: sabit tatiller bayramın içine düşse de hiç atılmaz.
            var tanima = new (string Ad, DateOnly Gun, bool Beklenen)[]
            {
                ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", new(2027, 5, 19), false),
                ("Ulusal Egemenlik ve Çocuk Bayramı", new(2029, 4, 23), false),
                ("Cumhuriyet Bayramı Arifesi (yarım gün)", new(2027, 10, 28), false),
                ("Zafer Bayramı", new(2033, 3, 12), false),
                ("Yılbaşı", new(2033, 1, 1), false),
                ("Kurban Bayramı", new(2027, 8, 1), false),
                ("Ramazan Bayramı", new(2025, 3, 30), false),
                ("Ramazan Bayramı (kesin değil)", new(2027, 3, 12), true),
                ("RAMAZAN BAYRAMI", new(2027, 3, 10), true),
                ("Şeker Bayramı", new(2027, 3, 10), true),
                ("Eid al Fitr", new(2027, 3, 12), true),
                ("Kurban Bayrami Day 4 (kesin değil)", new(2027, 5, 19), true),
                ("Sacrifice Feast Holiday", new(2026, 5, 25), true),
                ("Eid al Adha Public Sector Holiday", new(2027, 5, 21), true),
            };
            var yanlis = tanima.Where(t => TablodakiBayramMi(t.Ad, t.Gun) != t.Beklenen).ToList();
            Denetle("akıştaki bayram kaydı adla ve tarihle tanınıyor, sabit tatiller hiç tanınmıyor", yanlis.Count == 0,
                string.Join(" ; ", yanlis.Select(t => $"{t.Ad} {t.Gun:yyyy-MM-dd}")));

            // Takvimde: 9 Mart 2027 Salı kırmızı, 12 Mart Cuma değil (süre hesabı).
            Denetle("takvimde 9 Mart 2027 kırmızı, 12 Mart 2027 değil",
                Kirmizi(GunIcerigi(servis, new DateOnly(2027, 3, 9))) && !Kirmizi(GunIcerigi(servis, new DateOnly(2027, 3, 12))));
            object? arifeIcerigi = GunIcerigi(servis, new DateOnly(2027, 3, 8));
            Denetle("8 Mart 2027 arife: kırmızı değil, gün kutusunda adı var",
                arifeIcerigi is not null && !Kirmizi(arifeIcerigi) && (bool)Oku(arifeIcerigi, "HasAnything")!);
            Denetle("29 Şubat 2028 kırmızı değil, 14 Şubat 2029 kırmızı",
                !Kirmizi(GunIcerigi(servis, new DateOnly(2028, 2, 29))) && Kirmizi(GunIcerigi(servis, new DateOnly(2029, 2, 14))));

            // Yeni ön bellek: biçimi 1.13'ünkiyle aynı, kayıtlar akıştaki gibi; 1.13 okuyabiliyor.
            string yazilan = File.ReadAllText(OnbellekYolu);
            using (JsonDocument belge = JsonDocument.Parse(yazilan))
            {
                var ustAlanlar = belge.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
                var gunAlanlari = belge.RootElement.GetProperty("Days").EnumerateArray()
                    .SelectMany(g => g.EnumerateObject().Select(p => p.Name)).ToHashSet();
                Denetle("ön bellek dosyasının biçimi 1.13'ünkiyle aynı (yeni alan yok)",
                    ustAlanlar.IsSubsetOf(new[] { "FetchedUtc", "ETag", "LastModified", "FullFeed", "Days" })
                    && gunAlanlari.IsSubsetOf(new[] { "Date", "Name", "Kind", "DateUncertain" }),
                    string.Join(",", ustAlanlar) + " / " + string.Join(",", gunAlanlari));
            }

            EskiOnbellek? eski = JsonSerializer.Deserialize<EskiOnbellek>(yazilan);
            Denetle("dosyada akışın kendisi duruyor (düzeltme okurken yapılıyor)",
                eski is { FullFeed: true }
                && eski.Days.Any(d => d.Date == new DateOnly(2027, 3, 12) && d.Name == "Ramazan Bayramı (kesin değil)")
                && eski.Days.Any(d => d.Date == new DateOnly(2027, 5, 17) && d.Name == "Kurban Bayrami Day 2 (kesin değil)"));

            // 1.13 aynı güne düşen kayıtlardan sonuncuyu tutar: tam gün olan sonda, gün kırmızı kalır.
            EskiTatil? yirmiUcNisan = eski?.Days.LastOrDefault(d => d.Date == new DateOnly(2029, 4, 23));
            Denetle("1.13 yeni dosyayı okursa 23 Nisan 2029 yine tam gün",
                yirmiUcNisan is { Kind: "Full", Name: "Ulusal Egemenlik ve Çocuk Bayramı" }
                && eski!.Days.Count(d => d.Date == new DateOnly(2029, 4, 23)) == 2,
                yirmiUcNisan?.Name ?? "yok");
        }
        finally
        {
            ((IDisposable)servis).Dispose();
        }

        // 1.13'ün yazdığı ön bellek (Google'ın kaymış tarihleri, her güne tek kayıt):
        // düzeltme yeniden indirmeyi beklemiyor, dosyaya da dokunulmuyor.
        OnbellegiSil();
        Directory.CreateDirectory(Path.GetDirectoryName(OnbellekYolu)!);
        var onbellek113 = new EskiOnbellek
        {
            FetchedUtc = DateTimeOffset.UtcNow.AddDays(-3),
            ETag = "\"eski\"",
            FullFeed = true,
            Days =
            {
                new() { Date = new(2025, 6, 7), Name = "Kurban Bayrami Day 2", Kind = "Full" },
                new() { Date = new(2027, 1, 1), Name = "Yılbaşı", Kind = "Full" },
                new() { Date = new(2027, 3, 9), Name = "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", Kind = "HalfDay", DateUncertain = true },
                new() { Date = new(2027, 3, 10), Name = "Ramazan Bayramı (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 3, 11), Name = "Ramazan Bayramı (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 3, 12), Name = "Ramazan Bayramı (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 5, 18), Name = "Kurban Bayrami Day 3 (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 5, 19), Name = "Atatürk’ü Anma, Gençlik ve Spor Bayramı", Kind = "Full" },
            },
        };
        File.WriteAllText(OnbellekYolu, JsonSerializer.Serialize(onbellek113), new UTF8Encoding(false));
        string? dosyaOnce = DosyaOku(OnbellekYolu);

        var sahte2 = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis2 = TatilServisi(sahte2);
        try
        {
            Cagir(servis2, "LoadCache");
            Yenile(servis2, false);
            Denetle("sınama düzeni: 1.13'ün ön belleği 30 gün dolmadan yeniden indirilmiyor", sahte2.Kosullar.Count == 0,
                sahte2.Kosullar.Count + " istek");
            Denetle("1.13'ün ön belleğinden okununca 2027 Ramazan Bayramı Diyanet'e göre",
                Yalniz(servis2, new DateOnly(2027, 3, 8), "Ramazan Bayramı Arifesi (yarım gün)", yarim: true)
                && Enumerable.Range(9, 3).All(g => Yalniz(servis2, new DateOnly(2027, 3, g), "Ramazan Bayramı", yarim: false))
                && GunTatilleri(servis2, new DateOnly(2027, 3, 12)).Count == 0,
                Yazi(servis2, new DateOnly(2027, 3, 12)));
            Denetle("1.13'ün ön belleğinde düşmüş Kurban Bayramı'nın 4. günü de 19 Mayıs 2027'de",
                GunTatilleri(servis2, new DateOnly(2027, 5, 19)).Select(t => t.Ad)
                    .SequenceEqual(new[] { "Atatürk’ü Anma, Gençlik ve Spor Bayramı", "Kurban Bayramı 4. gün" }),
                Yazi(servis2, new DateOnly(2027, 5, 19)));
            Denetle("1.13'ün ön belleğindeki tablo dışı ad da Türkçe",
                Yalniz(servis2, new DateOnly(2025, 6, 7), "Kurban Bayramı 2. gün", yarim: false), Yazi(servis2, new DateOnly(2025, 6, 7)));
            Denetle("1.13'ün ön bellek dosyasına dokunulmadı", DosyaOku(OnbellekYolu) == dosyaOnce);
        }
        finally
        {
            ((IDisposable)servis2).Dispose();
        }
    }

    // ----------------------------------------------------------------- 20. madde

    private static void Madde20()
    {
        Console.WriteLine("20) Tablonun dışındaki yıllarda bayram adları Türkçe; \"(kesin değil)\" eki kalıyor");
        OnbellegiSil();
        var sahte = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis = TatilServisi(sahte);
        try
        {
            Yenile(servis, true);

            var beklenen = new (DateOnly Gun, string Ad)[]
            {
                (new(2023, 6, 26), "Kurban Bayramı"),
                (new(2023, 6, 29), "Kurban Bayramı 2. gün"),
                (new(2023, 6, 30), "Kurban Bayramı 3. gün"),
                (new(2023, 7, 1), "Kurban Bayramı 4. gün"),
                (new(2024, 6, 20), "Kurban Bayramı"),
                (new(2024, 6, 21), "Kurban Bayramı (idari izin)"),
                (new(2025, 6, 7), "Kurban Bayramı 2. gün"),
            };
            var yanlis = beklenen.Where(b => !Yalniz(servis, b.Gun, b.Ad, yarim: false)).ToList();
            Denetle("akıştaki İngilizce ve yanlış yazılmış Kurban Bayramı adları Türkçe", yanlis.Count == 0,
                string.Join(" ; ", yanlis.Select(b => Yazi(servis, b.Gun))));

            Denetle("\"(kesin değil)\" eki ve belirsizlik korunuyor (2036)",
                GunTatilleri(servis, new DateOnly(2036, 2, 8)) is [("Kurban Bayramı 2. gün (kesin değil)", false, true)],
                Yazi(servis, new DateOnly(2036, 2, 8)));

            var gunler = (IDictionary)Oku(servis, "Days")!;
            var adlar = gunler.Keys.Cast<DateOnly>().SelectMany(g => GunTatilleri(servis, g)).Select(t => t.Ad).ToList();
            var kalan = adlar.Where(a => a.Contains("Bayrami") || a.Contains("Day ") || a.Contains("Sacrifice") || a.Contains("Eid"))
                .Distinct().ToList();
            Denetle("hiçbir tatil adında İngilizce ya da \"Bayrami\" kalmıyor", adlar.Count > 0 && kalan.Count == 0,
                string.Join(" ; ", kalan));

            var ornekler = new (string Akistaki, string Beklenen)[]
            {
                ("Kurban Bayrami Day 3 (kesin değil)", "Kurban Bayramı 3. gün (kesin değil)"),
                ("Kurban Bayrami", "Kurban Bayramı"),
                ("Ramazan Bayrami", "Ramazan Bayramı"),
                ("Sacrifice Feast Holiday", "Kurban Bayramı"),
                ("Sacrifice Feast Holiday (kesin değil)", "Kurban Bayramı (kesin değil)"),
                ("Eid al Adha Public Sector Holiday", "Kurban Bayramı (idari izin)"),
                ("Eid al Fitr", "Ramazan Bayramı"),
                ("Ramadan Feast Day 2", "Ramazan Bayramı 2. gün"),
                ("Eve of Sacrifice Feast (half day)", "Kurban Bayramı Arifesi (yarım gün)"),
                // Doğru adlar ve sabit tatiller olduğu gibi kalır.
                ("Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)"),
                ("Kurban Bayramı (kesin değil)", "Kurban Bayramı (kesin değil)"),
                ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", "Atatürk’ü Anma, Gençlik ve Spor Bayramı"),
                ("Cumhuriyet Bayramı Arifesi (yarım gün)", "Cumhuriyet Bayramı Arifesi (yarım gün)"),
                ("Yılbaşı", "Yılbaşı"),
            };
            var tutmayan = ornekler.Select(o => (o.Akistaki, o.Beklenen, Cikan: AdiDuzelt(o.Akistaki)))
                .Where(o => o.Cikan != o.Beklenen).ToList();
            Denetle("ad düzeltmesi örnekleri (doğru adlar ve sabit tatiller değişmiyor)", tutmayan.Count == 0,
                string.Join(" ; ", tutmayan.Select(o => $"{o.Akistaki} → {o.Cikan}")));
        }
        finally
        {
            ((IDisposable)servis).Dispose();
        }
    }

    // ----------------------------------------------------------------- 21. madde

    private static void Madde21()
    {
        Console.WriteLine("21) Aynı güne düşen iki tatil de saklanıyor, gün kutusunda ikisi de görünüyor");
        Type servisTipi = Tip("Kalender.Calendar.Holidays.HolidayService");
        Type ayristirici = Tip("Kalender.Calendar.Holidays.IcsParser");

        List<object> Ayikla(string akis)
            => ((IEnumerable)Statik(servisTipi, "Extract", Statik(ayristirici, "Parse", akis))!).Cast<object>().ToList();

        List<object> kayitlar = Ayikla(GoogleAkisi());
        List<string> GunKayitlari(DateOnly gun)
            => kayitlar.Where(k => (DateOnly)Oku(k, "Date")! == gun).Select(k => (string)Oku(k, "Name")!).ToList();

        Denetle("akıştan ayıklanınca 19 Mayıs 2027'deki iki kayıt da kalıyor",
            GunKayitlari(new DateOnly(2027, 5, 19)).ToHashSet()
                .SetEquals(new[] { "Atatürk’ü Anma, Gençlik ve Spor Bayramı", "Kurban Bayrami Day 4 (kesin değil)" }),
            string.Join(" | ", GunKayitlari(new DateOnly(2027, 5, 19))));
        Denetle("23 Nisan 2023 ve 23 Nisan 2029'daki iki kayıt da kalıyor",
            GunKayitlari(new DateOnly(2023, 4, 23)).Count == 2 && GunKayitlari(new DateOnly(2029, 4, 23)).Count == 2);

        List<object> ayni = Ayikla(TatilAkisi(
            ("20300101", "20300102", "Yılbaşı", Yarim),
            ("20300101", "20300102", "Yılbaşı", Tam),
            ("20300830", "20300831", "Zafer Bayramı", Tam),
            ("20300830", "20300831", "Zafer Bayramı", Tam)));
        Denetle("aynı ad aynı güne iki kez gelirse bir kez, tam gün olarak kalıyor",
            ayni.Count == 2 && ayni.All(k => Oku(k, "Kind")!.ToString() == "Full"), ayni.Count + " kayıt");

        OnbellegiSil();
        var sahte = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis = TatilServisi(sahte);
        try
        {
            Yenile(servis, true);
            var mayis19 = GunTatilleri(servis, new DateOnly(2027, 5, 19));
            Denetle("19 Mayıs 2027'de iki tatil: 19 Mayıs ve Kurban Bayramı'nın 4. günü",
                mayis19.SequenceEqual(new[]
                {
                    ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", false, false),
                    ("Kurban Bayramı 4. gün", false, false),
                }), Yazi(servis, new DateOnly(2027, 5, 19)));
            Denetle("23 Nisan 2029'da iki tatil, tam gün önce: 23 Nisan ve Kurban Bayramı arifesi",
                GunTatilleri(servis, new DateOnly(2029, 4, 23)).SequenceEqual(new[]
                {
                    ("Ulusal Egemenlik ve Çocuk Bayramı", false, false),
                    ("Kurban Bayramı Arifesi (yarım gün)", true, false),
                }), Yazi(servis, new DateOnly(2029, 4, 23)));
            Denetle("tablonun dışındaki yılda da (23 Nisan 2023) iki tatil",
                GunTatilleri(servis, new DateOnly(2023, 4, 23)).Select(t => t.Ad).ToHashSet()
                    .SetEquals(new[] { "Ulusal Egemenlik ve Çocuk Bayramı", "Ramazan Bayramı" }),
                Yazi(servis, new DateOnly(2023, 4, 23)));

            // Takvimin gördüğü gün içeriği ve renk kuralı (tam gün yarım güne üstün).
            object? icerik19 = GunIcerigi(servis, new DateOnly(2027, 5, 19));
            object? icerik23 = GunIcerigi(servis, new DateOnly(2029, 4, 23));
            object? icerikArife = GunIcerigi(servis, new DateOnly(2029, 2, 13));

            static List<(string Baslik, bool YarimGun)> TatilSatirlari(object? icerik)
                => icerik is null
                    ? new()
                    : ((IEnumerable)Oku(icerik, "Entries")!).Cast<object>()
                        .Where(e => Oku(e, "Kind")!.ToString() == "Holiday")
                        .Select(e => ((string)Oku(e, "Title")!, (bool)Oku(e, "YarimGun")!))
                        .ToList();

            Denetle("19 Mayıs 2027 kırmızı; gün kutusu için iki tatil satırı, ikisi de tam gün",
                Kirmizi(icerik19) && TatilSatirlari(icerik19).SequenceEqual(new[]
                {
                    ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", false),
                    ("Kurban Bayramı 4. gün", false),
                }), string.Join(" | ", TatilSatirlari(icerik19)));
            Denetle("23 Nisan 2029 kırmızı (tam gün arifeye üstün); arife satırı yarım gün işaretli",
                Kirmizi(icerik23) && TatilSatirlari(icerik23).SequenceEqual(new[]
                {
                    ("Ulusal Egemenlik ve Çocuk Bayramı", false),
                    ("Kurban Bayramı Arifesi (yarım gün)", true),
                }), string.Join(" | ", TatilSatirlari(icerik23)));
            Denetle("yalnız arife olan gün (13 Şubat 2029) kırmızı değil",
                icerikArife is not null && !Kirmizi(icerikArife) && TatilSatirlari(icerikArife).Count == 1);

            // Gün kutusu: pencere kurulur ama gösterilmez; satırlar ve renkleri okunur.
            Type pencereTipi = Tip("Kalender.Ui.DayPopupWindow");
            var pencere = (Window)Activator.CreateInstance(pencereTipi)!;
            try
            {
                object kirmizi = pencereTipi.GetField("HolidayBrush", Hepsi)!.GetValue(null)!;
                object olagan = pencereTipi.GetField("TitleBrush", Hepsi)!.GetValue(null)!;

                List<UIElement> Satirlar(object? icerik)
                {
                    Cagir(pencere, "BuildLines", icerik);
                    return ((Panel)AlanOku(pencere, "Lines")).Children.Cast<UIElement>().ToList();
                }

                List<UIElement> s19 = Satirlar(icerik19);
                var y19 = s19.OfType<TextBlock>().ToList();
                Denetle("gün kutusunda 19 Mayıs 2027'nin iki tatili iki satırda, arada ayraç, ikisi de kırmızı",
                    s19.Count == 3 && s19[1] is Border && y19.Count == 2
                    && y19[0].Text == "Atatürk’ü Anma, Gençlik ve Spor Bayramı" && y19[1].Text == "Kurban Bayramı 4. gün"
                    && y19.All(y => ReferenceEquals(y.Foreground, kirmizi)),
                    string.Join(" | ", y19.Select(y => y.Text)));

                List<UIElement> s23 = Satirlar(icerik23);
                var y23 = s23.OfType<TextBlock>().ToList();
                Denetle("gün kutusunda 23 Nisan 2029: tam gün tatil kırmızı, arife olağan renkte",
                    s23.Count == 3 && y23.Count == 2
                    && y23[0].Text == "Ulusal Egemenlik ve Çocuk Bayramı" && ReferenceEquals(y23[0].Foreground, kirmizi)
                    && y23[1].Text == "Kurban Bayramı Arifesi (yarım gün)" && ReferenceEquals(y23[1].Foreground, olagan),
                    string.Join(" | ", y23.Select(y => y.Text)));
            }
            finally
            {
                pencere.Close();
            }
        }
        finally
        {
            ((IDisposable)servis).Dispose();
        }
    }
}
