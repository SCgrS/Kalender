using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

// Explorer'ın duvar kâğıdı listesi bozulunca Kalender'in hangi kayda yazdığının
// sınaması (yansımayla). 2026-10-17'de ev bilgisayarında tek ekranın listesinde
// iki kayıt vardı: ekranda görünen kaydın kimliği boş dizeydi, ekranın aygıt
// yolunu taşıyan kayıt hiçbir ekrana karşılık gelmiyordu (dikdörtgen sorusu
// E_FAIL). 1.14 görüntüyü bu hayalet kayda yazıyordu; ekranda iki gün önceki
// takvim kaldı. Senaryolar kalender.md'deki düzeltme tasarımından:
//   0. Kayıtların sınıflanması (canlı, bağlı değil, hayalet), listenin
//      tutarlılığı, günlük özeti; okuma hataları.
//   S1. Sağlıklı tek ekran: görüntü ekranın kendi kaydına yazılır, nöbet
//       "yerinde" der; kimliği okunamamış ekranda da; çıkarılmış haricî ekranın
//       kaydı (S_FALSE) listeyi bozuk saydırmaz.
//   S2. Bugünkü durum (boş kimlikli canlı kayıt + hayalet): yalnız aygıt yolu
//       verilmeden yazılır, görüntü ekranda; günlüğe bir kez hata, sonraki
//       yazımda yinelenmez; hayaletteki takvim ekranda sayılmaz.
//   S3. 1.14'ün bıraktığı durum (son yazım hayalete): nöbet iki ardışık
//       denetimden sonra yeniden yazdırır, aygıt yolu verilmeden; liste
//       düzelince kendi kaydına yazılır, günlüğe bir kez "düzeldi".
//   S4. Temizlik: ekranda duran dosyalar (boş kimlikli kayıt, hayalet) silinmez,
//       kullanılmayan eski dosya silinir; liste okunamazsa hiçbiri silinmez;
//       yazımdan sonraki temizlik de ekrana bakar.
//   S6. İki ekrandan yalnız biri yazılırken (takvim kaldırılırken) aygıt yolu
//       verilmeden yazılmaz; katman bağlı ekran sayısını her yazımdan önce verir.
//   S7. Çok ekranda tek kimliksiz kayıt: önce o ekran aygıt yolu verilmeden,
//       sonra öbür ekranlar kendi kayıtlarına; iki kimliksiz ekranda ya da öbür
//       ekranın kaydı yokken aygıt yolu verilmeden hiç yazılmaz.
//   S8. Klon kipi (aynı dikdörtgende iki canlı kayıt, tek ekran): aygıt yolu
//       verilmeden yazılır, günlüğe hata yazılmaz.
//   S9. Dalgalanma: tek seferlik farklı okuma yeniden yazım istemez.
//   S2b. 17.10 06:54'te ölçülen liste (aygıt yollu kayıt canlı, boş kimlikli
//        kayıt hayalet; roller önceki ölçümün tersi): liste tutarsız olduğu için
//        yine aygıt yolu verilmeden; roller yer değiştirse de yol değişmez.
//   S11. Nöbetin geri çekilmesi: yazılan görüntü ekrana ulaşmazsa art arda üç
//        uyuşmazlıktan sonra 5, beşten sonra 15 dakika; kullanıcıya bir kez
//        haber; uyanma, Explorer'ın yeniden başlaması ve yapılandırma
//        değişimi sıfırlar.
//   S12. Kaydın dosyası okunamazsa ekran "bilinmiyor": yeniden yazım istenmez.
//   S13. Çok ekranda yazım anında bağlı olmayan ekran atlanır, kaydı canlı olunca
//        nöbet yeniden yazdırır; atlanan ekran döngüye sokmaz.
//   S14. Temizlik: yalnız hayaletin dosyası okunamıyorsa eski dosyalar yine
//        silinir; canlı kayıt okunamazsa hiçbiri, günlüğe bir kez hata.
// (S5 ve S10 duvar kâğıdı yedeğiyle ilgili; WallpaperService değişikliğiyle gelir.)
//
// Gerçek duvar kâğıdına dokunulmaz: uygulamanın duvar kâğıdı fabrikası
// (DuvarKagidiKayitlari.Ac) ilk iş olarak açılırsa hata veren bir fabrikaya, her
// senaryoda bu programdaki sahte kabuğa bağlanır. Uygulamanın klasörü
// (Paths.RootFolder) %TEMP% altında geçici bir klasöre çevrilir (zemin dosyaları,
// yedek ve günlük orada) ve sonunda silinir; sıkıştırma kalitesi denetimi
// atlanır (kayıt defterine yazılmaz); Windows'un duvar kâğıdı ayarı bu programdan
// verilir. Pencere açılmaz. Çıkış kodu 0 = hepsi geçti.
//
// Ölçüm: "dotnet run -c Release -- --gercek-oku" Windows'un gerçek listesini
// yalnız okur ve uygulamanın kayıt tablosunu, her ekran için seçeceği yazım
// yolunu yazar. Gerçek arayüz salt okunur bir sarmalayıcıyla sarılır: Set*,
// Enable ya da AdvanceSlideshow çağrısı hata verir, yapılmaz.
// Kullanım: dotnet run -c Release
internal static class Program
{
    private const BindingFlags Hepsi =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private const int SOk = 0;
    private const int SFalse = 1;
    private const int EFail = unchecked((int)0x80004005);

    /// <summary>Ev bilgisayarındaki ekranın biçiminde bir aygıt yolu.</summary>
    private const string Aus = @"\\?\DISPLAY#AUS2700#5&8f63ace&0&UID41220#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";

    private const string Foto = @"C:\Users\deneme\Pictures\deniz.jpg";

    /// <summary>Resim kipi açıkken Windows'un verdiği türden bir yol.</summary>
    private const string ResimAcik = @"C:\Users\deneme\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper";

    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static readonly List<string> Hatalar = new();
    private static readonly List<Bitmap> Goruntuler = new();
    private static int _hata;
    private static int _gecen;

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

    private static Type Kayitlar => Tip("Kalender.Desktop.DuvarKagidiKayitlari");

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

    // ------------------------------------------------------ yansıma yardımcıları

    private static object? Oku(object o, string ad) => o.GetType().GetProperty(ad, Hepsi)!.GetValue(o);

    private static void Yaz(object o, string ad, object? deger) => o.GetType().GetProperty(ad, Hepsi)!.SetValue(o, deger);

    private static object AlanOku(object o, string ad) => o.GetType().GetField(ad, Hepsi)!.GetValue(o)!;

    private static void AlanAta(object o, string ad, object? deger) => o.GetType().GetField(ad, Hepsi)!.SetValue(o, deger);

    private static object? Cagir(object o, string ad, params object?[] a) => o.GetType().GetMethod(ad, Hepsi)!.Invoke(o, a);

    private static object? Statik(Type t, string ad, params object?[] a) => t.GetMethod(ad, Hepsi)!.Invoke(null, a);

    private static string? DosyaOku(string yol) => File.Exists(yol) ? Convert.ToHexString(File.ReadAllBytes(yol)) : null;

    // ------------------------------------------------- derlenmiş koddaki komutlar

    /// <summary>Bir IL komutu: işlem kodu ve varsa çağrılan yöntem ya da alan.</summary>
    private sealed record Komut(OpCode Kod, MemberInfo? Uye);

    private static readonly Dictionary<short, OpCode> IslemKodlari = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .GroupBy(k => k.Value)
        .ToDictionary(g => g.Key, g => g.First());

    /// <summary>Yöntemin gövdesini komut komut çözer.</summary>
    private static List<Komut> Komutlar(MethodBase yontem)
    {
        byte[] il = yontem.GetMethodBody()!.GetILAsByteArray()!;
        var sonuc = new List<Komut>();
        int i = 0;
        while (i < il.Length)
        {
            short deger = il[i] == 0xFE ? unchecked((short)(0xFE00 | il[i + 1])) : il[i];
            OpCode kod = IslemKodlari[deger];
            i += kod.Size;

            MemberInfo? uye = null;
            try
            {
                uye = kod.OperandType switch
                {
                    OperandType.InlineMethod => yontem.Module.ResolveMethod(BitConverter.ToInt32(il, i)),
                    OperandType.InlineField => yontem.Module.ResolveField(BitConverter.ToInt32(il, i)),
                    _ => null,
                };
            }
            catch (ArgumentException)
            {
                // Genel türün açık örneği; bu sınamada aranan bir şey değil.
            }

            i += kod.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };
            sonuc.Add(new Komut(kod, uye));
        }

