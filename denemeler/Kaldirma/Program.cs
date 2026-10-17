using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

// Kalender kaldırmasındaki dosya silme komutunun sınaması (yansımayla):
// uygulamada PowerShell çağrısı kalmadığı, cmd komutunun Türkçe ve özel
// karakterli yollarda çalıştığı, kilitli klasörü kilit kalkana kadar beklediği,
// vazgeçince yedek kayıtları bıraktığı, uzun listeyi cmd sınırına göre böldüğü,
// RunOnce yedek komutunun klasörü sildiği ve komutun uygulama kapanırken
// başlatılınca da işini bitirdiği.
//
// Yalnızca %TEMP% altında kendi açtığı klasörlere ve HKCU\Software\KalenderSinama
// anahtarına yazar, sonunda ikisini de siler. Gerçek RunOnce kaydına, kurulu
// uygulamaya ve ayarlara dokunmaz.
// Çıkış kodu 0 = hepsi geçti. Kullanım: dotnet run -c Release
internal static class Program
{
    private const BindingFlags Hepsi =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private const string SinamaAnahtari = @"Software\KalenderSinama\RunOnce";
    private const string Onek = "SinamaTemizlik";

    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static readonly Type Kurucu = Uyg.GetType("Kalender.Core.Installer", throwOnError: true)!;
    private static int _hata;
    private static int _gecen;

    private static void Denetle(string ad, bool sonuc, string ayrinti = "")
    {
        if (sonuc)
        {
            _gecen++;
            Console.WriteLine($"   GEÇTİ: {ad}{(ayrinti.Length > 0 ? " — " + ayrinti : "")}");
        }
        else
        {
            _hata++;
            Console.WriteLine($"   KALDI: {ad}{(ayrinti.Length > 0 ? " — " + ayrinti : "")}");
        }
    }

    private static IReadOnlyList<string> Komutlar(IReadOnlyList<string> hedefler, int deneme)
        => (IReadOnlyList<string>)Kurucu.GetMethod("BuildRemovalCommands", Hepsi)!
            .Invoke(null, new object[] { hedefler, SinamaAnahtari, Onek, deneme })!;

    private static string CmdYolu => (string)Kurucu.GetProperty("CmdPath", Hepsi)!.GetValue(null)!;

