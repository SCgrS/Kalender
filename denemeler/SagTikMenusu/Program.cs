using System.Collections;
using System.Reflection;
using System.Text;

// Takvimin üzerindeki sağ tık menüsünün sınaması (yansımayla):
//  1. Bir güne sağ tıklanınca ilk satır "10 Ekim'i takvimde aç"; ay adına
//     gelen ek her ayda doğru (Ocak'ı, Ağustos'u, Eylül'ü, Ekim'i…).
//     Ekler elle yazılmış listeyle ve ünlü uyumundan ayrıca hesaplanarak
//     karşılaştırılır.
//  2. Gün ızgarasının dışında (ay adı, gün adları) ilk satır görünen ayı açar.
//  3. "Yeni etkinlik" ve "Yeni görev" satırları ve onların Google adresleri
//     kalktı; geri kalan satırlar ve sıraları aynı.
//
// Menü ekranda açılmaz; yalnızca satırları kuran işlev çağrılır. Kurulu
// Kalender'e, ayar dosyasına ve Google hesabına dokunulmaz.
// Çıkış kodu 0 = hepsi geçti. Kullanım: dotnet run -c Release
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

    private sealed record Satir(uint Id, string Metin, bool Etkin, bool Ayrac);

    private static List<Satir> Menu(DateOnly? gun, DateOnly gorunenAy, bool buAyda)
    {
        MethodInfo kur = Tip("Kalender.Core.AppHost").GetMethod("SagTikMenusu", Hepsi)!;
        var liste = (IEnumerable)kur.Invoke(null, new object?[] { gun, gorunenAy, buAyda })!;
        var satirlar = new List<Satir>();
        foreach (object s in liste)
        {
            Type t = s.GetType();
            satirlar.Add(new Satir(
                (uint)t.GetProperty("Id")!.GetValue(s)!,
                (string)t.GetProperty("Text")!.GetValue(s)!,
                (bool)t.GetProperty("Enabled")!.GetValue(s)!,
                (bool)t.GetProperty("IsSeparator")!.GetValue(s)!));
        }

        return satirlar;
    }

    private static uint Sabit(string ad)
        => (uint)Tip("Kalender.Core.AppHost").GetField(ad, Hepsi)!.GetValue(null)!;

    private static readonly string[] AyAdlari =
    {
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
    };

    /// <summary>Kullanıcının istediği biçim; elle yazıldı.</summary>
    private static readonly string[] BeklenenEkli =
    {
        "Ocak'ı", "Şubat'ı", "Mart'ı", "Nisan'ı", "Mayıs'ı", "Haziran'ı",
        "Temmuz'u", "Ağustos'u", "Eylül'ü", "Ekim'i", "Kasım'ı", "Aralık'ı",
    };

    /// <summary>
    /// Ünsüzle biten ada gelen belirtme eki, son ünlüye göre (büyük ünlü uyumu
    /// ve düzlük-yuvarlaklık): a/ı → ı, e/i → i, o/u → u, ö/ü → ü.
    /// </summary>
    private static string UnluUyumuylaEk(string ad)
    {
        const string unluler = "aıeioöuü";
        char son = ad.ToLowerInvariant().Last(c => unluler.Contains(c));
        char ek = son switch
        {
            'a' or 'ı' => 'ı',
            'e' or 'i' => 'i',
            'o' or 'u' => 'u',
            _ => 'ü',
        };
        return ad + "'" + ek;
    }

    /// <summary>
    /// Uygulamanın duvar kâğıdı fabrikasını (DuvarKagidiKayitlari.Ac) hata veren
    /// bir fabrikayla değiştirir: bu sınama Windows'un duvar kâğıdı arayüzünü hiç
    /// kullanmıyor; bir yol kazara açmaya kalkarsa gerçek duvar kâğıdına dokunulmaz.
    /// </summary>
    private static void DuvarKagidiniKapat()
    {
        const BindingFlags Her = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Type arayuz = Uyg.GetType("Kalender.Interop.IDesktopWallpaper", throwOnError: true)!;
        Func<object> hata = () => throw new InvalidOperationException("gerçek arayüz");
        Uyg.GetType("Kalender.Desktop.DuvarKagidiKayitlari", throwOnError: true)!.GetProperty("Ac", Her)!.SetValue(
            null,
            Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(arayuz), hata,
                typeof(Program).GetMethod(nameof(Uret), Her)!.MakeGenericMethod(arayuz)));
    }

    private static T Uret<T>(Func<object> kaynak) => (T)kaynak();

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Duvar kâğıdı arayüzü bu sınamada kullanılmıyor; açılmaya kalkılırsa hata verir.
        DuvarKagidiniKapat();

        uint gunuAc = Sabit("MenuGoToDay");
        uint onceki = Sabit("MenuPrevMonth");
        uint sonraki = Sabit("MenuNextMonth");
        uint buAy = Sabit("MenuThisMonth");
        uint ayarlar = Sabit("MenuSettings");

        // ------------------------------------------------------------------
        Console.WriteLine("1) Bir güne sağ tık: ilk satır");
        {
            var yanlislar = new List<string>();
            for (int ay = 1; ay <= 12; ay++)
            {
                string uyumlu = UnluUyumuylaEk(AyAdlari[ay - 1]);
                if (uyumlu != BeklenenEkli[ay - 1])
                {
                    yanlislar.Add($"liste {BeklenenEkli[ay - 1]} / uyum {uyumlu}");
                }

                int sonGun = DateTime.DaysInMonth(2026, ay);
                foreach (int g in new[] { 1, 10, sonGun })
                {
                    var gun = new DateOnly(2026, ay, g);
                    Satir ilk = Menu(gun, new DateOnly(2026, ay, 1), buAyda: false)[0];
                    string beklenen = $"{g} {BeklenenEkli[ay - 1]} takvimde aç";
                    if (ilk.Metin != beklenen || ilk.Id != gunuAc || !ilk.Etkin || ilk.Ayrac)
                    {
                        yanlislar.Add($"{gun:yyyy-MM-dd}: \"{ilk.Metin}\" (beklenen \"{beklenen}\")");
                    }
                }
            }

            Denetle("on iki ayın elle yazılan ekleri ünlü uyumuyla aynı",
                !yanlislar.Any(y => y.StartsWith("liste", StringComparison.Ordinal)), string.Join("; ", yanlislar));
            Denetle("her ayda 1., 10. ve son gün \"<gün> <Ay'ek> takvimde aç\"; satır günü açar",
                !yanlislar.Any(y => !y.StartsWith("liste", StringComparison.Ordinal)), string.Join("; ", yanlislar));

            Denetle("örnek: 10 Ekim'i takvimde aç",
                Menu(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1), true)[0].Metin == "10 Ekim'i takvimde aç");
            Denetle("örnek: 30 Ağustos'u takvimde aç",
                Menu(new DateOnly(2026, 8, 30), new DateOnly(2026, 8, 1), false)[0].Metin == "30 Ağustos'u takvimde aç");
            Denetle("örnek: 1 Ocak'ı takvimde aç (son ünsüz yumuşamaz)",
                Menu(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), false)[0].Metin == "1 Ocak'ı takvimde aç");

            // Ekim 2026'ya bakarken ızgaradaki soluk 30 Eylül'e sağ tıklandı.
            Satir tasan = Menu(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), true)[0];
            Denetle("komşu ayın soluk gününde o günün ayı yazar", tasan.Metin == "30 Eylül'ü takvimde aç", tasan.Metin);
        }

        // ------------------------------------------------------------------
        Console.WriteLine("2) Gün dışına (ay adı, gün adları) sağ tık: ilk satır");
        {
            Satir ilk = Menu(null, new DateOnly(2026, 10, 1), true)[0];
            Denetle("görünen ayı açar", ilk.Id == gunuAc && ilk.Metin == "Ekim 2026 ayını takvimde aç", ilk.Metin);
            Satir ocak = Menu(null, new DateOnly(2027, 1, 1), false)[0];
            Denetle("başka yılda yıl doğru", ocak.Metin == "Ocak 2027 ayını takvimde aç", ocak.Metin);
            Denetle("\"Google Takvim'de\" yazısı kalmadı",
                !Menu(null, new DateOnly(2026, 10, 1), true).Any(s => s.Metin.Contains("Google", StringComparison.Ordinal)));
        }

        // ------------------------------------------------------------------
        Console.WriteLine("3) Satırlar");
        {
            foreach (DateOnly? gun in new DateOnly?[] { new DateOnly(2026, 10, 10), null })
            {
                string ad = gun is null ? "gün dışı" : "gün";
                List<Satir> m = Menu(gun, new DateOnly(2026, 10, 1), buAyda: true);
                uint[] beklenen = { gunuAc, 0, onceki, sonraki, buAy, 0, ayarlar };
                Denetle($"{ad}: sıra aç | ayraç | önceki, sonraki, bu ay | ayraç | Ayarlar",
                    m.Select(s => s.Id).SequenceEqual(beklenen), string.Join(", ", m.Select(s => s.Ayrac ? "—" : s.Metin)));
                Denetle($"{ad}: yeni etkinlik ve yeni görev satırı yok",
                    !m.Any(s => s.Metin.Contains("etkinlik", StringComparison.OrdinalIgnoreCase)
                        || s.Metin.Contains("görev", StringComparison.OrdinalIgnoreCase)
                        || s.Metin.Contains("Yeni", StringComparison.Ordinal)));
                Denetle($"{ad}: art arda, başta ya da sonda ayraç yok",
                    !m[0].Ayrac && !m[^1].Ayrac && !m.Zip(m.Skip(1)).Any(c => c.First.Ayrac && c.Second.Ayrac));
            }

            List<Satir> ekim = Menu(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1), buAyda: true);
            Denetle("önceki ve sonraki ayın adları", ekim[2].Metin == "Önceki ay: Eylül" && ekim[3].Metin == "Sonraki ay: Kasım");
            Denetle("bu aydayken \"Bu aya dön\" soluk", ekim[4].Metin == "Bu aya dön" && !ekim[4].Etkin);
            Denetle("başka aydayken \"Bu aya dön\" tıklanabilir",
                Menu(new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 1), buAyda: false)[4].Etkin);

            List<Satir> ocak = Menu(null, new DateOnly(2027, 1, 1), false);
            List<Satir> aralik = Menu(null, new DateOnly(2026, 12, 1), false);
            Denetle("yıl dönümünde önceki/sonraki ay", ocak[2].Metin == "Önceki ay: Aralık" && aralik[3].Metin == "Sonraki ay: Ocak");

            Type hostTip = Tip("Kalender.Core.AppHost");
            Type linkTip = Tip("Kalender.Google.GoogleLinks");
            Denetle("menü kimlikleri ve Google adresleri kalktı",
                hostTip.GetField("MenuNewEvent", Hepsi) is null && hostTip.GetField("MenuNewTask", Hepsi) is null
                && linkTip.GetMethod("NewEvent", Hepsi) is null && linkTip.GetMethod("NewTask", Hepsi) is null);
            Denetle("gün ve ay adresleri duruyor",
                (string)linkTip.GetMethod("Day", Hepsi)!.Invoke(null, new object[] { new DateOnly(2026, 10, 10) })!
                    == "https://calendar.google.com/calendar/u/0/r/day/2026/10/10"
                && (string)linkTip.GetMethod("Month", Hepsi)!.Invoke(null, new object[] { new DateOnly(2026, 10, 1) })!
                    == "https://calendar.google.com/calendar/u/0/r/month/2026/10/1");
        }

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }
}