        return sonuc;
    }

    private static int Bul(List<Komut> komutlar, Func<Komut, bool> kosul, int bas = 0)
    {
        for (int i = Math.Max(0, bas); i < komutlar.Count; i++)
        {
            if (kosul(komutlar[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool Cagri(Komut k, Type tip, string ad)
        => k.Uye is MethodBase m && m.DeclaringType == tip && m.Name == ad;

    // ------------------------------------------------------ deneme nesneleri

    /// <summary>
    /// Uygulamanın duvar kâğıdı fabrikasını (DuvarKagidiKayitlari.Ac) verilen
    /// kaynağa bağlar: uygulama arayüzü hangi yoldan açarsa açsın onu alır.
    /// </summary>
    private static void FabrikayiBagla(Func<object> kaynak)
    {
        Type arayuz = Tip("Kalender.Interop.IDesktopWallpaper");
        Kayitlar.GetProperty("Ac", Hepsi)!.SetValue(
            null,
            Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(arayuz), kaynak,
                typeof(Program).GetMethod(nameof(Uret), Hepsi)!.MakeGenericMethod(arayuz)));
    }

    private static T Uret<T>(Func<object> kaynak) => (T)kaynak();

    /// <summary>Sahte kabuk; kayıtlar sırasıyla. Uygulamanın fabrikası ona bağlanır.</summary>
    private static SahteKabuk Kabuk(params Kayit[] kayitlar)
    {
        var k = (SahteKabuk)DispatchProxy.Create(Tip("Kalender.Interop.IDesktopWallpaper"), typeof(SahteKabuk));
        k.Kayitlar.AddRange(kayitlar);
        FabrikayiBagla(() => k);
        return k;
    }

    private static Rectangle Sinir(int x, int g = 2560, int y = 1440) => new(x, 0, g, y);

    private static int _ekranNo;

    /// <summary>Hayali bir ekran (kimliği verilen); adı \\.\DISPLAY1 ile başlayan kimlik okunamamış sayılır.</summary>
    private static object Ekran(string kimlik, int x, int g = 2560, int y = 1440)
    {
        object m = Activator.CreateInstance(Tip("Kalender.Interop.MonitorTarget"), true)!;
        bool okunamadi = kimlik.StartsWith(@"\\.\", StringComparison.Ordinal);
        Yaz(m, "Handle", IntPtr.Zero);
        Yaz(m, "DeviceName", okunamadi ? kimlik : $@"\\.\DISPLAY{++_ekranNo}");
        Yaz(m, "StableId", kimlik);
        Yaz(m, "KimlikOkunamadi", okunamadi);
        Yaz(m, "Bounds", Sinir(x, g, y));
        Yaz(m, "WorkArea", new Rectangle(x, 0, g, y - 48));
        Yaz(m, "Dpi", 96u);
        Yaz(m, "IsPrimary", x == 0);
        return m;
    }

    private static string Kimlik(object ekran) => (string)Oku(ekran, "StableId")!;

    private static IList EkranListesi(params object[] ekranlar)
    {
        var liste = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Interop.MonitorTarget")))!;
        foreach (object e in ekranlar)
        {
            liste.Add(e);
        }

        return liste;
    }

    /// <summary>
    /// Duvar kâğıdına yazan tuval. Sıkıştırma kalitesi denetlenmiş sayılır:
    /// gerçek kayıt defterine yazılmasın.
    /// </summary>
    private static object Tuval(int bagliEkranSayisi)
    {
        object t = Activator.CreateInstance(Tip("Kalender.Desktop.WallpaperCanvas"), true)!;
        AlanAta(t, "_qualityChecked", true);
        Yaz(t, "BagliEkranSayisi", bagliEkranSayisi);
        return t;
    }

    /// <summary>Masaüstü katmanının "her şeyi yeniden yaz"ı (Forget); kalite denetimi yine atlanır.</summary>
    private static void Unut(object tuval)
    {
        Cagir(tuval, "Forget");
        AlanAta(tuval, "_qualityChecked", true);
    }

    private static Bitmap Goruntu(Color renk)
    {
        var b = new Bitmap(32, 18, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(renk);
        }

        Goruntuler.Add(b);
        return b;
    }

    /// <summary>Ekranlara verilen renkte görüntü yazdırır (Apply); yazılan ekran sayısı.</summary>
    private static int Uygula(object tuval, bool kaldirirken, params (object Ekran, Color Renk)[] ekranlar)
    {
        Type ekranTipi = Tip("Kalender.Desktop.CanvasScreen");
        var liste = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ekranTipi))!;
        foreach ((object ekran, Color renk) in ekranlar)
        {
            liste.Add(Activator.CreateInstance(ekranTipi, ekran, Goruntu(renk)));
        }

        // Dosya adında milisaniye var; peş peşe yazımlar aynı adı almasın.
        Thread.Sleep(3);
        object sonuc = Cagir(tuval, "Apply", liste, kaldirirken)!;
        return (bool)Oku(sonuc, "Success")! ? (int)Oku(sonuc, "Written")! : -1;
    }

    private static bool BizimMi(object tuval) => (bool)Cagir(tuval, "EkrandakilerBizimMi")!;

    private static HashSet<string> Duranlar(object tuval) => (HashSet<string>)Cagir(tuval, "EkrandaDuranlar")!;

    /// <summary>Tuvalin bir ekran için tuttuğu son yazım kaydı (kimlik verilirse o kayda, verilmezse aygıt yolu olmadan).</summary>
    private static object SonYazim(object ekran, string? kimlik, string dosya)
    {
        Type hedefTipi = Tip("Kalender.Desktop.DuvarKagidiHedefi");
        object tur = Enum.Parse(Tip("Kalender.Desktop.HedefTuru"), kimlik is null ? "Hepsi" : "Kimlik");
        object hedef = Activator.CreateInstance(hedefTipi, tur, kimlik)!;
        return Activator.CreateInstance(
            typeof(ValueTuple<,,>).MakeGenericType(ekran.GetType(), hedefTipi, typeof(string)), ekran, hedef, dosya)!;
    }

    /// <summary>Uygulamanın okuduğu kayıt tablosu (DuvarKagidiKayitlari.Oku); okunamazsa null.</summary>
    private static object? Tablo(SahteKabuk k) => Statik(Kayitlar, "Oku", k);

    private static List<object> Satirlar(object tablo) => ((IEnumerable)tablo).Cast<object>().ToList();

    private static bool Saglikli(object tablo) => (bool)Statik(Kayitlar, "Saglikli", tablo)!;

    /// <summary>Ekranın yazım yolu: "Kimlik:…", "Hepsi", "Kimliksiz" ya da "Yok".</summary>
    private static string Coz(object tablo, object ekran, int bagli)
    {
        object h = Statik(Kayitlar, "Coz", tablo, ekran, bagli)!;
        string tur = Oku(h, "Tur")!.ToString()!;
        return tur == "Kimlik" ? "Kimlik:" + Oku(h, "Kimlik") : tur;
    }

    private static string Yazimlar(SahteKabuk k)
        => string.Join(", ", k.Yazilanlar.Select(y => $"({(y.Aygit is null ? "NULL" : y.Aygit.Length > 24 ? y.Aygit[..24] + "…" : y.Aygit)}, {Path.GetFileName(y.Yol)})"));

    private static string ZeminKlasoru => (string)Tip("Kalender.Core.Paths").GetProperty("CanvasFolder")!.GetValue(null)!;

    /// <summary>Zemin klasöründe deneme dosyası; eskiyse son yazım zamanı 10 dakika önce.</summary>
    private static string ZeminDosyasi(string ad, bool eski = true)
    {
        Directory.CreateDirectory(ZeminKlasoru);
        string yol = Path.Combine(ZeminKlasoru, ad);
        File.WriteAllText(yol, "deneme");
        if (eski)
        {
            File.SetLastWriteTimeUtc(yol, DateTime.UtcNow.AddMinutes(-10));
        }

        return yol;
    }

    /// <summary>Günlüğe yazılan hata satırlarını toplar (Log, Trace'e de yazıyor).</summary>
    private sealed class GunlukDinleyici : TraceListener
    {
        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null && message.Contains("[HATA]", StringComparison.Ordinal))
            {
                Hatalar.Add(message);
            }
        }
    }

    // ------------------------------------------------------------- ana akış

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // İlk iş: uygulamanın duvar kâğıdı fabrikası. Gerçek arayüzü açan
        // fabrika yalnız ölçümde, salt okunur sarmalayıcının içinde kullanılır;
        // sınamada açılmaya kalkılırsa hata verir.
        var gercekFabrika = (Delegate)Kayitlar.GetProperty("Ac", Hepsi)!.GetValue(null)!;
        FabrikayiBagla(() => throw new InvalidOperationException("gerçek arayüz"));

        PropertyInfo kok = Tip("Kalender.Core.Paths").GetProperty("RootFolder", Hepsi)!;
        string gercekKok = (string)kok.GetValue(null)!;
        string yedekDosyasi = Path.Combine(gercekKok, "onceki-duvar-kagidi.json");
        string? yedekOnce = DosyaOku(yedekDosyasi);
        string gecici = Path.Combine(Path.GetTempPath(), "Kalender Hayalet Ekran ğüşıöç " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gecici);
        kok.SetValue(null, gecici);

        try
        {
            if (args.Contains("--gercek-oku"))
            {
                return GercekOku(gercekFabrika);
            }

            Tip("Kalender.Desktop.WallpaperService").GetProperty("SistemDuvarKagidiOkuyucu", Hepsi)!
                .SetValue(null, new Func<string?>(() => ResimAcik));
            Trace.Listeners.Add(new GunlukDinleyici());

            Bolum(Siniflama);
            Bolum(S1);
            Bolum(S2);
            Bolum(S3);
            Bolum(S4);
            Bolum(S6);
            Bolum(S7);
            Bolum(S8);
            Bolum(S9);
            Bolum(S2b);
            Bolum(S11);
            Bolum(S12);
            Bolum(S13);
            Bolum(S14);
        }
        finally
        {
            kok.SetValue(null, gercekKok);
            foreach (Bitmap b in Goruntuler)
            {
                b.Dispose();
            }

            try
            {
                Directory.Delete(gecici, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   (deneme klasörü silinemedi: " + ex.Message + ")");
            }
        }

        Denetle("kurulu Kalender'in duvar kâğıdı yedeğine dokunulmadı", DosyaOku(yedekDosyasi) == yedekOnce);

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    // ------------------------------------------------------- 0. sınıflama

    private static void Siniflama()
    {
        Console.WriteLine("0) Kayıtların sınıflanması, listenin tutarlılığı, günlük özeti");
        string Sinif(int hr, Rectangle r) => Statik(Kayitlar, "Siniflandir", hr, r)!.ToString()!;
        Denetle("S_OK ve dolu dikdörtgen canlı; S_FALSE ya da boş dikdörtgen bağlı değil; hata hayalet",
            Sinif(SOk, Sinir(0)) == "Canli" && Sinif(SFalse, Rectangle.Empty) == "BagliDegil"
            && Sinif(SFalse, Sinir(0)) == "BagliDegil" && Sinif(SOk, Rectangle.Empty) == "BagliDegil"
            && Sinif(EFail, Rectangle.Empty) == "Hayalet" && Sinif(EFail, Sinir(0)) == "Hayalet");

        bool Tutarli(params Kayit[] kayitlar) => Saglikli(Tablo(Kabuk(kayitlar))!);
        Denetle("sağlıklı iki ekran tutarlı", Tutarli(new("A", SOk, Sinir(0), Foto), new("B", SOk, Sinir(2560), Foto)));
        Denetle("çıkarılmış haricî ekranın kaydı (S_FALSE) listeyi bozuk saydırmıyor",
            Tutarli(new("A", SOk, Sinir(0), Foto), new("B", SFalse, Rectangle.Empty, Foto)));
        Denetle("hayalet kayıt, kimliği boş canlı kayıt ve iki kayıtta geçen kimlik tutarsız",
            !Tutarli(new("A", SOk, Sinir(0), Foto), new("B", EFail, Rectangle.Empty, Foto))
            && !Tutarli(new Kayit("", SOk, Sinir(0), Foto))
            && !Tutarli(new("A", SOk, Sinir(0), Foto), new("a", SFalse, Rectangle.Empty, Foto)));

        // Okuma: boş kimlikli kayıt da dikdörtgeni ve dosyasıyla okunuyor.
        SahteKabuk k = Kabuk(new Kayit("", SOk, Sinir(0), "f15.bmp"), new(Aus, EFail, Rectangle.Empty, "f16.bmp"));
        object tablo = Tablo(k)!;
        List<object> s = Satirlar(tablo);
        Denetle("boş kimlikli kayıt canlı (dikdörtgeni ve dosyası okunuyor), aygıt yollu kayıt hayalet (E_FAIL)",
            s.Count == 2 && Oku(s[0], "Durum")!.ToString() == "Canli" && (string?)Oku(s[0], "Dosya") == "f15.bmp"
            && (Rectangle)Oku(s[0], "Sinir")! == Sinir(0)
            && Oku(s[1], "Durum")!.ToString() == "Hayalet" && (int)Oku(s[1], "RectHr")! == EFail
            && (string?)Oku(s[1], "Dosya") == "f16.bmp");
        Denetle("aygıt yolu verilmeden okunan dosya da saklanıyor", (string?)Oku(tablo, "Ortak") == "f15.bmp");

        string ozet = (string)Statik(Kayitlar, "Ozet", tablo)!;
        Denetle("günlük özeti tek satır: kimlik, durum, sonuç kodu, dikdörtgen, dosya adı",
            !ozet.Contains('\n') && ozet.Contains("(boş kimlik) Canli") && ozet.Contains("Hayalet hr=0x80004005")
            && ozet.Contains("0,0 2560x1440 -> f15.bmp") && ozet.Contains("aygıtsız -> f15.bmp"), ozet);

        k.OkumadaHata = true;
        List<object> hatali = Satirlar(Tablo(k)!);
        Denetle("dosya okunamayan kayıt yine sınıflanıyor, dosyası bilinmiyor (null)",
            hatali.Count == 2 && Oku(hatali[0], "Durum")!.ToString() == "Canli" && Oku(hatali[0], "Dosya") is null
            && Oku(Tablo(k)!, "Ortak") is null);
        k.SayimdaHata = true;
        Denetle("kayıt sayısı okunamazsa liste bilinmiyor (null)", Tablo(k) is null);
    }

    // ------------------------------------------------------------------- S1

    private static void S1()
    {
        Console.WriteLine("S1) Sağlıklı tek ekran: görüntü ekranın kendi kaydına yazılıyor");
        int hata = Hatalar.Count;
        SahteKabuk k = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        object ekran = Ekran(Aus, 0);
        object tuval = Tuval(1);
        int yazilan = Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("tek yazım, ekranın aygıt yoluyla", yazilan == 1 && k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit == Aus, Yazimlar(k));
        Denetle("nöbet: ekranda Kalender'in görüntüsü var", BizimMi(tuval) && BizimMi(tuval));
        Denetle("kapanışta ekranda duruyor sayılıyor", Duranlar(tuval).SetEquals(new[] { Aus }));
        Denetle("günlüğe hata yazılmadı", Hatalar.Count == hata);

        k.Kayitlar[0].Dosya = Foto;
        Denetle("kullanıcı başka resim seçince nöbet yeniden yazım istiyor", !BizimMi(tuval));
        Denetle("kapanışta ekranda durmuyor sayılıyor (takvimsiz zemin yazılmaz)", Duranlar(tuval).Count == 0);

        // Kimliği okunamamış ekran (\\.\DISPLAY1): tek canlı kayıt onundur.
        SahteKabuk k2 = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        object tuval2 = Tuval(1);
        Uygula(tuval2, false, (Ekran(@"\\.\DISPLAY1", 0), Color.Navy));
        Denetle("kimliği okunamamış tek ekranda da görüntü canlı kaydın aygıt yoluna yazılıyor",
            k2.Yazilanlar.Count == 1 && k2.Yazilanlar[0].Aygit == Aus && BizimMi(tuval2), Yazimlar(k2));

        // Dizüstü + çıkarılmış haricî ekran: haricî ekranın kaydı S_FALSE veriyor.
        string harici = @"\\?\DISPLAY#DEL4321#1";
        SahteKabuk k3 = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto), new(harici, SFalse, Rectangle.Empty, Foto));
        object tuval3 = Tuval(1);
        Uygula(tuval3, false, (Ekran(Aus, 0), Color.Navy));
        Denetle("çıkarılmış haricî ekran varken görüntü ekranın kendi kaydına; aygıt yolu verilmeden yazılmıyor, haricî ekranın fotoğrafı yerinde",
            k3.Yazilanlar.Count == 1 && k3.Yazilanlar[0].Aygit == Aus && k3.Kayitlar[1].Dosya == Foto, Yazimlar(k3));
        Denetle("günlüğe hata yazılmadı (bağlı olmayan ekran olağan durum)", Hatalar.Count == hata);
    }

    // ------------------------------------------------------------------- S2

    private static void S2()
    {
        Console.WriteLine("S2) Bugünkü durum: boş kimlikli canlı kayıt + hayalet; yalnız aygıt yolu verilmeden yazılıyor");
        int hata = Hatalar.Count;
        string f15 = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261015-114200-000.bmp");
        string f16 = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261017-000002-000.bmp");
        SahteKabuk k = Kabuk(new Kayit("", SOk, Sinir(0), f15), new(Aus, EFail, Rectangle.Empty, f16));
        object ekran = Ekran(Aus, 0);

        object tablo = Tablo(k)!;
        Denetle("liste tutarsız; tek ekranda yazım yolu aygıt yolu verilmeden (hayalet seçilmiyor)",
            !Saglikli(tablo) && Coz(tablo, ekran, 1) == "Hepsi", Coz(tablo, ekran, 1));

        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("tek yazım, aygıt yolu verilmeden; hayalete hiç yazılmıyor",
            k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit is null, Yazimlar(k));
        Denetle("görüntü ekranda görünen (boş kimlikli) kayıtta", k.Yazilanlar.Count == 1 && k.Kayitlar[0].Dosya == k.Yazilanlar[0].Yol);
        Denetle("nöbet: ekranda Kalender'in görüntüsü var (aygıt yolu verilmeden yazılan da denetleniyor)", BizimMi(tuval) && BizimMi(tuval));
        Denetle("kapanışta ekranda duruyor sayılıyor", Duranlar(tuval).SetEquals(new[] { Aus }));
        Denetle("günlüğe bir kez hata yazıldı, kayıt tablosuyla",
            Hatalar.Count == hata + 1 && Hatalar[^1].Contains("tutarsız") && Hatalar[^1].Contains("aygıt yolu verilmeden")
            && Hatalar[^1].Contains("(boş kimlik) Canli") && Hatalar[^1].Contains("Hayalet"),
            Hatalar.Count > hata ? Hatalar[^1] : "hata yok");

        Uygula(tuval, false, (ekran, Color.Teal));
        Denetle("sonraki yazım da aygıt yolu verilmeden; günlüğe yeni hata yazılmadı",
            k.Yazilanlar.Count == 2 && k.Yazilanlar[1].Aygit is null && Hatalar.Count == hata + 1, Yazimlar(k));

        // Takvimi gösteren ekranlar (takvim kaldırılırken): hayaletteki takvim ekranda görünmüyor.
        SahteKabuk k3 = Kabuk(new Kayit("", SOk, Sinir(0), Foto), new(Aus, EFail, Rectangle.Empty, f16));
        HashSet<string> Gosterenler() => (HashSet<string>)Statik(
            Tip("Kalender.Desktop.WallpaperCanvas"), "TakvimGosterenler", EkranListesi(ekran), k3)!;
        Denetle("hayalet kaydın gösterdiği takvim ekranda sayılmıyor (ekranda kullanıcının fotoğrafı)", Gosterenler().Count == 0);
        k3.Kayitlar[0].Dosya = f15;
        Denetle("boş kimlikli canlı kayıttaki takvim sayılıyor", Gosterenler().SetEquals(new[] { Aus }));
    }

    // ------------------------------------------------------------------- S3

    private static void S3()
    {
        Console.WriteLine("S3) 1.14'ün bıraktığı durum: nöbet iki denetimden sonra yeniden yazdırıyor; liste düzelince kendi kaydına");
        int hata = Hatalar.Count;
        string f15 = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261015-114200-000.bmp");
        string f16 = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261017-000002-000.bmp");
        SahteKabuk k = Kabuk(new Kayit("", SOk, Sinir(0), f15), new(Aus, EFail, Rectangle.Empty, f16));
        object ekran = Ekran(Aus, 0);

        // 1.14'ün yaptığı: son yazım hayalet kayda (f16); ekranda 15 Ekim (f15).
        object tuval = Tuval(1);
        ((IDictionary)AlanOku(tuval, "_sonYazilanlar"))[Aus] = SonYazim(ekran, Aus, f16);
        ((IDictionary)AlanOku(tuval, "_hashes"))[Aus] = "eski";
        AlanAta(tuval, "_yazimdakiEkranSayisi", 1);

        bool ilk = BizimMi(tuval);
        bool ikinci = BizimMi(tuval);
        Denetle("nöbet ilk farklı okumada bekliyor, ikinci ardışık okumada yeniden yazım istiyor", ilk && !ikinci, $"{ilk} / {ikinci}");

        // Masaüstü katmanının yaptığı: her şeyi unut, yeniden çiz.
        Unut(tuval);
        Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("yeniden yazım aygıt yolu verilmeden; görüntü ekranda",
            k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit is null && k.Kayitlar[0].Dosya == k.Yazilanlar[0].Yol, Yazimlar(k));
        Denetle("yeniden yazımdan sonra nöbet 'yerinde' diyor", BizimMi(tuval) && BizimMi(tuval));

        // Liste düzeldi (oturum kapatılıp açıldı): tek kayıt, ekranın aygıt yoluyla; ekranda bizim görüntümüz.
        string ekrandaki = k.Kayitlar[0].Dosya;
        k.Kayitlar.Clear();
        k.Kayitlar.Add(new(Aus, SOk, Sinir(0), ekrandaki));
        bool d1 = BizimMi(tuval);
        bool d2 = BizimMi(tuval);
        Denetle("liste düzelince de iki ardışık denetimden sonra yeniden yazım isteniyor", d1 && !d2, $"{d1} / {d2}");
        Unut(tuval);
        Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("liste düzelince ekranın kendi kaydına yazılıyor",
            k.Yazilanlar.Count == 2 && k.Yazilanlar[1].Aygit == Aus && BizimMi(tuval), Yazimlar(k));
        Denetle("günlükte bozukluk için bir, düzelme için bir hata satırı",
            Hatalar.Count == hata + 2 && Hatalar[^2].Contains("tutarsız") && Hatalar[^1].Contains("düzeldi"),
            string.Join(" | ", Hatalar.Skip(hata)));
    }

    // ------------------------------------------------------------------- S4

    private static void S4()
    {
        Console.WriteLine("S4) Temizlik ekranda duran dosyaları koruyor (boş kimlikli kayıt ve hayalet dâhil)");
        string f15 = ZeminDosyasi("zemin-aaaaaaaa-20261015-114200-000.bmp");
        string f16 = ZeminDosyasi("zemin-aaaaaaaa-20261017-000002-000.bmp");
        string f0 = ZeminDosyasi("zemin-aaaaaaaa-20261014-090000-000.bmp");
        SahteKabuk k = Kabuk(new Kayit("", SOk, Sinir(0), f15), new(Aus, EFail, Rectangle.Empty, f16));
        object tuval = Tuval(1);

        // Zamanlayıcının çağırdığı temizlik (arayüz verilmeden): arayüzü kendisi açıyor.
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        Denetle("boş kimlikli kaydın ve hayaletin dosyası kalıyor, kullanılmayan eski dosya siliniyor",
            File.Exists(f15) && File.Exists(f16) && !File.Exists(f0));

        f0 = ZeminDosyasi("zemin-aaaaaaaa-20261014-090000-000.bmp");
        k.SayimdaHata = true;
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        k.SayimdaHata = false;
        k.OkumadaHata = true;
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        k.OkumadaHata = false;
        FabrikayiBagla(() => throw new InvalidOperationException("gerçek arayüz"));
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        FabrikayiBagla(() => k);
        Denetle("liste okunamazsa ya da arayüz açılamazsa hiçbir dosya silinmiyor",
            File.Exists(f15) && File.Exists(f16) && File.Exists(f0));

        // Yazımdan sonraki temizlik de ekrana bakıyor. En kötü durum: aygıt yolu
        // verilmeden yazılan görüntü boş kimlikli kayda ulaşmıyor (bozuk kabuk;
        // ölçülmedi). Ekranda kalan eski gün silinmemeli; görüntünün ekrana
        // çıkmadığını nöbet görmeli.
        k.AygitsizBosKaydaUlasmaz = true;
        Uygula(tuval, false, (Ekran(Aus, 0), Color.Olive));
        string yeni = k.Yazilanlar.Count == 1 ? k.Yazilanlar[0].Yol : "?";
        Denetle("yazımdan sonra ekranda kalan eski gün silinmiyor; hiçbir kayıtta olmayan eski dosyalar siliniyor",
            File.Exists(f15) && File.Exists(yeni) && !File.Exists(f16) && !File.Exists(f0), Yazimlar(k));
        Denetle("görüntü ekrana çıkmadı; nöbet bunu görüyor", !BizimMi(tuval));
    }

    // ------------------------------------------------------------------- S6

    private static void S6()
    {
        Console.WriteLine("S6) İki ekrandan yalnız biri yazılırken aygıt yolu verilmeden yazılmıyor");
        string aId = @"\\?\DISPLAY#AAA0001#1";
        string bId = @"\\?\DISPLAY#BBB0002#1";
        object a = Ekran(aId, 0, 1920, 1080);
        object b = Ekran(bId, 1920, 1920, 1080);

        SahteKabuk k = Kabuk(new Kayit(aId, SOk, Sinir(0, 1920, 1080), Foto), new(bId, SOk, Sinir(1920, 1920, 1080), Foto));
        Uygula(Tuval(2), true, (a, Color.Gray));
        Denetle("sağlıklı listede yalnız o ekran, kendi kaydına yazılıyor",
            k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit == aId && k.Kayitlar[1].Dosya == Foto, Yazimlar(k));

        SahteKabuk bozuk = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), Foto), new(bId, SOk, Sinir(1920, 1920, 1080), Foto), new(aId, EFail, Rectangle.Empty, Foto));
        Uygula(Tuval(2), true, (a, Color.Gray));
        Denetle("kaydı kimliksiz ekran tek başına yazılırken atlanıyor (aygıt yolu verilmeden yazmak öbür ekranı da boyardı)",
            bozuk.Yazilanlar.Count == 0 && bozuk.Kayitlar[1].Dosya == Foto, Yazimlar(bozuk));

        object tablo = Tablo(bozuk)!;
        Denetle("bağlı ekran sayısı verilmeseydi alt küme tek ekran sanılırdı (bu yüzden katman veriyor)",
            Coz(tablo, a, 1) == "Hepsi" && Coz(tablo, a, 2) == "Kimliksiz" && Coz(tablo, b, 2) == "Kimlik:" + bId);

        // Masaüstü katmanı (derlenmiş koddan): çizimde ve takvimsiz zeminde yazımdan önce bağlı ekran sayısını veriyor.
        Type katman = Tip("Kalender.Desktop.DesktopLayerManager");
        Type tuvalTipi = Tip("Kalender.Desktop.WallpaperCanvas");
        bool Veriyor(string yontem)
        {
            List<Komut> k2 = Komutlar(katman.GetMethod(yontem, Hepsi)!);
            int ver = Bul(k2, x => Cagri(x, tuvalTipi, "set_BagliEkranSayisi"));
            int yaz = Bul(k2, x => Cagri(x, tuvalTipi, "Apply"));
            return ver > 0 && yaz > ver && Komutlar(katman.GetMethod(yontem, Hepsi)!)
                .GetRange(Math.Max(0, ver - 3), 3).Any(x => x.Uye is FieldInfo f && f.Name == "_monitors");
        }

        Denetle("katman çizimde ve takvimsiz zeminde yazımdan önce bağlı ekran sayısını veriyor (_monitors)",
            Veriyor("RefreshOnce") && Veriyor("PaintPlainBackground"));
    }

    // ------------------------------------------------------------------- S7

    private static void S7()
    {
        Console.WriteLine("S7) Çok ekranda tek kimliksiz kayıt: önce o ekran aygıt yolu verilmeden, sonra öbürleri kendi kayıtlarına");
        string aId = @"\\?\DISPLAY#AAA0001#1";
        string bId = @"\\?\DISPLAY#BBB0002#1";
        object a = Ekran(aId, 0, 1920, 1080);
        object b = Ekran(bId, 1920, 1920, 1080);

        SahteKabuk k = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), Foto), new(bId, SOk, Sinir(1920, 1920, 1080), Foto), new(aId, EFail, Rectangle.Empty, Foto));
        object tuval = Tuval(2);
        Uygula(tuval, false, (a, Color.Red), (b, Color.Blue));
        Denetle("yazım sırası: önce kimliksiz ekran aygıt yolu verilmeden, sonra öbür ekran kendi kaydına",
            k.Yazilanlar.Count == 2 && k.Yazilanlar[0].Aygit is null && k.Yazilanlar[1].Aygit == bId, Yazimlar(k));
        Denetle("sonunda her ekranda kendi görüntüsü",
            k.Yazilanlar.Count == 2 && k.Kayitlar[0].Dosya == k.Yazilanlar[0].Yol && k.Kayitlar[1].Dosya == k.Yazilanlar[1].Yol);
        Denetle("nöbet: iki ekranda da Kalender'in görüntüsü var", BizimMi(tuval) && Duranlar(tuval).SetEquals(new[] { aId, bId }));

        Uygula(tuval, false, (a, Color.Green), (b, Color.Blue));
        Denetle("yalnız kimliksiz ekran değişse de öbür ekran yeniden yazılıyor (aygıt yolu verilmeden yazım onu da boyadı)",
            k.Yazilanlar.Count == 4 && k.Yazilanlar[2].Aygit is null && k.Yazilanlar[3].Aygit == bId
            && k.Kayitlar[1].Dosya == k.Yazilanlar[3].Yol, Yazimlar(k));

        Uygula(tuval, false, (a, Color.Green), (b, Color.Yellow));
        Denetle("yalnız kimliği bulunan ekran değişince yalnız o, kendi kaydına yazılıyor",
            k.Yazilanlar.Count == 5 && k.Yazilanlar[4].Aygit == bId, Yazimlar(k));

        SahteKabuk iki = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), Foto), new("", SOk, Sinir(1920, 1920, 1080), Foto),
            new(aId, EFail, Rectangle.Empty, Foto), new(bId, EFail, Rectangle.Empty, Foto));
        Uygula(Tuval(2), false, (a, Color.Red), (b, Color.Blue));
        Denetle("iki kimliksiz kayıtta aygıt yolu verilmeden hiç yazılmıyor", iki.Yazilanlar.Count == 0, Yazimlar(iki));

        SahteKabuk eksik = Kabuk(new Kayit("", SOk, Sinir(0, 1920, 1080), Foto), new(aId, EFail, Rectangle.Empty, Foto));
        Uygula(Tuval(2), false, (a, Color.Red), (b, Color.Blue));
        Denetle("öbür ekranın kaydı yoksa aygıt yolu verilmeden yazılmıyor (o ekran geri yazılamazdı)",
            eksik.Yazilanlar.Count == 0, Yazimlar(eksik));
    }

    // ------------------------------------------------------------------- S8

    private static void S8()
    {
        Console.WriteLine("S8) Klon kipi: aynı dikdörtgende iki canlı kayıt, tek ekran");
        int hata = Hatalar.Count;
        string m1 = @"\\?\DISPLAY#AUS2700#1";
        string m2 = @"\\?\DISPLAY#SAM0B5A#1";
        SahteKabuk k = Kabuk(new Kayit(m1, SOk, Sinir(0), Foto), new(m2, SOk, Sinir(0), Foto));
        object ekran = Ekran(m1, 0);
        object tablo = Tablo(k)!;
        Denetle("liste tutarlı, yazım yolu aygıt yolu verilmeden (birden çok canlı kayıt)",
            Saglikli(tablo) && Coz(tablo, ekran, 1) == "Hepsi");

        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("tek yazım, aygıt yolu verilmeden; iki kayıt da görüntüyü gösteriyor",
            k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit is null && k.Kayitlar.All(x => x.Dosya == k.Yazilanlar[0].Yol), Yazimlar(k));
        Denetle("nöbet 'yerinde'; günlüğe hata yazılmadı", BizimMi(tuval) && Hatalar.Count == hata);
    }

    // ------------------------------------------------------------------- S9

    private static void S9()
    {
        Console.WriteLine("S9) Dalgalanma: tek seferlik farklı okuma yeniden yazım istemiyor");
        SahteKabuk k = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        object ekran = Ekran(Aus, 0);
        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        string yol = k.Kayitlar[0].Dosya;

        void Bozuk()
        {
            k.Kayitlar.Clear();
            k.Kayitlar.Add(new("", SOk, Sinir(0), yol));
            k.Kayitlar.Add(new(Aus, EFail, Rectangle.Empty, yol));
        }

        void Saglam()
        {
            k.Kayitlar.Clear();
            k.Kayitlar.Add(new(Aus, SOk, Sinir(0), yol));
        }

        var sonuclar = new List<bool>();
        Bozuk();
        sonuclar.Add(BizimMi(tuval));
        Saglam();
        sonuclar.Add(BizimMi(tuval));
        Bozuk();
        sonuclar.Add(BizimMi(tuval));
        Saglam();
        sonuclar.Add(BizimMi(tuval));
        Denetle("arada bir görülen farklı liste yeniden yazım istemiyor", sonuclar.All(x => x), string.Join(", ", sonuclar));

        Bozuk();
        bool b1 = BizimMi(tuval);
        bool b2 = BizimMi(tuval);
        Denetle("aynı farklılık iki ardışık denetimde görülünce yeniden yazım isteniyor", b1 && !b2, $"{b1} / {b2}");
        Denetle("denetimler yazmıyor (yazımı masaüstü katmanı yapar)", k.Yazilanlar.Count == 1, Yazimlar(k));
    }

    // ------------------------------------------------------------------ S2b

    private static void S2b()
    {
        Console.WriteLine("S2b) 17.10 06:54'te ölçülen liste: aygıt yollu kayıt canlı, boş kimlikli kayıt hayalet; yine aygıt yolu verilmeden");
        int hata = Hatalar.Count;
        string f = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261016-210002-000.bmp");
        SahteKabuk k = Kabuk(new Kayit(Aus, SOk, Sinir(0), f), new("", EFail, Rectangle.Empty, f));
        object ekran = Ekran(Aus, 0);

        // Düzeltmenin ilk hâli (ve 1.14) burada kimlikli tek canlı kaydı seçiyordu:
        // ekranda boş kimlikli kayıt duruyorsa takvim yine ekrana çıkmazdı.
        object tablo = Tablo(k)!;
        Denetle("liste tutarsız; kimlikli tek canlı kayıt olsa da tek ekranda yazım yolu aygıt yolu verilmeden",
            !Saglikli(tablo) && Coz(tablo, ekran, 1) == "Hepsi", Coz(tablo, ekran, 1));

        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        string yazilan = k.Yazilanlar.Count == 1 ? k.Yazilanlar[0].Yol : "?";
        Denetle("tek yazım, aygıt yolu verilmeden; görüntü iki kayda da ulaşıyor (hangisi ekrandaysa)",
            k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit is null && k.Kayitlar.All(x => x.Dosya == yazilan), Yazimlar(k));
        Denetle("nöbet: ekranda Kalender'in görüntüsü var", BizimMi(tuval) && BizimMi(tuval));
        Denetle("günlüğe bir kez hata yazıldı, kayıt tablosuyla",
            Hatalar.Count == hata + 1 && Hatalar[^1].Contains("tutarsız") && Hatalar[^1].Contains("(boş kimlik) Hayalet")
            && Hatalar[^1].Contains("Canli"), Hatalar.Count > hata ? Hatalar[^1] : "hata yok");

        // Canlı ve hayalet yer değiştiriyor (05:06 ile 06:54 arasında böyle oldu):
        // yazım yolu aynı kalıyor, nöbet yeniden yazım istemiyor.
        var sonuclar = new List<bool>();
        for (int i = 0; i < 4; i++)
        {
            (k.Kayitlar[0].Hr, k.Kayitlar[1].Hr) = (k.Kayitlar[1].Hr, k.Kayitlar[0].Hr);
            (k.Kayitlar[0].Sinir, k.Kayitlar[1].Sinir) = (k.Kayitlar[1].Sinir, k.Kayitlar[0].Sinir);
            sonuclar.Add(BizimMi(tuval));
        }

        Denetle("canlı ve hayalet kayıt yer değiştirse de yazım yolu değişmiyor, yeniden yazım istenmiyor",
            sonuclar.All(x => x) && Coz(Tablo(k)!, ekran, 1) == "Hepsi", string.Join(", ", sonuclar));
        Denetle("sağlıklı tek ekranda yine ekranın kendi kaydı (tutarlılık şartı yalnız bozuk listede devreye giriyor)",
            Coz(Tablo(Kabuk(new Kayit(Aus, SOk, Sinir(0), f)))!, ekran, 1) == "Kimlik:" + Aus
            && Coz(Tablo(Kabuk(new Kayit(Aus, SOk, Sinir(0), f), new(@"\\?\DISPLAY#DEL4321#1", SFalse, Rectangle.Empty, Foto)))!, ekran, 1)
                == "Kimlik:" + Aus);
    }

    // ------------------------------------------------------------------ S11

    /// <summary>
    /// Kurucusu çalıştırılmamış masaüstü katmanı: verilen tuvalle, yazma açık,
    /// takvim bir ekranda. Ekran yapılandırması gerçek ekranlardan yalnız
    /// okunarak alınır ki nöbet yeniden kurmaya kalkmasın; katmanın ekran
    /// listesi boş, yeniden çizim hiçbir şey çizmez.
    /// </summary>
    private static object Katman(object tuval, object ekran)
    {
        Type t = Tip("Kalender.Desktop.DesktopLayerManager");
        object y = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
        object depo = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Settings.SettingsStore"));
        AlanAta(depo, "_gate", new object());
        object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;
        Yaz(ayarlar, "SyncSystemWallpaper", true);
        Yaz(depo, "Current", ayarlar);

        var takvimler = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Desktop.EkrandakiTakvim")))!;
        takvimler.Add(Activator.CreateInstance(Tip("Kalender.Desktop.EkrandakiTakvim"), ekran, null));

        AlanAta(y, "_settings", depo);
        AlanAta(y, "_canvas", tuval);
        AlanAta(y, "_duvarKagidiNobeti", Activator.CreateInstance(Tip("Kalender.Desktop.DuvarKagidiNobeti"), true)!);
        AlanAta(y, "_screens", takvimler);
        AlanAta(y, "_monitors", Array.CreateInstance(Tip("Kalender.Interop.MonitorTarget"), 0));
        AlanAta(y, "_bilinenKimlikler", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        AlanAta(y, "_rebuildTimer", new System.Windows.Threading.DispatcherTimer());
        AlanAta(y, "_tazelemeTimer", new System.Windows.Threading.DispatcherTimer());
        AlanAta(y, "_configSignature", Statik(t, "BuildConfigSignature", Cagir(y, "EkranlariOku")));
        return y;
    }

    /// <summary>Katmanın bir duvar kâğıdı denetim turu (Nobet); takvimi yeniden yazdırdıysa (Forget) true.</summary>
    private static bool NobetTuru(object katman)
    {
        object tuval = AlanOku(katman, "_canvas");
        AlanAta(katman, "_nobetSayaci", 3);
        ((IDictionary)AlanOku(tuval, "_hashes"))["iz"] = "iz";
        Cagir(katman, "Nobet");
        return ((IDictionary)AlanOku(tuval, "_hashes")).Count == 0;
    }

    private static string Denetim(object tuval) => Cagir(tuval, "EkraniDenetle")!.ToString()!;

    private static void S11()
    {
        Console.WriteLine("S11) Nöbetin geri çekilmesi: yazılan görüntü ekrana ulaşmazsa denetim seyrekleşiyor, kullanıcıya bir kez haber");
        Type nobetTipi = Tip("Kalender.Desktop.DuvarKagidiNobeti");
        Type sonucTipi = Tip("Kalender.Desktop.EkranDenetimi");
        object n = Activator.CreateInstance(nobetTipi, true)!;
        DateTime t0 = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        bool Isle(string sonuc, DateTime an) => (bool)Cagir(n, "Isle", Enum.Parse(sonucTipi, sonuc), an)!;
        bool Zamani(DateTime an) => (bool)Cagir(n, "DenetimZamani", an)!;
        int ArdArda() => (int)Oku(n, "ArdArda")!;

        bool b1 = Isle("Gorunmuyor", t0), b2 = Isle("Gorunmuyor", t0);
        Denetle("ilk iki uyuşmazlıkta beklenmiyor, haber yok", !b1 && !b2 && Zamani(t0) && ArdArda() == 2);
        Denetle("yol değişimi ve bilinmeyen sonuç sayılmıyor",
            !Isle("YolDegisti", t0) && !Isle("Bilinmiyor", t0) && ArdArda() == 2 && Zamani(t0));
        bool b3 = Isle("Gorunmuyor", t0);
        Denetle("üçüncü uyuşmazlıkta haber; sonraki denetim 5 dakika sonra",
            b3 && !Zamani(t0.AddMinutes(4.9)) && Zamani(t0.AddMinutes(5)));
        DateTime t1 = t0.AddMinutes(5);
        bool b4 = Isle("Gorunmuyor", t1);
        DateTime t2 = t1.AddMinutes(5);
        bool b5 = Isle("Gorunmuyor", t2);
        Denetle("beşinci uyuşmazlıktan sonra 15 dakika; haber bir kez",
            !b4 && !b5 && ArdArda() == 5 && !Zamani(t2.AddMinutes(14.9)) && Zamani(t2.AddMinutes(15)));
        Denetle("en çok 15 dakika", !Isle("Gorunmuyor", t2.AddMinutes(15)) && Zamani(t2.AddMinutes(30)));
        Isle("Yerinde", t2.AddMinutes(30));
        Denetle("ekranda Kalender'in görüntüsü görülünce baştan başlıyor", ArdArda() == 0 && Zamani(t2.AddMinutes(30)));
        Isle("Gorunmuyor", t0);
        Isle("Gorunmuyor", t0);
        Denetle("yeni bozukluk döneminde üçüncü uyuşmazlık yine bildiriliyor", Isle("Gorunmuyor", t0));
        Cagir(n, "Sifirla");
        Denetle("sıfırlanınca hemen denetleniyor", ArdArda() == 0 && Zamani(t0));

        // Tuvalin denetimi sonucu nedeniyle veriyor.
        int hata = Hatalar.Count;
        string f15 = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261015-114200-000.bmp");
        SahteKabuk k = Kabuk(new Kayit("", SOk, Sinir(0), f15), new(Aus, EFail, Rectangle.Empty, f15));
        k.AygitsizBosKaydaUlasmaz = true;
        object ekran = Ekran(Aus, 0);
        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("yazım ekrana ulaşmadı: denetim 'görüntü yok' diyor (yol aynı)", Denetim(tuval) == "Gorunmuyor", Denetim(tuval));

        // Masaüstü katmanının nöbeti (gerçek ekranlar yalnız okunur, duvar kâğıdına yalnız sahte kabukla).
        object katman = Katman(tuval, ekran);
        int haber = 0;
        katman.GetType().GetEvent("DuvarKagidiGuncellenemiyor")!.AddEventHandler(katman, new Action(() => haber++));
        object nobeti = AlanOku(katman, "_duvarKagidiNobeti");
        int hataOnce = Hatalar.Count;
        var turlar = new List<bool> { NobetTuru(katman), NobetTuru(katman), NobetTuru(katman) };
        Denetle("nöbet ilk üç turda yeniden yazdırıyor, üçüncüde bir kez haber ve kayıt tablosuyla bir hata",
            turlar.All(x => x) && haber == 1 && Hatalar.Count == hataOnce + 1 && Hatalar[^1].Contains("art arda 3")
            && Hatalar[^1].Contains("Hayalet"), $"{string.Join(", ", turlar)}; haber {haber}; "
            + (Hatalar.Count > hataOnce ? Hatalar[^1] : "hata yok"));
        Denetle("üçüncü uyuşmazlıktan sonraki tur denetlemiyor, yazdırmıyor (5 dakika bekleniyor)", !NobetTuru(katman));

        AlanAta(nobeti, "_sonraki", DateTime.MinValue);
        Denetle("süre dolunca yine denetleniyor; ikinci kez haber yok, yeni hata yok",
            NobetTuru(katman) && haber == 1 && Hatalar.Count == hataOnce + 1 && (int)Oku(nobeti, "ArdArda")! == 4);

        Cagir(katman, "NobetiSifirla");
        Denetle("katman sıfırlayınca (uyanma, Explorer'ın yeniden başlaması) hemen denetleniyor",
            (int)Oku(nobeti, "ArdArda")! == 0 && NobetTuru(katman));

        // Kabuk düzeldi: aygıt yolu verilmeden yazılan görüntü ekrandaki kayda ulaşıyor.
        k.AygitsizBosKaydaUlasmaz = false;
        k.Kayitlar[0].Dosya = k.Yazilanlar[^1].Yol;
        Denetle("ekranda Kalender'in görüntüsü görülünce yazdırmıyor, sayaç sıfır",
            !NobetTuru(katman) && (int)Oku(nobeti, "ArdArda")! == 0 && haber == 1);

        // Derlenmiş koddan: ekran yapılandırması değişince, uyanınca ve Explorer
        // yeniden başlayınca sıfırlanıyor; haber tepsi balonuyla veriliyor.
        Type katmanTipi = Tip("Kalender.Desktop.DesktopLayerManager");
        Type host = Tip("Kalender.Core.AppHost");
        bool Cagiriyor(Type tip, string yontem, Type hedef, string ad)
            => Komutlar(tip.GetMethod(yontem, Hepsi)!).Any(x => Cagri(x, hedef, ad));
        List<Komut> nobet = Komutlar(katmanTipi.GetMethod("Nobet", Hepsi)!);
        int zamani = Bul(nobet, x => Cagri(x, nobetTipi, "DenetimZamani"));
        int denetle = Bul(nobet, x => Cagri(x, Tip("Kalender.Desktop.WallpaperCanvas"), "EkraniDenetle"));
        Denetle("nöbet denetimden önce geri çekilme süresine bakıyor, sonucu işliyor",
            zamani >= 0 && denetle > zamani && Bul(nobet, x => Cagri(x, nobetTipi, "Isle"), denetle) > denetle);
        Denetle("ekran yapılandırması değişince, uyanınca ve Explorer yeniden başlayınca geri çekilme bırakılıyor",
            Cagiriyor(katmanTipi, "Rebuild", katmanTipi, "NobetiSifirla")
            && Cagiriyor(host, "OnResumed", katmanTipi, "NobetiSifirla")
            && Cagiriyor(host, "OnShellRestarted", katmanTipi, "NobetiSifirla"));
        Denetle("uygulama haberi tepsi balonuyla veriyor",
            Cagiriyor(host, "Start", katmanTipi, "add_DuvarKagidiGuncellenemiyor")
            && Cagiriyor(host, "OnDuvarKagidiGuncellenemiyor", Tip("Kalender.Tray.TrayIcon"), "ShowBalloon"));

        // Explorer yeniden başlayınca: geri çekilme bırakılıyor, görüntü 5 saniye sonra bir kez yeniden yazılıyor.
        object uygulama = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(host);
        AlanAta(uygulama, "_layer", katman);
        Cagir(nobeti, "Isle", Enum.Parse(sonucTipi, "Gorunmuyor"), DateTime.UtcNow);
        Cagir(uygulama, "OnShellRestarted");
        var tazeleme = (System.Windows.Threading.DispatcherTimer)AlanOku(katman, "_tazelemeTimer");
        var kurma = (System.Windows.Threading.DispatcherTimer)AlanOku(katman, "_rebuildTimer");
        Denetle("Explorer yeniden başlayınca geri çekilme bırakılıyor, görüntü 5 saniye sonra bir kez yeniden yazılacak",
            (int)Oku(nobeti, "ArdArda")! == 0 && tazeleme.IsEnabled && tazeleme.Interval == TimeSpan.FromSeconds(5) && kurma.IsEnabled,
            $"{Oku(nobeti, "ArdArda")} / {tazeleme.IsEnabled} {tazeleme.Interval} / {kurma.IsEnabled}");
        tazeleme.Stop();
        kurma.Stop();
        Denetle("günlükte bozuk liste için bir, art arda uyuşmazlık için bir hata satırı",
            Hatalar.Count == hata + 2, string.Join(" | ", Hatalar.Skip(hata)));
    }

    // ------------------------------------------------------------------ S12

    private static void S12()
    {
        Console.WriteLine("S12) Kaydın dosyası okunamazsa ekran 'bilinmiyor': nöbet yeniden yazdırmıyor");
        SahteKabuk k = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        object ekran = Ekran(Aus, 0);
        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("okunabilirken yerinde", Denetim(tuval) == "Yerinde");

        k.OkunamayanKimlikler.Add(Aus);
        Denetle("sağlıklı tek ekranda kaydın dosyası okunamayınca bilinmiyor, yeniden yazım istenmiyor",
            Denetim(tuval) == "Bilinmiyor" && BizimMi(tuval), Denetim(tuval));
        Denetle("kapanışta ekranda duruyor sayılmıyor (emin olunamayan yere yazılmaz)", Duranlar(tuval).Count == 0);
        k.OkunamayanKimlikler.Clear();
        k.OkumadaHata = true;
        Denetle("hiçbir dosya okunamayınca da bilinmiyor", Denetim(tuval) == "Bilinmiyor");
        k.OkumadaHata = false;
        Denetle("okuma düzelince yerinde", Denetim(tuval) == "Yerinde");

        // Aygıt yolu verilmeden yazılan ekran (bozuk liste): canlı kaydın dosyası okunamıyor.
        SahteKabuk bozuk = Kabuk(new Kayit("", SOk, Sinir(0), Foto), new(Aus, EFail, Rectangle.Empty, Foto));
        object tuval2 = Tuval(1);
        Uygula(tuval2, false, (ekran, Color.Navy));
        bozuk.OkunamayanKimlikler.Add("");
        Denetle("aygıt yolu verilmeden yazılan ekranda canlı kaydın dosyası okunamayınca bilinmiyor",
            Denetim(tuval2) == "Bilinmiyor", Denetim(tuval2));
        bozuk.OkunamayanKimlikler.Clear();
        bozuk.OkunamayanKimlikler.Add(Aus);
        Denetle("yalnız hayaletin dosyası okunamıyorsa yine yerinde (hayalete bakılmıyor)", Denetim(tuval2) == "Yerinde");
    }

    // ------------------------------------------------------------------ S13

    private static void S13()
    {
        Console.WriteLine("S13) Çok ekranda yazım anında bağlı olmayan ekran: atlanıyor, kaydı canlı olunca nöbet yeniden yazdırıyor");
        int hata = Hatalar.Count;
        string aId = @"\\?\DISPLAY#AAA0001#1";
        string bId = @"\\?\DISPLAY#BBB0002#1";
        object a = Ekran(aId, 0, 1920, 1080);
        object b = Ekran(bId, 1920, 1920, 1080);
        string dunku = Path.Combine(ZeminKlasoru, "zemin-bbbbbbbb-20261016-090000-000.bmp");

        // Dizüstüne takılan haricî ekran: Windows onun kaydını bir an bağlı saymıyor (S_FALSE).
        SahteKabuk k = Kabuk(new Kayit(aId, SOk, Sinir(0, 1920, 1080), Foto), new(bId, SFalse, Rectangle.Empty, dunku));
        object tuval = Tuval(2);
        Uygula(tuval, false, (a, Color.Red), (b, Color.Blue));
        Denetle("yalnız bağlı ekran yazılıyor; bağlı olmayan ekran atlanıyor",
            k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit == aId && k.Kayitlar[1].Dosya == dunku, Yazimlar(k));
        Denetle("ekran bağlı olmadıkça nöbet yeniden yazım istemiyor (döngü yok)",
            BizimMi(tuval) && BizimMi(tuval) && BizimMi(tuval));
        Denetle("kapanışta atlanan ekran duruyor sayılmıyor", Duranlar(tuval).SetEquals(new[] { aId }));

        k.Kayitlar[1].Hr = SOk;
        k.Kayitlar[1].Sinir = Sinir(1920, 1920, 1080);
        string d1 = Denetim(tuval), d2 = Denetim(tuval);
        Denetle("kaydı canlı olunca iki ardışık denetimden sonra yeniden yazım isteniyor (yol değişti)",
            d1 == "Bilinmiyor" && d2 == "YolDegisti", $"{d1} / {d2}");
        Unut(tuval);
        Uygula(tuval, false, (a, Color.Red), (b, Color.Blue));
        Denetle("yeniden yazımda haricî ekran kendi kaydına; dünkü takvim kalmıyor",
            k.Yazilanlar.Count == 3 && k.Yazilanlar[2].Aygit == bId && k.Kayitlar[1].Dosya == k.Yazilanlar[2].Yol && BizimMi(tuval),
            Yazimlar(k));

        // İki kimliksiz ekran (aygıt yolu verilmeden yazılamıyor): atlanıyorlar, nöbet döngüye girmiyor.
        SahteKabuk iki = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), Foto), new("", SOk, Sinir(1920, 1920, 1080), Foto),
            new(aId, EFail, Rectangle.Empty, Foto), new(bId, EFail, Rectangle.Empty, Foto));
        object tuval2 = Tuval(2);
        Uygula(tuval2, false, (a, Color.Red), (b, Color.Blue));
        Denetle("iki kimliksiz ekran atlanınca nöbet her denetimde yeniden yazım istemiyor",
            iki.Yazilanlar.Count == 0 && BizimMi(tuval2) && BizimMi(tuval2) && BizimMi(tuval2) && Duranlar(tuval2).Count == 0);
        Denetle("sağlıklı listede atlanan ekran için günlüğe hata yazılmadı (bozuk liste için bir)", Hatalar.Count == hata + 1,
            string.Join(" | ", Hatalar.Skip(hata)));
    }

    // ------------------------------------------------------------------ S14

    private static void S14()
    {
        Console.WriteLine("S14) Temizlik: yalnız hayaletin dosyası okunamıyorsa kullanılmayan dosyalar yine siliniyor");
        Type servis = Tip("Kalender.Desktop.WallpaperService");
        string f15 = ZeminDosyasi("zemin-dddddddd-20261015-114200-000.bmp");
        string f16 = ZeminDosyasi("zemin-dddddddd-20261017-000002-000.bmp");
        string f0 = ZeminDosyasi("zemin-dddddddd-20261014-090000-000.bmp");
        SahteKabuk k = Kabuk(new Kayit("", SOk, Sinir(0), f15), new(Aus, EFail, Rectangle.Empty, f16));
        k.OkunamayanKimlikler.Add(Aus);
        var dosyalar = (HashSet<string>?)Statik(servis, "EkrandakiDosyalar", k);
        Denetle("ekrandaki dosyalar: hayaletin okunamayan dosyası atlanıyor, canlı kaydınki var",
            dosyalar is not null && dosyalar.Contains(f15) && !dosyalar.Contains(f16));

        object tuval = Tuval(1);
        int hata = Hatalar.Count;
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        Denetle("canlı kaydın dosyası kalıyor, kullanılmayan eski dosya siliniyor; günlüğe hata yok",
            File.Exists(f15) && !File.Exists(f0) && Hatalar.Count == hata);

        f0 = ZeminDosyasi("zemin-dddddddd-20261014-090000-000.bmp");
        k.OkunamayanKimlikler.Clear();
        k.OkunamayanKimlikler.Add("");
        Denetle("canlı kaydın dosyası okunamazsa ekrandakiler bilinmiyor", Statik(servis, "EkrandakiDosyalar", k) is null);
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        Denetle("o zaman hiçbir dosya silinmiyor; günlüğe bir kez hata",
            File.Exists(f15) && File.Exists(f0) && Hatalar.Count == hata + 1 && Hatalar[^1].Contains("silinemiyor"),
            string.Join(" | ", Hatalar.Skip(hata)));

        SahteKabuk bagliDegil = Kabuk(new Kayit(Aus, SOk, Sinir(0), f15), new(@"\\?\DISPLAY#DEL4321#1", SFalse, Rectangle.Empty, f16));
        bagliDegil.OkunamayanKimlikler.Add(@"\\?\DISPLAY#DEL4321#1");
        Denetle("bağlı olmayan ekranın dosyası okunamazsa da bilinmiyor (o ekran takılınca dosyasını gösterecek)",
            Statik(servis, "EkrandakiDosyalar", bagliDegil) is null);

        FabrikayiBagla(() => k);
        k.OkunamayanKimlikler.Clear();
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        Denetle("okunabilince yine siliniyor, hata bayrağı iniyor", !File.Exists(f0) && !(bool)AlanOku(tuval, "_temizlikOkunamadiYazildi"));
    }

    // ---------------------------------------------------------------- ölçüm

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr baglam);

    /// <summary>
    /// Windows'un gerçek listesini yalnız okur: uygulamanın okuma ve seçimi
    /// (Oku, Saglikli, Coz, Gorunen) gerçek arayüzde, salt okunur sarmalayıcıyla.
    /// </summary>
    private static int GercekOku(Delegate gercekFabrika)
    {
        Console.WriteLine("Windows'un duvar kâğıdı listesi (gerçek, yalnız okunuyor)");

        // Uygulama gibi ekran başına DPI farkında (koordinatlar fiziksel piksel).
        SetProcessDpiAwarenessContext(new IntPtr(-4));

        var salt = (SaltOkunurKabuk)DispatchProxy.Create(Tip("Kalender.Interop.IDesktopWallpaper"), typeof(SaltOkunurKabuk));
        salt.Gercek = gercekFabrika.DynamicInvoke()!;
        FabrikayiBagla(() => salt);

        object? tablo = Statik(Kayitlar, "Oku", salt);
        if (tablo is null)
        {
            Console.WriteLine("   Liste okunamadı.");
            return 1;
        }

        foreach (object x in Satirlar(tablo))
        {
            var r = (Rectangle)Oku(x, "Sinir")!;
            string kimlik = (string)Oku(x, "Kimlik")!;
            string? dosya = (string?)Oku(x, "Dosya");
            Console.WriteLine($"   [{Oku(x, "Sira")}] {(kimlik.Length == 0 ? "(boş kimlik)" : kimlik)}");
            Console.WriteLine($"       durum {Oku(x, "Durum")}, GetMonitorRECT 0x{(int)Oku(x, "RectHr")!:X8}, "
                + $"{r.X},{r.Y} {r.Width}x{r.Height}, dosya {(dosya is null ? "(okunamadı)" : dosya.Length == 0 ? "(boş)" : Path.GetFileName(dosya))}");
        }

        string? ortak = (string?)Oku(tablo, "Ortak");
        Console.WriteLine($"   aygıt yolu verilmeden: {(ortak is null ? "(okunamadı)" : ortak.Length == 0 ? "(boş)" : Path.GetFileName(ortak))}");
        Console.WriteLine($"   liste tutarlı: {(Saglikli(tablo) ? "evet" : "hayır")}");

        var ekranlar = (IList)Statik(Tip("Kalender.Interop.MonitorEnumerator"), "All")!;
        Console.WriteLine($"   bağlı ekran: {ekranlar.Count}");
        foreach (object ekran in ekranlar)
        {
            object h = Statik(Kayitlar, "Coz", tablo, ekran, ekranlar.Count)!;
            string? gorunen = (string?)Statik(Kayitlar, "Gorunen", tablo, h, ekran);
            Console.WriteLine($"   ekran {ekran}");
            Console.WriteLine($"       kimlik {Kimlik(ekran)}");
            Console.WriteLine($"       seçilen yazım yolu: {Oku(h, "Tur")}{(Oku(h, "Kimlik") is string id ? " " + id : string.Empty)}; "
                + $"ekranda: {(gorunen is null ? "(bilinmiyor)" : gorunen.Length == 0 ? "(boş)" : Path.GetFileName(gorunen))}");
        }

        Console.WriteLine($"   özet: {Statik(Kayitlar, "Ozet", tablo)}");
        Console.WriteLine($"   arayüz çağrıları: {string.Join(", ", salt.Cagrilar.GroupBy(c => c).Select(g => $"{g.Key} ×{g.Count()}"))}; "
            + $"engellenen yazma çağrısı: {salt.Engellenen}");
        return salt.Engellenen == 0 ? 0 : 1;
    }
}