    /// <summary>Uygulamanın yaptığı gibi: pencere açmadan, Temp çalışma klasörüyle.</summary>
    private static Process Baslat(string argumanlar)
        => Process.Start(new ProcessStartInfo(CmdYolu, argumanlar)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath(),
        })!;

    /// <summary>Türkçe harf, boşluk, tırnak ve cmd'nin özel saydığı işaretlerle klasör adları.</summary>
    private static readonly string[] ZorAdlar =
    {
        "A klasör (ç&ğ) 'x' !ünlem",
        "B ^şapka;virgül,=eşit",
        "C ı İ Ş Ö Ü ç Ğ",
        "D [köşeli] {süslü} @#$ ~",
    };

    /// <summary>Hedef klasörü iç içe dosyalarla doldurur.</summary>
    private static void Doldur(string klasor)
    {
        Directory.CreateDirectory(Path.Combine(klasor, "alt klasör", "daha derin"));
        File.WriteAllText(Path.Combine(klasor, "ayarlar.json"), "{}");
        File.WriteAllText(Path.Combine(klasor, "alt klasör", "günlük.log"), "x");
        File.WriteAllText(Path.Combine(klasor, "alt klasör", "daha derin", "veri.bin"), "y");
    }

    private static void YedekKayitlariYaz(IReadOnlyList<string> hedefler)
    {
        using RegistryKey k = Registry.CurrentUser.CreateSubKey(SinamaAnahtari, writable: true);
        for (int n = 0; n < hedefler.Count; n++)
        {
            k.SetValue(Onek + (n + 1), (string)Kurucu.GetMethod("FallbackCommand", Hepsi)!.Invoke(null, new object[] { hedefler[n] })!);
        }
    }

    private static int KalanKayit()
    {
        using RegistryKey? k = Registry.CurrentUser.OpenSubKey(SinamaAnahtari);
        return k?.GetValueNames().Count(n => n.StartsWith(Onek, StringComparison.Ordinal)) ?? 0;
    }

    private static void KayitlariSil() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\KalenderSinama", throwOnMissingSubKey: false);

    private static string YeniKok()
    {
        string kok = Path.Combine(Path.GetTempPath(), "KalenderKaldirmaSinama-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(kok);
        return kok;
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

    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Duvar kâğıdı arayüzü bu sınamada kullanılmıyor; açılmaya kalkılırsa hata verir.
        DuvarKagidiniKapat();

        // Çocuk süreç: kaldırma penceresi gibi bir hedefi kilitli tutar, komutu
        // kapanışta başlatır ve çıkar. Kilit süreç kapanınca kalkar.
        if (args.Length == 2 && args[0] == "--cocuk")
        {
            return Cocuk(args[1]);
        }

        KayitlariSil();
        try
        {
            Sina();
        }
        finally
        {
            KayitlariSil();
        }

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    private static void Sina()
    {
        // ------------------------------------------------------------------
        Console.WriteLine("1) Uygulamada PowerShell çağrısı kalmadı");
        {
            byte[] dll = File.ReadAllBytes(Uyg.Location);
            string metin16 = Encoding.Unicode.GetString(dll);
            string metin8 = Encoding.Latin1.GetString(dll);
            bool ps = metin16.Contains("powershell", StringComparison.OrdinalIgnoreCase)
                || metin8.Contains("powershell", StringComparison.OrdinalIgnoreCase);
            bool enc = metin16.Contains("EncodedCommand", StringComparison.OrdinalIgnoreCase)
                || metin8.Contains("EncodedCommand", StringComparison.OrdinalIgnoreCase);
            Denetle("Kalender.dll'de \"powershell\" yok", !ps);
            Denetle("Kalender.dll'de \"EncodedCommand\" yok", !enc);
            Denetle("komut Windows'un kendi cmd.exe'siyle", string.Equals(CmdYolu,
                Path.Combine(Environment.SystemDirectory, "cmd.exe"), StringComparison.OrdinalIgnoreCase), CmdYolu);
            Denetle("varsayılan deneme sayısı 30", (int)Kurucu.GetField("RemovalTries", Hepsi)!.GetValue(null)! == 30);
        }

        // ------------------------------------------------------------------
        Console.WriteLine("2) Zor adlı klasörler, biri 2,5 sn kilitli: kilit kalkınca hepsi silinir");
        {
            string kok = YeniKok();
            var hedefler = ZorAdlar.Select(a => Path.Combine(kok, a)).ToList();
            hedefler.ForEach(Doldur);
            YedekKayitlariYaz(hedefler);

            IReadOnlyList<string> komutlar = Komutlar(hedefler, 10);
            Denetle("tek komut", komutlar.Count == 1, $"{komutlar.Count} komut, {komutlar[0].Length} karakter");

            var kilit = new FileStream(Path.Combine(hedefler[0], "alt klasör", "günlük.log"), FileMode.Open, FileAccess.Read, FileShare.None);
            var saat = Stopwatch.StartNew();
            using Process p = Baslat(komutlar[0]);
            Thread.Sleep(2500);
            Denetle("kilit sürerken kilitli klasör duruyor", Directory.Exists(hedefler[0]));
            Denetle("kilit sürerken kilitsiz klasörler silindi", hedefler.Skip(1).All(h => !Directory.Exists(h)));
            Denetle("kilit sürerken yedek kayıtlar duruyor", KalanKayit() == hedefler.Count);
            kilit.Dispose();

            bool bitti = p.WaitForExit(15000);
            saat.Stop();
            Denetle("komut kendiliğinden bitti", bitti && p.ExitCode == 0, $"{saat.Elapsed.TotalSeconds:F1} sn, çıkış {(bitti ? p.ExitCode : -1)}");
            Denetle("bütün klasörler silindi", hedefler.All(h => !Directory.Exists(h)));
            Denetle("yedek kayıtlar silindi", KalanKayit() == 0);
            Denetle("kök klasöre dokunulmadı (yalnızca hedefler)", Directory.Exists(kok));
            Directory.Delete(kok, true);
        }

        // ------------------------------------------------------------------
        Console.WriteLine("3) Kilit hiç kalkmazsa: denemeler bitince vazgeçer, yedek kayıtlar kalır");
        {
            string kok = YeniKok();
            var hedefler = ZorAdlar.Take(2).Select(a => Path.Combine(kok, a)).ToList();
            hedefler.ForEach(Doldur);
            YedekKayitlariYaz(hedefler);

            using var kilit = new FileStream(Path.Combine(hedefler[0], "ayarlar.json"), FileMode.Open, FileAccess.Read, FileShare.None);
            var saat = Stopwatch.StartNew();
            using Process p = Baslat(Komutlar(hedefler, 3)[0]);
            bool bitti = p.WaitForExit(15000);
            saat.Stop();
            Denetle("3 denemeden sonra bitti", bitti && saat.Elapsed.TotalSeconds is > 2 and < 8, $"{saat.Elapsed.TotalSeconds:F1} sn");
            Denetle("kilitli klasör duruyor", Directory.Exists(hedefler[0]));
            Denetle("kilitsiz klasör silindi", !Directory.Exists(hedefler[1]));
            Denetle("yedek kayıtlar duruyor (oturum açılışında silinsin)", KalanKayit() == 2);
            kilit.Dispose();

            // Oturum açılışında Windows'un yapacağı: RunOnce komutunu çalıştır.
            string yedek;
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(SinamaAnahtari)!)
            {
                yedek = (string)k.GetValue(Onek + "1")!;
            }

            Denetle("yedek komut cmd.exe'nin tam yoluyla", yedek.StartsWith("\"" + CmdYolu + "\" ", StringComparison.OrdinalIgnoreCase), yedek);
            Denetle("yedek komut 260 karakterden kısa", yedek.Length < 260, yedek.Length + " karakter");
            int bolum = yedek.IndexOf("\" ", 1, StringComparison.Ordinal);
            using (Process r = Process.Start(new ProcessStartInfo(yedek[1..bolum], yedek[(bolum + 2)..])
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = Path.GetTempPath(),
            })!)
            {
                r.WaitForExit(15000);
            }

            Denetle("yedek komut zor adlı klasörü sildi", !Directory.Exists(hedefler[0]));
            KayitlariSil();
            Directory.Delete(kok, true);
        }

        // ------------------------------------------------------------------
        Console.WriteLine("4) Uzun liste cmd sınırına göre bölünür, hepsi silinir");
        {
            string kok = YeniKok();
            var hedefler = Enumerable.Range(1, 80)
                .Select(i => Path.Combine(kok, $"Kalender-Kurulum-1.{i} uzun bir klasör adı çğıöşü {i:D3}"))
                .ToList();
            hedefler.ForEach(h => Directory.CreateDirectory(Path.Combine(h, "alt")));
            YedekKayitlariYaz(hedefler);

            IReadOnlyList<string> komutlar = Komutlar(hedefler, 5);
            int sinir = (int)Kurucu.GetField("MaxRemovalCommandLength", Hepsi)!.GetValue(null)!;
            Denetle("birden çok komuta bölündü", komutlar.Count > 1, $"{komutlar.Count} komut");
            Denetle("her komut sınırın altında", komutlar.All(k => k.Length <= sinir) && sinir < 8191,
                "en uzun " + komutlar.Max(k => k.Length) + " / " + sinir);

            var numaralar = komutlar
                .SelectMany(k => Regex.Matches(k, "/v " + Onek + @"(\d+) ").Select(m => int.Parse(m.Groups[1].Value)))
                .OrderBy(n => n).ToList();
            Denetle("her yedek kayıt tam bir komutta", numaralar.SequenceEqual(Enumerable.Range(1, 80)));

            var surecler = komutlar.Select(Baslat).ToList();
            bool bitti = surecler.All(s => s.WaitForExit(15000));
            surecler.ForEach(s => s.Dispose());
            Denetle("bütün komutlar bitti", bitti);
            Denetle("80 klasörün hepsi silindi", hedefler.All(h => !Directory.Exists(h)));
            Denetle("80 yedek kaydın hepsi silindi", KalanKayit() == 0);
            Directory.Delete(kok, true);
        }

        // ------------------------------------------------------------------
        Console.WriteLine("5) Kaldırma süreci kapanırken başlatılınca (uygulamadaki gibi)");
        {
            string kok = YeniKok();
            var saat = Stopwatch.StartNew();
            using (Process cocuk = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--cocuk \"{kok}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            })!)
            {
                cocuk.WaitForExit(20000);
                Denetle("kaldırma süreci kapandı", cocuk.HasExited && cocuk.ExitCode == 0);
            }

            var hedefler = ZorAdlar.Select(a => Path.Combine(kok, a)).ToList();
            bool silindi = false;
            for (int i = 0; i < 100 && !silindi; i++)
            {
                silindi = hedefler.All(h => !Directory.Exists(h)) && KalanKayit() == 0;
                if (!silindi)
                {
                    Thread.Sleep(100);
                }
            }

            saat.Stop();
            Denetle("süreç kapandıktan sonra klasörler ve yedek kayıtlar silindi", silindi,
                $"{saat.Elapsed.TotalSeconds:F1} sn");
            Directory.Delete(kok, true);
        }
    }

    /// <summary>
    /// Kaldırma penceresinin yerine geçen çocuk süreç: hedefleri oluşturur,
    /// birini (exe gibi) kapanana dek kilitli tutar, komutu uygulamanın
    /// kendi başlatma yöntemiyle kapanış anında başlatır.
    /// </summary>
    private static int Cocuk(string kok)
    {
        var hedefler = ZorAdlar.Select(a => Path.Combine(kok, a)).ToList();
        hedefler.ForEach(Doldur);
        YedekKayitlariYaz(hedefler);

        // Kapanışa kadar açık kalır; GC toplamasın diye alanda tutulur.
        _kilit = new FileStream(Path.Combine(hedefler[0], "ayarlar.json"), FileMode.Open, FileAccess.Read, FileShare.None);

        IReadOnlyList<string> komutlar = Komutlar(hedefler, 10);
        MethodInfo baslat = Kurucu.GetMethod("StartRemoval", Hepsi)!;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (string komut in komutlar)
            {
                baslat.Invoke(null, new object[] { komut });
            }
        };

        Thread.Sleep(300);
        return 0;
    }

    private static FileStream? _kilit;
}