/// <summary>Sahte kabuğun duvar kâğıdı listesindeki bir kayıt.</summary>
public sealed class Kayit
{
    public Kayit(string kimlik, int hr, Rectangle sinir, string dosya)
    {
        Kimlik = kimlik;
        Hr = hr;
        Sinir = sinir;
        Dosya = dosya;
    }

    /// <summary>Aygıt yolu; boş olabilir.</summary>
    public string Kimlik { get; set; }

    /// <summary>GetMonitorRECT'in sonucu: S_OK, S_FALSE ya da hata.</summary>
    public int Hr { get; set; }

    public Rectangle Sinir { get; set; }

    /// <summary>Kaydın gösterdiği dosya.</summary>
    public string Dosya { get; set; }

    public bool Canli => Hr == 0 && Sinir.Width > 0 && Sinir.Height > 0;
}

/// <summary>
/// Windows'un duvar kâğıdı arayüzünün (IDesktopWallpaper) sahtesi; Explorer'ın
/// listesi bozuk da olabilir: kimliği boş kayıt, dikdörtgen sorusu hata veren
/// (hayalet) ya da S_FALSE veren (bağlı olmayan) kayıt. Hayalete yazım hatasız
/// kabul edilir (gerçekte ekrana çıkmıyor). Aygıt yolu verilmeden yazım bütün
/// kayıtlara gider (2026-10-17'de ölçüldü: bozuk listede de ekrana ulaştı);
/// istenirse kimliği boş kayda ulaşmaz (en kötü durum, ölçülmedi).
/// </summary>
public class SahteKabuk : DispatchProxy
{
    private const int EFail = unchecked((int)0x80004005);
    private const int EInvalidArg = unchecked((int)0x80070057);

    public List<Kayit> Kayitlar { get; } = new();

    /// <summary>
    /// Aygıt yolu verilmeden okunan dosya: doğruysa ilk canlı kaydınki
    /// (2026-10-17'de ölçülen), değilse <see cref="Ortak"/>.
    /// </summary>
    public bool AygitsizIlkCanliyiVerir { get; set; } = true;

    public string Ortak { get; set; } = string.Empty;

    /// <summary>Doğruysa aygıt yolu verilmeden yazılan görüntü kimliği boş kayda ulaşmıyor.</summary>
    public bool AygitsizBosKaydaUlasmaz { get; set; }

    /// <summary>Doğruysa kayıt sayısı okunamıyor.</summary>
    public bool SayimdaHata { get; set; }

    /// <summary>Doğruysa GetWallpaper hata verir.</summary>
    public bool OkumadaHata { get; set; }

    /// <summary>Bu kimliklerde GetWallpaper hata verir (yalnız o kayıt okunamıyor; boş kimlik de olur).</summary>
    public HashSet<string> OkunamayanKimlikler { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SetWallpaper çağrıları, sırasıyla.</summary>
    public List<(string? Aygit, string Yol)> Yazilanlar { get; } = new();

    public int Konum { get; set; } = 4;

    public uint Renk { get; set; }

    private Kayit? Bul(string kimlik)
        => Kayitlar.FirstOrDefault(k => string.Equals(k.Kimlik, kimlik, StringComparison.OrdinalIgnoreCase));

    protected override object? Invoke(MethodInfo? yontem, object?[]? a)
    {
        switch (yontem!.Name)
        {
            case "GetMonitorDevicePathCount":
                if (SayimdaHata)
                {
                    throw new COMException("Liste okunamadı (deneme).", EFail);
                }

                return (uint)Kayitlar.Count;

            case "GetMonitorDevicePathAt":
                return Kayitlar[(int)(uint)a![0]!].Kimlik;

            case "GetMonitorRECT":
                Kayit? kayit = Bul((string)a![0]!);
                a[1] = Dikdortgen(yontem.GetParameters()[1].ParameterType.GetElementType()!,
                    kayit is null || kayit.Hr < 0 ? Rectangle.Empty : kayit.Sinir);
                return kayit?.Hr ?? EInvalidArg;

            case "GetWallpaper":
                if (OkumadaHata)
                {
                    throw new COMException("Duvar kâğıdı okunamadı (deneme).", EFail);
                }

                if (a![0] is string kimlik)
                {
                    if (OkunamayanKimlikler.Contains(kimlik))
                    {
                        throw new COMException("Kaydın duvar kâğıdı okunamadı (deneme).", EFail);
                    }

                    return Bul(kimlik)?.Dosya ?? string.Empty;
                }

                return AygitsizIlkCanliyiVerir ? Kayitlar.FirstOrDefault(k => k.Canli)?.Dosya ?? string.Empty : Ortak;

            case "SetWallpaper":
                string yol = (string)a![1]!;
                Yazilanlar.Add(((string?)a[0], yol));
                if (a[0] is string hedef)
                {
                    // Hayalete de hatasız yazılır; ekrana çıkmaz.
                    (Bul(hedef) ?? throw new COMException("Bilinmeyen aygıt yolu (deneme).", EInvalidArg)).Dosya = yol;
                }
                else
                {
                    Ortak = yol;
                    foreach (Kayit k in Kayitlar.Where(k => !(AygitsizBosKaydaUlasmaz && k.Kimlik.Length == 0)))
                    {
                        k.Dosya = yol;
                    }
                }

                return null;

            case "GetBackgroundColor":
                return Renk;

            case "SetBackgroundColor":
                Renk = (uint)a![0]!;
                return null;

            case "GetPosition":
                return Enum.ToObject(yontem.ReturnType, Konum);

            case "SetPosition":
                Konum = Convert.ToInt32(a![0]);
                return null;

            case "Enable":
                return null;

            default:
                throw new NotSupportedException(yontem.Name);
        }
    }

    /// <summary>Uygulamanın RECT yapısı (NativeMethods.RECT), kutulu.</summary>
    private static object Dikdortgen(Type tip, Rectangle r)
    {
        object kutu = Activator.CreateInstance(tip)!;
        tip.GetField("Left")!.SetValue(kutu, r.Left);
        tip.GetField("Top")!.SetValue(kutu, r.Top);
        tip.GetField("Right")!.SetValue(kutu, r.Right);
        tip.GetField("Bottom")!.SetValue(kutu, r.Bottom);
        return kutu;
    }
}

/// <summary>
/// Windows'un gerçek duvar kâğıdı arayüzünün salt okunur sarmalayıcısı: Get*
/// çağrılarını gerçeğine iletir; yazan her çağrı (Set*, Enable,
/// AdvanceSlideshow) hata verir ve gerçeğine hiç ulaşmaz.
/// </summary>
public class SaltOkunurKabuk : DispatchProxy
{
    /// <summary>Gerçek arayüz (COM nesnesi).</summary>
    public object? Gercek { get; set; }

    /// <summary>Engellenen yazma çağrılarının sayısı.</summary>
    public int Engellenen { get; private set; }

    /// <summary>Gerçeğine iletilen çağrılar, sırasıyla.</summary>
    public List<string> Cagrilar { get; } = new();

    protected override object? Invoke(MethodInfo? yontem, object?[]? a)
    {
        string ad = yontem!.Name;
        if (ad.StartsWith("Set", StringComparison.Ordinal) || ad is "Enable" or "AdvanceSlideshow")
        {
            Engellenen++;
            throw new InvalidOperationException($"Salt okunur ölçüm: {ad} çağrılmaz.");
        }

        Cagrilar.Add(ad);
        try
        {
            return yontem.Invoke(Gercek, a);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}
