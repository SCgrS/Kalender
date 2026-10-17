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
using System.Text.Json;

// Explorer'ın duvar kâğıdı listesi bozulunca Kalender'in hangi kayda yazdığının
// sınaması (yansımayla). 2026-10-17'de ev bilgisayarında tek ekranın listesinde
// iki kayıt vardı: ekranda görünen kaydın kimliği boş dizeydi, ekranın aygıt
// yolunu taşıyan kayıt hiçbir ekrana karşılık gelmiyordu (dikdörtgen sorusu
// E_FAIL). 1.14 görüntüyü bu hayalet kayda yazıyordu; ekranda iki gün önceki
// takvim kaldı. Senaryolar kalender.md'deki düzeltme tasarımından:
//   0. Kayıtların sınıflanması (canlı, bağlı değil, hayalet), listenin
//      tutarlılığı, günlük özeti; okuma hataları; dikdörtgen sorusu Explorer'a
//      ulaşamazsa (istisna, RPC kodu) liste bilinmiyor, hayalet sayılmıyor.
//   S1. Sağlıklı tek ekran: görüntü ekranın kendi kaydına yazılır, nöbet
//       "yerinde" der; kimliği okunamamış ekranda da; çıkarılmış haricî ekranın
//       kaydı (S_FALSE) listeyi bozuk saydırmaz.
//   S2. Bugünkü durum (boş kimlikli canlı kayıt + hayalet): yalnız aygıt yolu
//       verilmeden yazılır, görüntü ekranda; günlüğe bir kez hata, sonraki
//       yazımda yinelenmez; hayaletteki takvim ekranda sayılmaz.
//   S3. 1.14'ün bıraktığı durum (son yazım hayalete): nöbet iki ardışık
//       denetimden sonra yeniden yazdırır, aygıt yolu verilmeden; liste
//       düzelince görüntü kendi kaydında da duruyorsa yeniden yazılmaz, yoksa
//       kendi kaydına yazılır; günlüğe bir kez "düzeldi".
//   S4. Temizlik: ekranda duran dosyalar (boş kimlikli kayıt, hayalet) silinmez,
//       kullanılmayan eski dosya silinir; liste okunamazsa hiçbiri silinmez;
//       yazımdan sonraki temizlik de ekrana bakar.
//   S5. Dizüstü + çıkarılmış haricî ekran (S_FALSE): aygıt yolu verilmeden hiç
//       yazılmaz, günlüğe hata yazılmaz, yedekte haricî ekranın fotoğrafı durur
//       ve geri yüklemede kendi kaydına döner.
//   S6. İki ekrandan yalnız biri yazılırken (takvim kaldırılırken) aygıt yolu
//       verilmeden yazılmaz; katman bağlı ekran sayısını her yazımdan önce verir.
//   S7. Çok ekranda tek kimliksiz kayıt: önce o ekran aygıt yolu verilmeden,
//       sonra öbür ekranlar kendi kayıtlarına; iki kimliksiz ekranda ya da öbür
//       ekranın kaydı yokken aygıt yolu verilmeden hiç yazılmaz.
//   S8. Klon kipi (aynı dikdörtgende iki canlı kayıt, tek ekran): aygıt yolu
//       verilmeden yazılır, günlüğe hata yazılmaz.
//   S9. Dalgalanma: tek seferlik farklı okuma yeniden yazım istemez.
//   S10. Bozuk listede yedek ve geri yükleme: yedekte boş anahtar ve hayaletin
//        kimliği yok; geri yükleme tek ekranda aygıt yolu verilmeden (canlı kayıt
//        olmasa da), çok ekranda kimliği dolu canlı kayıtlara, tek kimliksiz
//        ekran önce aygıt yolu verilmeden; iki bağlı ekranda tek canlı kayıt
//        varken aygıt yolu verilmeden yazılmaz; hayalet ve bağlı olmayan kayıtlar
//        kendi fotoğraflarını alır, başarı sayılmaz; eski yedeğin boş anahtarı
//        ortak yol gibi okunur; açılıştaki "ekranda takvim kalmış mı" sorusu
//        hayalete bakmaz; listede canlı kayıt yoksa hayaletin fotoğrafı
//        yedeklenir, ekranda ne durduğu okunamıyorsa düz renk yedeği yazılmaz.
//   S2b. 17.10 06:54'te ölçülen liste (aygıt yollu kayıt canlı, boş kimlikli
//        kayıt hayalet; roller önceki ölçümün tersi): liste tutarsız olduğu için
//        yine aygıt yolu verilmeden; roller yer değiştirse de yol değişmez.
//   S11. Nöbetin geri çekilmesi: yazılan görüntü ekrana ulaşmazsa art arda üç
//        uyuşmazlıktan sonra 5, beşten sonra 15 dakika; kullanıcıya bir kez
//        haber (sonraki bozukluk döneminde de yeniden haber yok); uyanma,
//        Explorer'ın yeniden başlaması ve yapılandırma değişimi sıfırlar.
//   S12. Kaydın dosyası okunamazsa ekran "bilinmiyor": yeniden yazım istenmez.
//   S13. Çok ekranda yazım anında bağlı olmayan ekran atlanır, kaydı canlı olunca
//        nöbet yeniden yazdırır; atlanan ekran döngüye sokmaz.
//   S14. Temizlik: yalnız hayaletin dosyası okunamıyorsa eski dosyalar yine
//        silinir; canlı kayıt okunamazsa hiçbiri, günlüğe bir kez hata; bağlı
//        olmayan ekranın dosyası okunamazsa temizlik durmaz, o ekranın son
//        görüntüsü korunur.
//   S15. Canlı kayıt yokken nöbet aygıt yolu verilmeden okunan dosyaya bakar.
//   S16. Geri yükleme eksik kalınca takvimin durduğu ekranlar bütün bağlı
//        ekranlarla aranır.
//   S17. Liste iki hâl arasında gidip gelirken yeniden yazım sınırlı: görüntü
//        yeni yoldan da görünüyorsa ya da ekranın kaydı bulunamıyorsa yazılmaz;
//        hiç görünmüyorsa yol değişimi yazımları da geri çekilmeye girer.
//   S18. Bir kaydın kimliği okunamazsa liste bilinmiyor (hayalet sayılmaz):
//        yedek o tur alınmaz, dosya silinmez, nöbet yeniden yazdırmaz.
//   S19. Derlenmiş koddan: duvar kâğıdı arayüzünün bildirimi (yöntem sırası,
//        GetMonitorRECT'te PreserveSig) ve arayüzün yalnız fabrikada açılması.
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

    /// <summary>
    /// Bir IL komutu: işlem kodu, varsa çağrılan yöntem ya da alan, yüklenen
    /// sabit dize ve başvurulan tür ya da üye (ldtoken, castclass gibi).
    /// </summary>
    private sealed record Komut(OpCode Kod, MemberInfo? Uye, string? Dize = null, MemberInfo? Belirtec = null);

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
            string? dize = null;
            MemberInfo? belirtec = null;
            try
            {
                uye = kod.OperandType switch
                {
                    OperandType.InlineMethod => yontem.Module.ResolveMethod(BitConverter.ToInt32(il, i)),
                    OperandType.InlineField => yontem.Module.ResolveField(BitConverter.ToInt32(il, i)),
                    _ => null,
                };
                if (kod.OperandType == OperandType.InlineString)
                {
                    dize = yontem.Module.ResolveString(BitConverter.ToInt32(il, i));
                }

                if (kod.OperandType is OperandType.InlineType or OperandType.InlineTok)
                {
                    belirtec = yontem.Module.ResolveMember(BitConverter.ToInt32(il, i));
                }
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
            sonuc.Add(new Komut(kod, uye, dize, belirtec));
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

    private static Type Servis => Tip("Kalender.Desktop.WallpaperService");

    private static string YedekDosyasi => (string)Tip("Kalender.Core.Paths").GetProperty("BackupFile")!.GetValue(null)!;

    /// <summary>
    /// Deneme klasöründe bir fotoğraf dosyası: geri yükleme dosyası olmayan
    /// yolu denemiyor.
    /// </summary>
    private static string Fotograf(string ad)
    {
        string klasor = Path.Combine((string)Tip("Kalender.Core.Paths").GetProperty("RootFolder")!.GetValue(null)!, "fotoğraflar");
        Directory.CreateDirectory(klasor);
        string yol = Path.Combine(klasor, ad);
        File.WriteAllText(yol, "deneme");
        return yol;
    }

    /// <summary>Asıl ve bekleyen yedeği siler: bir sonraki yazım yeni yedek alsın.</summary>
    private static void YedekleriSil()
    {
        File.Delete(YedekDosyasi);
        File.Delete((string)Servis.GetProperty("BekleyenYedekDosyasi", Hepsi)!.GetValue(null)!);
    }

    /// <summary>Yazımdan önce alınan yedek (deneme klasöründe); yoksa null.</summary>
    private static object? YedekOku()
        => File.Exists(YedekDosyasi)
            ? JsonSerializer.Deserialize(File.ReadAllText(YedekDosyasi), Tip("Kalender.Desktop.WallpaperBackup"))
            : null;

    /// <summary>Yedek: ortak yol ve ekran başına yollar.</summary>
    private static object Yedek(string? ortak, params (string Aygit, string Yol)[] ekranlar)
    {
        object b = Activator.CreateInstance(Tip("Kalender.Desktop.WallpaperBackup"), true)!;
        Yaz(b, "Wallpaper", ortak);
        Yaz(b, "Position", 4);
        foreach ((string aygit, string yol) in ekranlar)
        {
            EkranBasina(b)[aygit] = yol;
        }

        return b;
    }

    private static IDictionary<string, string> EkranBasina(object yedek) => (IDictionary<string, string>)Oku(yedek, "PerMonitor")!;

    /// <summary>
    /// Yedeği ekranlara geri koyar (EkranlariGeriYukle; bağlı ekran sayısı
    /// verilerek); her ekran geri yüklendiyse true.
    /// </summary>
    private static bool GeriKoy(SahteKabuk k, object yedek, out int konan, int bagli = 1)
    {
        object?[] a = { k, yedek, bagli, 0 };
        bool hepsi = (bool)Servis.GetMethod("EkranlariGeriYukle", Hepsi)!.Invoke(null, a)!;
        konan = (int)a[3]!;
        return hepsi;
    }

    /// <summary>Açılıştaki ve kaldırmadaki soru: ekranda takvimin zemini var mı (EkrandaZeminVar)?</summary>
    private static bool? ZeminVar(SahteKabuk k) => (bool?)Statik(Servis, "EkrandaZeminVar", k);

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
            Bolum(S5);
            Bolum(S6);
            Bolum(S7);
            Bolum(S8);
            Bolum(S9);
            Bolum(S10);
            Bolum(S2b);
            Bolum(S11);
            Bolum(S12);
            Bolum(S13);
            Bolum(S14);
            Bolum(S15);
            Bolum(S16);
            Bolum(S17);
            Bolum(S18);
            Bolum(S19);
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

        // Dikdörtgen sorusu Explorer'a ulaşamazsa (istisna: aktarım hatası; RPC
        // kodları: Explorer çağrının ortasında kapandı ya da yeniden başlıyor)
        // kayıt hayalet sayılmıyor, liste bilinmiyor. Explorer'ın kendi E_FAIL'i hayalet.
        SahteKabuk u = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        u.AygitsizIlkCanliyiVerir = false;
        u.DikdortgeniSorulamayanlar.Add(Aus);
        Denetle("dikdörtgen sorusu istisna verirse kayıt hayalet sayılmıyor, liste bilinmiyor (null)", Tablo(u) is null);
        YedekleriSil();
        Statik(Servis, "EnsureBackup", u);
        Denetle("o tur yedek alınmıyor (tek kayıt hayalet sanılıp düz renk yedeği yazılmıyor)", YedekOku() is null);
        u.DikdortgeniSorulamayanlar.Clear();
        bool Bilinmiyor(int hr)
        {
            u.Kayitlar[0].Hr = hr;
            return Tablo(u) is null;
        }

        Denetle("RPC kodları da bilinmiyor (RPC_E_DISCONNECTED, RPC_S_SERVER_UNAVAILABLE, RPC_S_CALL_FAILED, RPC_S_CALL_FAILED_DNE)",
            Bilinmiyor(unchecked((int)0x80010108)) && Bilinmiyor(unchecked((int)0x8001010A)) && Bilinmiyor(unchecked((int)0x800706BA))
            && Bilinmiyor(unchecked((int)0x800706BE)) && Bilinmiyor(unchecked((int)0x800706BF)));
        Denetle("Explorer'ın kendi hatası (E_FAIL, E_INVALIDARG) hayalet",
            !Bilinmiyor(EFail) && Oku(Satirlar(Tablo(u)!)[0], "Durum")!.ToString() == "Hayalet"
            && !Bilinmiyor(unchecked((int)0x80070057)) && Oku(Satirlar(Tablo(u)!)[0], "Durum")!.ToString() == "Hayalet");
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

        // Liste düzeldi (oturum kapatılıp açıldı): tek kayıt, ekranın aygıt yoluyla. Aygıt
        // yolu verilmeden yazılan görüntü bütün kayıtlara gitti; ekranın kendi kaydında da bizimki.
        string ekrandaki = k.Kayitlar[0].Dosya;
        k.Kayitlar.Clear();
        k.Kayitlar.Add(new(Aus, SOk, Sinir(0), ekrandaki));
        string d1 = Denetim(tuval), d2 = Denetim(tuval);
        Denetle("liste düzelince görüntü ekranın kendi kaydında da duruyorsa yeniden yazılmıyor, yeni yol benimseniyor",
            d1 == "Bilinmiyor" && d2 == "Yerinde" && Denetim(tuval) == "Yerinde" && k.Yazilanlar.Count == 1, $"{d1} / {d2}");
        Uygula(tuval, false, (ekran, Color.Teal));
        Denetle("sonraki yazım ekranın kendi kaydına",
            k.Yazilanlar.Count == 2 && k.Yazilanlar[1].Aygit == Aus && BizimMi(tuval), Yazimlar(k));
        Denetle("günlükte bozukluk için bir, düzelme için bir hata satırı",
            Hatalar.Count == hata + 2 && Hatalar[^2].Contains("tutarsız") && Hatalar[^1].Contains("düzeldi"),
            string.Join(" | ", Hatalar.Skip(hata)));

        // Aygıt yolu verilmeden yazılan görüntü hayalete ulaşmadıysa (ölçülmedi): liste
        // düzelince ekranın kendi kaydında eski gün; iki ardışık denetimden sonra yeniden yazılıyor.
        int hata2 = Hatalar.Count;
        SahteKabuk k2 = Kabuk(new Kayit("", SOk, Sinir(0), f15), new(Aus, EFail, Rectangle.Empty, f16));
        object tuval2 = Tuval(1);
        Uygula(tuval2, false, (ekran, Color.Navy));
        k2.Kayitlar.Clear();
        k2.Kayitlar.Add(new(Aus, SOk, Sinir(0), f16));
        string e1 = Denetim(tuval2), e2 = Denetim(tuval2);
        Denetle("liste düzelince kendi kaydında başka görüntü varsa iki ardışık denetimden sonra yeniden yazım isteniyor",
            e1 == "Bilinmiyor" && e2 == "YolDegisti", $"{e1} / {e2}");
        Unut(tuval2);
        Uygula(tuval2, false, (ekran, Color.Navy));
        Denetle("yeniden yazım ekranın kendi kaydına; günlüğe bir kez 'düzeldi'",
            k2.Yazilanlar.Count == 2 && k2.Yazilanlar[0].Aygit is null && k2.Yazilanlar[1].Aygit == Aus && BizimMi(tuval2)
            && Hatalar.Count == hata2 + 2 && Hatalar[^1].Contains("düzeldi"), Yazimlar(k2));
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

    // ------------------------------------------------------------------- S5

    private static void S5()
    {
        Console.WriteLine("S5) Dizüstü + çıkarılmış haricî ekran: aygıt yolu verilmeden yazılmıyor, yedekte haricî ekranın fotoğrafı duruyor");
        int hata = Hatalar.Count;
        string harici = @"\\?\DISPLAY#DEL4321#1";
        string dizustuFoto = Fotograf("dizüstü.jpg");
        string hariciFoto = Fotograf("haricî.jpg");
        SahteKabuk k = Kabuk(new Kayit(Aus, SOk, Sinir(0), dizustuFoto), new(harici, SFalse, Rectangle.Empty, hariciFoto));
        object ekran = Ekran(Aus, 0);

        // Yazma ilk kez açılıyor: yazımdan önce yedek alınır.
        YedekleriSil();
        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        Denetle("tek yazım, ekranın kendi kaydına; aygıt yolu verilmeden hiç yazılmıyor, haricî ekranın fotoğrafı yerinde",
            k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit == Aus && k.Kayitlar[1].Dosya == hariciFoto, Yazimlar(k));

        object? yedek = YedekOku();
        Denetle("yedekte iki ekranın fotoğrafı da var (bağlı olmayan haricî ekranınki dâhil)",
            yedek is not null && EkranBasina(yedek).Count == 2 && EkranBasina(yedek)[Aus] == dizustuFoto
            && EkranBasina(yedek)[harici] == hariciFoto && (bool)Oku(yedek, "WallpaperEnabled")!,
            yedek is null ? "yedek yok" : string.Join(", ", EkranBasina(yedek).Select(p => $"{p.Key} = {Path.GetFileName(p.Value)}")));
        Denetle("nöbet 'yerinde'; günlüğe hata yazılmadı", Denetim(tuval) == "Yerinde" && Hatalar.Count == hata);

        // Ekranda takvimin zemini var mı (açılışta ve kaldırmada sorulan): bağlı
        // olmayan ekranın kaydındaki takvim de sayılır; o ekran takılınca gösterecek.
        string takvim = k.Kayitlar[0].Dosya;
        Denetle("dizüstündeki takvim sayılıyor", ZeminVar(k) == true);
        k.Kayitlar[0].Dosya = dizustuFoto;
        k.Kayitlar[1].Dosya = takvim;
        Denetle("bağlı olmayan haricî ekranın kaydındaki takvim de sayılıyor (kaldırmada zemin klasörü korunur)", ZeminVar(k) == true);
        k.Kayitlar[1].Dosya = hariciFoto;
        Denetle("ikisinde de fotoğraf varken takvim yok", ZeminVar(k) == false);

        // Geri yükleme: iki ekran kendi kaydına, aygıt yoluyla.
        k.Kayitlar[0].Dosya = takvim;
        k.Yazilanlar.Clear();
        bool tam = GeriKoy(k, yedek!, out int konan);
        Denetle("geri yüklemede iki ekran kendi fotoğrafına, aygıt yoluyla; aygıt yolu verilmeden yazılmıyor",
            tam && konan == 2 && k.Yazilanlar.Count == 2 && k.Yazilanlar.All(y => y.Aygit is not null)
            && k.Kayitlar[0].Dosya == dizustuFoto && k.Kayitlar[1].Dosya == hariciFoto, Yazimlar(k));
        Denetle("günlüğe hata yazılmadı (bağlı olmayan ekran olağan durum)", Hatalar.Count == hata);
        YedekleriSil();
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
        string eski = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261015-114200-000.bmp");

        // Bozuk hâlde ekrandaki (boş kimlikli) kayıtta eski gün: yeni yoldan bakınca görüntü yok.
        void Bozuk()
        {
            k.Kayitlar.Clear();
            k.Kayitlar.Add(new("", SOk, Sinir(0), eski));
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

    // ------------------------------------------------------------------ S17

    private static void S17()
    {
        Console.WriteLine("S17) Liste iki hâl arasında gidip gelirken yeniden yazım sınırlı: görüntü yeni yoldan da görünüyorsa yazılmıyor");
        // Ekran kapalıyken monitör ~8,7 sn'de bir veri yolundan düşüyor; Windows'un
        // listesi iki hâl arasında gidip gelebilir. Her durum iki denetim sürüyor (en kötüsü:
        // her değişim "iki ardışık denetimde aynı" şartını tutturuyor). Masaüstü katmanının
        // yaptığı: denetim, sonucu nöbete işle, yeniden yazım isteniyorsa her şeyi unut, yeniden çiz.
        Type nobetTipi = Tip("Kalender.Desktop.DuvarKagidiNobeti");
        int Dongu(SahteKabuk k, object tuval, Action[] haller, Func<(object, Color)[]> ekranlar, int tur, out object nobet)
        {
            nobet = Activator.CreateInstance(nobetTipi, true)!;
            int yazimOnce = k.Yazilanlar.Count;
            DateTime an = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < tur; i++, an = an.AddSeconds(32))
            {
                haller[i / 2 % haller.Length]();
                if (!(bool)Cagir(nobet, "DenetimZamani", an)!)
                {
                    continue;
                }

                object sonuc = Cagir(tuval, "EkraniDenetle")!;
                Cagir(nobet, "Isle", sonuc, an);
                if (sonuc.ToString() is "YolDegisti" or "Gorunmuyor")
                {
                    Unut(tuval);
                    Uygula(tuval, false, ekranlar());
                }
            }

            return k.Yazilanlar.Count - yazimOnce;
        }

        // Tek ekran: kaydın dikdörtgen sorusu S_OK ile S_FALSE arasında gidip geliyor.
        // Bağlı değilken canlı kayıt yok: aygıt yolu verilmeden; Windows ortak yolu veriyor.
        object ekran = Ekran(Aus, 0);
        SahteKabuk k = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        k.AygitsizIlkCanliyiVerir = false;
        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        k.Ortak = k.Kayitlar[0].Dosya;
        Action[] tek =
        {
            () => k.Kayitlar[0].Hr = SFalse,
            () => k.Kayitlar[0].Hr = SOk,
        };
        int yazim = Dongu(k, tuval, tek, () => new[] { (ekran, Color.Navy) }, 40, out object n1);
        Denetle("tek ekranda bağlı/bağlı değil arasında gidip gelirken görüntü yerinde; hiç yeniden yazılmıyor",
            yazim == 0 && (int)Oku(n1, "ArdArda")! == 0, $"{yazim} yazım; {Yazimlar(k)}");

        // Tek ekran: liste sağlıklı ile tutarsız (ekrandaki kayıt aygıt yollu, yanında hayalet) arasında.
        SahteKabuk k2 = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        object tuval2 = Tuval(1);
        Uygula(tuval2, false, (ekran, Color.Navy));
        string bizim = k2.Kayitlar[0].Dosya;
        Action[] tutarsiz =
        {
            () =>
            {
                if (k2.Kayitlar.Count == 1)
                {
                    k2.Kayitlar.Add(new("", EFail, Rectangle.Empty, k2.Kayitlar[0].Dosya));
                }
            },
            () =>
            {
                if (k2.Kayitlar.Count == 2)
                {
                    k2.Kayitlar.RemoveAt(1);
                }
            },
        };
        yazim = Dongu(k2, tuval2, tutarsiz, () => new[] { (ekran, Color.Navy) }, 40, out object n2);
        Denetle("tek ekranda liste sağlıklı ile tutarsız arasında gidip gelirken görüntü yerinde; hiç yeniden yazılmıyor",
            yazim == 0 && k2.Kayitlar[0].Dosya == bizim && (int)Oku(n2, "ArdArda")! == 0, $"{yazim} yazım; {Yazimlar(k2)}");

        // Çok ekran: B'nin kaydı bağlı ile bağlı değil arasında (kaydı bulunamıyor ↔ kendi kaydı).
        string aId = @"\\?\DISPLAY#AAA0001#1";
        string bId = @"\\?\DISPLAY#BBB0002#1";
        object a = Ekran(aId, 0, 1920, 1080);
        object b = Ekran(bId, 1920, 1920, 1080);
        SahteKabuk k3 = Kabuk(new Kayit(aId, SOk, Sinir(0, 1920, 1080), Foto), new(bId, SOk, Sinir(1920, 1920, 1080), Foto));
        object tuval3 = Tuval(2);
        Uygula(tuval3, false, (a, Color.Red), (b, Color.Blue));
        Action[] cok =
        {
            () => k3.Kayitlar[1].Hr = SFalse,
            () => k3.Kayitlar[1].Hr = SOk,
        };
        yazim = Dongu(k3, tuval3, cok, () => new[] { (a, Color.Red), (b, Color.Blue) }, 40, out object n3);
        Denetle("çok ekranda bir ekranın kaydı bulunamaz olup geri gelirken görüntü yerinde; hiç yeniden yazılmıyor",
            yazim == 0 && Denetim(tuval3) is "Yerinde" or "Bilinmiyor", $"{yazim} yazım; {Yazimlar(k3)}");
        k3.Kayitlar[1].Hr = SOk;
        Denetim(tuval3);
        Denetim(tuval3);
        Denetle("kaydı geri gelince iki ekran da yerinde sayılıyor", Duranlar(tuval3).SetEquals(new[] { aId, bId }));

        // En kötüsü: yazılan görüntü yeni yoldan hiç görünmüyor (her değişimde
        // ekrandaki kayıtta eski gün). Yeniden yazım art arda üçten sonra seyrekleşiyor.
        string eski = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261015-114200-000.bmp");
        SahteKabuk k4 = Kabuk(new Kayit(Aus, SOk, Sinir(0), Foto));
        object tuval4 = Tuval(1);
        Uygula(tuval4, false, (ekran, Color.Navy));
        Action[] inatci =
        {
            () =>
            {
                k4.Kayitlar.Clear();
                k4.Kayitlar.Add(new("", SOk, Sinir(0), eski));
                k4.Kayitlar.Add(new(Aus, EFail, Rectangle.Empty, eski));
            },
            () =>
            {
                k4.Kayitlar.Clear();
                k4.Kayitlar.Add(new(Aus, SOk, Sinir(0), eski));
            },
        };
        yazim = Dongu(k4, tuval4, inatci, () => new[] { (ekran, Color.Navy) }, 60, out object n4);
        // Sınırsız olsaydı her iki denetimde bir, 32 dakikada 30 yazım olurdu.
        Denetle("görüntü yeni yoldan hiç görünmüyorsa yol değişimi yazımları da sayılıyor: 32 dakikada en çok 6 yeniden yazım",
            yazim is > 0 and <= 6 && (int)Oku(n4, "ArdArda")! >= DuvarKagidiNobetiEsik(nobetTipi), $"{yazim} yazım; ArdArda {Oku(n4, "ArdArda")}");
    }

    private static int DuvarKagidiNobetiEsik(Type nobetTipi) => (int)nobetTipi.GetField("IlkEsik", Hepsi)!.GetValue(null)!;

    // ------------------------------------------------------------------ S18

    private static void S18()
    {
        Console.WriteLine("S18) Bir kaydın kimliği okunamazsa liste bilinmiyor: yedek alınmıyor, dosya silinmiyor, yeniden yazım istenmiyor");
        string harici = @"\\?\DISPLAY#DEL4321#1";
        string dizustuFoto = Fotograf("dizüstü.jpg");
        string hariciFoto = Fotograf("haricî.jpg");
        string f0 = ZeminDosyasi("zemin-eeeeeeee-20261014-090000-000.bmp");
        SahteKabuk k = Kabuk(new Kayit(Aus, SOk, Sinir(0), dizustuFoto), new(harici, SFalse, Rectangle.Empty, hariciFoto));
        k.KimligiOkunamayanSiralar.Add(1);

        Denetle("kimliği okunamayan kayıt varken liste bilinmiyor (hayalet sayılmıyor)", Tablo(k) is null);

        // Yedek: o tur alınmıyor (haricî ekranın fotoğrafı eksik kalmasın); sonraki yazım yeniden dener.
        YedekleriSil();
        Statik(Servis, "EnsureBackup", k);
        Denetle("o tur yedek alınmıyor (haricî ekranın fotoğrafı eksik bir yedek yazılmıyor)", YedekOku() is null);

        // Temizlik: ekranda ne durduğu bilinmiyor, hiçbir dosya silinmiyor.
        Denetle("ekrandaki dosyalar bilinmiyor; temizlik hiçbir dosyayı silmiyor",
            Statik(Servis, "EkrandakiDosyalar", k) is null
            && !(bool)Statik(Tip("Kalender.Desktop.WallpaperCanvas"), "KullanilmayanlariSil",
                ZeminKlasoru, Array.Empty<string>(), k, DateTime.UtcNow)!
            && File.Exists(f0));
        Denetle("ekranda takvim kalmış mı sorusu da bilinmiyor", ZeminVar(k) is null);

        // Nöbet: bilinmiyor, yeniden yazım istenmiyor.
        k.KimligiOkunamayanSiralar.Clear();
        object tuval = Tuval(1);
        Uygula(tuval, false, (Ekran(Aus, 0), Color.Navy));
        k.KimligiOkunamayanSiralar.Add(1);
        Denetle("nöbet: bilinmiyor, yeniden yazım istenmiyor", Denetim(tuval) == "Bilinmiyor" && BizimMi(tuval));

        // Okunabilince yedek yeniden deneniyor ve iki ekranın fotoğrafı da yedekte.
        k.KimligiOkunamayanSiralar.Clear();
        YedekleriSil();
        k.Kayitlar[0].Dosya = dizustuFoto;
        Statik(Servis, "EnsureBackup", k);
        object? yedek = YedekOku();
        Denetle("okunabilince yedekte iki ekranın fotoğrafı da var",
            yedek is not null && EkranBasina(yedek).Count == 2 && EkranBasina(yedek)[harici] == hariciFoto);
        YedekleriSil();
        File.Delete(f0);
    }

    // ------------------------------------------------------------------ S10

    private static void S10()
    {
        Console.WriteLine("S10) Bozuk listede yedek ve geri yükleme: hayalete ve boş anahtara dokunulmuyor");
        string aId = @"\\?\DISPLAY#AAA0001#1";
        string bId = @"\\?\DISPLAY#BBB0002#1";
        string foto = Fotograf("deniz.jpg");
        string foto2 = Fotograf("dağ.jpg");
        string silinmis = Path.Combine(Path.GetDirectoryName(foto)!, "silinmiş.jpg");
        string f15 = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261015-114200-000.bmp");
        string f16 = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261017-000002-000.bmp");
        object? Hazirla(SahteKabuk k) => Statik(Servis, "YedekHazirla", k);
        SahteKabuk Bugunku(string ekrandaki, string hayaletteki)
            => Kabuk(new Kayit("", SOk, Sinir(0), ekrandaki), new(Aus, EFail, Rectangle.Empty, hayaletteki));

        // ------------------------------------------ yedek
        object? y1 = Hazirla(Bugunku(foto, f16));
        Denetle("yedekte boş anahtar ve hayaletin kimliği yok; ekrandaki fotoğraf aygıt yolu verilmeden okunan yolda",
            y1 is not null && EkranBasina(y1).Count == 0 && (string?)Oku(y1, "Wallpaper") == foto && (bool)Oku(y1, "WallpaperEnabled")!,
            y1 is null ? "yedek yok" : string.Join(", ", EkranBasina(y1).Keys.Select(x => x.Length == 0 ? "\"\"" : x)));

        Denetle("hayaletteki fotoğraf yedeklenmiyor; ekranda yalnız takvim varken yedek yazılmıyor", Hazirla(Bugunku(f15, foto2)) is null);

        // Çok ekranda kimliği boş canlı kayıt; ekranlarda ayrı resimler, aygıt yolu verilmeden okunan yol boş.
        SahteKabuk cok = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), foto), new(bId, SOk, Sinir(1920, 1920, 1080), foto2), new(aId, EFail, Rectangle.Empty, f16));
        cok.AygitsizIlkCanliyiVerir = false;
        object? y3 = Hazirla(cok);
        Denetle("ekranlarda ayrı resimler varken kimliği boş ekranın fotoğrafı ortak yola, öbür ekranınki kendi kimliğiyle yazılıyor",
            y3 is not null && (string?)Oku(y3, "Wallpaper") == foto && EkranBasina(y3).Count == 1 && EkranBasina(y3)[bId] == foto2);

        // Yalnız hayaletten oluşan liste: ekranda ne durduğunu canlı kayıt söylemiyor, aygıt
        // yolu verilmeden okunan yol boş (Explorer hayaleti saymıyor). Önceden yedek düz renk
        // çıkıyordu; geri dönüş kullanıcının fotoğrafını düz renge çeviriyordu.
        SahteKabuk YalnizHayalet(string dosya)
        {
            SahteKabuk h = Kabuk(new Kayit(Aus, EFail, Rectangle.Empty, dosya));
            h.AygitsizIlkCanliyiVerir = false;
            return h;
        }

        object? y4 = Hazirla(YalnizHayalet(foto));
        Denetle("canlı kayıt yokken hayaletin fotoğrafı ortak yola yedekleniyor (yedek düz renk çıkmıyor)",
            y4 is not null && (bool)Oku(y4, "WallpaperEnabled")! && (string?)Oku(y4, "Wallpaper") == foto && EkranBasina(y4).Count == 0);
        SahteKabuk hOkunamaz = YalnizHayalet(foto);
        hOkunamaz.AygitsizOkumadaHata = true;
        object? y5 = Hazirla(hOkunamaz);
        Denetle("aygıt yolu verilmeden okunamasa da hayaletin fotoğrafı yedekleniyor",
            y5 is not null && (bool)Oku(y5, "WallpaperEnabled")! && (string?)Oku(y5, "Wallpaper") == foto);
        Denetle("canlı kayıt yokken hayaletteki takvim \"ekranda yalnız takvim var\" sayılıyor: yedek yazılmıyor (düz renk değil)",
            Hazirla(YalnizHayalet(f16)) is null);

        SahteKabuk hBos = YalnizHayalet(string.Empty);
        hBos.AygitsizOkumadaHata = true;
        bool hataVerdi = false;
        try
        {
            Hazirla(hBos);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            hataVerdi = true;
        }

        YedekleriSil();
        Statik(Servis, "EnsureBackup", hBos);
        Denetle("ekranda ne durduğu hiç okunamıyorsa (canlı kayıt yok, aygıt yolu verilmeden de okunamadı) düz renk yedeği yazılmıyor",
            hataVerdi && YedekOku() is null);
        hBos.AygitsizOkumadaHata = false;
        object? y6 = Hazirla(hBos);
        Denetle("aygıt yolu verilmeden okunabilir ve hiçbir kayıtta resim yoksa düz renk yedeği (önceki davranış)",
            y6 is not null && !(bool)Oku(y6, "WallpaperEnabled")!);

        // Canlı kayıt var ama dosyası okunamadı (Explorer meşgul): hayalete bakılmıyor, yedek resimsiz
        // çıkacağı için o tur yazılmıyor; aygıt yolu verilmeden okunan yol fotoğrafı veriyorsa o yedekleniyor.
        SahteKabuk canliOkunamaz = Bugunku(foto, foto2);
        canliOkunamaz.OkunamayanKimlikler.Add("");
        canliOkunamaz.AygitsizIlkCanliyiVerir = false;
        YedekleriSil();
        Statik(Servis, "EnsureBackup", canliOkunamaz);
        Denetle("canlı kaydın dosyası okunamazsa hayaletin fotoğrafı yedeklenmiyor, düz renk yedeği de yazılmıyor",
            YedekOku() is null);
        canliOkunamaz.Ortak = foto;
        object? y7 = Hazirla(canliOkunamaz);
        Denetle("aygıt yolu verilmeden okunan fotoğraf yine yedekleniyor",
            y7 is not null && (string?)Oku(y7, "Wallpaper") == foto && (bool)Oku(y7, "WallpaperEnabled")!);

        // ------------------------------------------ geri yükleme, tek canlı kayıt
        // 1.14'ün sağlıklı listede aldığı yedek (ekranın aygıt yoluyla), bugünkü bozuk liste.
        SahteKabuk k = Bugunku(f15, f16);
        bool tam = GeriKoy(k, Yedek(null, (Aus, foto)), out int konan);
        Denetle("ekrana aygıt yolu verilmeden geri konuyor; hayalete ondan sonra kendi kimliğiyle, başarı sayılmadan",
            tam && konan == 1 && k.Yazilanlar.Count == 2 && k.Yazilanlar[0].Aygit is null && k.Yazilanlar[0].Yol == foto
            && k.Yazilanlar[1].Aygit == Aus && k.Yazilanlar[1].Yol == foto && k.Kayitlar[0].Dosya == foto, Yazimlar(k));

        // Aygıt yolu verilmeden yazım hayalete ulaşmıyorsa (ölçülmedi): hayalet yine
        // ekranın fotoğrafını alıyor; liste düzelince (oturum yeniden açılınca hayaletin
        // aygıt yolu ekranın kaydı oluyor) takvimin donmuş zemini gelmiyor.
        SahteKabuk kh = Bugunku(f15, f16);
        kh.AygitsizHayaleteUlasmaz = true;
        tam = GeriKoy(kh, Yedek(null, (Aus, foto)), out konan);
        Denetle("aygıt yolu verilmeden yazım hayalete ulaşmasa da hayalette takvimin zemini kalmıyor",
            tam && konan == 1 && kh.Kayitlar[0].Dosya == foto && kh.Kayitlar[1].Dosya == foto, Yazimlar(kh));

        // 06:54'teki liste (aygıt yollu kayıt canlı): o kaydın kimliğiyle yedeklenen fotoğraf önce.
        SahteKabuk k2 = Kabuk(new Kayit(Aus, SOk, Sinir(0), f16), new("", EFail, Rectangle.Empty, f16));
        tam = GeriKoy(k2, Yedek(foto2, (@"\\?\DISPLAY#DEL4321#1", foto2), (Aus, foto)), out konan);
        Denetle("canlı kaydın kimliğiyle yedeklenen fotoğraf önce, yine aygıt yolu verilmeden",
            tam && konan == 1 && k2.Yazilanlar.Count == 1 && k2.Yazilanlar[0].Aygit is null && k2.Yazilanlar[0].Yol == foto, Yazimlar(k2));

        SahteKabuk k3 = Bugunku(f15, f16);
        tam = GeriKoy(k3, Yedek(foto2, (Aus, silinmis)), out konan);
        Denetle("ekranın fotoğrafı silinmişse ortak yol; dosyası olmayan yol denenmiyor",
            tam && konan == 1 && k3.Yazilanlar.Count == 2 && k3.Yazilanlar[0].Aygit is null && k3.Yazilanlar[0].Yol == foto2
            && k3.Yazilanlar.All(y => y.Yol == foto2), Yazimlar(k3));

        SahteKabuk k4 = Bugunku(f15, f16);
        tam = GeriKoy(k4, Yedek(null, (Aus, silinmis)), out konan);
        Denetle("hiçbir fotoğrafın dosyası yoksa geri konmuyor, sayı sıfır, ekran olduğu gibi",
            !tam && konan == 0 && k4.Yazilanlar.Count == 0 && k4.Kayitlar[0].Dosya == f15, Yazimlar(k4));

        // Canlı kayıt hiç yok (uyanma anı: ekran bağlı değil, yanında hayalet): yine aygıt yolu verilmeden.
        SahteKabuk k7 = Kabuk(new Kayit(Aus, SFalse, Rectangle.Empty, f15), new("", EFail, Rectangle.Empty, f16));
        tam = GeriKoy(k7, Yedek(null, (Aus, foto)), out konan);
        Denetle("canlı kayıt yokken de aygıt yolu verilmeden geri konuyor, sayı bir",
            tam && konan == 1 && k7.Yazilanlar.Count >= 1 && k7.Yazilanlar[0].Aygit is null && k7.Yazilanlar[0].Yol == foto
            && k7.Kayitlar[0].Dosya == foto, Yazimlar(k7));

        // Dizüstü + çıkarılmış haricî ekran (S_FALSE) + hayalet: haricî ekranın kaydı kendi fotoğrafına dönüyor.
        string harici = @"\\?\DISPLAY#DEL4321#1";
        string dizustuFoto = Fotograf("dizüstü.jpg");
        string hariciFoto = Fotograf("haricî.jpg");
        SahteKabuk k8 = Kabuk(
            new Kayit(Aus, SOk, Sinir(0), f15), new(harici, SFalse, Rectangle.Empty, f15), new("", EFail, Rectangle.Empty, f16));
        tam = GeriKoy(k8, Yedek(null, (Aus, dizustuFoto), (harici, hariciFoto)), out konan);
        Denetle("bozuk listede bağlı olmayan haricî ekranın kaydı kendi fotoğrafına dönüyor, başarı sayılmıyor",
            tam && konan == 1 && k8.Yazilanlar[0].Aygit is null && k8.Yazilanlar[0].Yol == dizustuFoto
            && k8.Kayitlar[0].Dosya == dizustuFoto && k8.Kayitlar[1].Dosya == hariciFoto, Yazimlar(k8));

        // ------------------------------------------ geri yükleme, çok ekran
        // S7 düzeni: A'nın kaydı kimliksiz, A'nın aygıt yolu hayalette; yedek sağlıklı listede alınmış.
        SahteKabuk cok2 = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), f15), new(bId, SOk, Sinir(1920, 1920, 1080), f15), new(aId, EFail, Rectangle.Empty, f16));
        tam = GeriKoy(cok2, Yedek(null, (aId, foto), (bId, foto2)), out konan, bagli: 2);
        Denetle("çok ekranda kimliksiz ekran önce aygıt yolu verilmeden (hayaletin kimliğiyle yedeklenen fotoğraf), sonra öbür ekran kendi kaydına",
            cok2.Yazilanlar.Count >= 2 && cok2.Yazilanlar[0].Aygit is null && cok2.Yazilanlar[0].Yol == foto
            && cok2.Yazilanlar[1].Aygit == bId && cok2.Yazilanlar[1].Yol == foto2, Yazimlar(cok2));
        Denetle("her ekranda kendi fotoğrafı, hayalette de; iki ekran sayılıyor, sonuç tam",
            tam && konan == 2 && cok2.Kayitlar[0].Dosya == foto && cok2.Kayitlar[1].Dosya == foto2 && cok2.Kayitlar[2].Dosya == foto,
            $"{tam} / {konan}");

        // Uçtan uca (katmanın yaptığı): geri yüklemeden sonra takvimi gösteren ekran yok, takvimsiz zemine gerek kalmıyor.
        object a = Ekran(aId, 0, 1920, 1080);
        object b = Ekran(bId, 1920, 1920, 1080);
        HashSet<string> Gosterenler(SahteKabuk t) => (HashSet<string>)Statik(
            Tip("Kalender.Desktop.WallpaperCanvas"), "TakvimGosterenler", EkranListesi(a, b), t)!;
        Denetle("geri yüklemeden sonra kimliksiz ekranda takvim kalmıyor", Gosterenler(cok2).Count == 0);

        // Aygıt yolu verilmeden yazım hata verirse kimliksiz ekranda takvim kalıyor; yalnız o
        // ekrana takvimsiz zemin de yazılamıyor. Katman bunu görüp "geri yüklenemedi" diyor (aşağıda, derlenmiş koddan).
        SahteKabuk cok3 = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), f15), new(bId, SOk, Sinir(1920, 1920, 1080), f15), new(aId, EFail, Rectangle.Empty, f16));
        cok3.AygitsizYazimdaHata = true;
        tam = GeriKoy(cok3, Yedek(null, (aId, foto), (bId, foto2)), out konan, bagli: 2);
        Uygula(Tuval(2), true, (a, Color.Gray));
        Denetle("aygıt yolu verilmeden yazılamayınca sonuç eksik; öbür ekran yine kendi fotoğrafına; kimliksiz ekranda takvim kalıyor",
            !tam && konan == 1 && cok3.Kayitlar[1].Dosya == foto2 && Gosterenler(cok3).SetEquals(new[] { aId }), Yazimlar(cok3));

        // İki kimliksiz ekran: aygıt yolu verilmeden yazılamaz (iki ekran aynı fotoğrafı alırdı); sonuç eksik.
        SahteKabuk iki = Kabuk(
            new("", SOk, Sinir(0, 1920, 1080), f15), new("", SOk, Sinir(1920, 1920, 1080), f15),
            new(aId, EFail, Rectangle.Empty, f16), new(bId, EFail, Rectangle.Empty, f16));
        tam = GeriKoy(iki, Yedek(null, (aId, foto), (bId, foto2)), out konan, bagli: 2);
        Denetle("iki kimliksiz ekranda aygıt yolu verilmeden hiç yazılmıyor; hayaletlere kendi fotoğrafları; sonuç eksik",
            !tam && konan == 0 && iki.Yazilanlar.All(y => y.Aygit is not null) && iki.Kayitlar[2].Dosya == foto
            && iki.Kayitlar[3].Dosya == foto2, Yazimlar(iki));

        // İki bağlı ekran, yalnız biri canlı (öbürünün aygıt yolu hayalette): aygıt yolu verilmeden
        // yazılmıyor (iki ekran da aynı fotoğrafı alırdı); öbür ekranın fotoğrafı hayalete; sonuç eksik.
        SahteKabuk tekCanli = Kabuk(new Kayit(aId, SOk, Sinir(0, 1920, 1080), f15), new(bId, EFail, Rectangle.Empty, f16));
        tam = GeriKoy(tekCanli, Yedek(null, (aId, foto), (bId, foto2)), out konan, bagli: 2);
        Denetle("iki bağlı ekranda tek canlı kayıt: aygıt yolu verilmeden yazılmıyor, öbür ekranın fotoğrafı hayalete, sonuç eksik",
            !tam && konan == 1 && tekCanli.Yazilanlar.All(y => y.Aygit is not null)
            && tekCanli.Kayitlar[0].Dosya == foto && tekCanli.Kayitlar[1].Dosya == foto2, Yazimlar(tekCanli));
        tekCanli = Kabuk(new Kayit(aId, SOk, Sinir(0, 1920, 1080), f15), new(bId, EFail, Rectangle.Empty, f16));
        GeriKoy(tekCanli, Yedek(null, (aId, foto), (bId, foto2)), out konan, bagli: 0);
        Denetle("bağlı ekran sayısı bilinmiyorsa canlı kayıtlardan tahmin ediliyor (önceki davranış: aygıt yolu verilmeden)",
            tekCanli.Yazilanlar.Count >= 1 && tekCanli.Yazilanlar[0].Aygit is null, Yazimlar(tekCanli));

        // Derlenmiş koddan: geri yükleme bağlı ekran sayısını veriyor; katman takvim kalırsa başarı bildirmiyor.
        List<Komut> geri = Komutlar(Servis.GetMethod("Restore", Hepsi, new[] { typeof(bool).MakeByRefType() })!);
        int sayi = Bul(geri, x => Cagri(x, Servis, "get_BagliEkranSayisiOkuyucu"));
        Denetle("geri yükleme bağlı ekran sayısını Windows'tan okuyup veriyor",
            sayi >= 0 && Bul(geri, x => Cagri(x, Servis, "EkranlariGeriYukle"), sayi) > sayi);
        Type katmanTipi = Tip("Kalender.Desktop.DesktopLayerManager");
        Type tuvalTipi = Tip("Kalender.Desktop.WallpaperCanvas");
        List<Komut> kaldir = Komutlar(katmanTipi.GetMethod("DuvarKagidiniGeriYukle", Hepsi)!);
        int zemin = Bul(kaldir, x => Cagri(x, katmanTipi, "PaintPlainBackground"));
        int yeniden = Bul(kaldir, x => Cagri(x, tuvalTipi, "TakvimGosterenler"), zemin);
        Denetle("katman takvimsiz zeminden sonra ekrana yeniden bakıyor; takvim kaldıysa günlüğe hata yazıp başarı bildirmiyor",
            zemin > 0 && yeniden > zemin && Bul(kaldir, x => x.Uye is MethodBase m && m.Name == "Error", yeniden) > yeniden);

        // ------------------------------------------ eski yedekteki boş anahtar
        SahteKabuk k5 = Kabuk(new Kayit(Aus, SOk, Sinir(0), f15));
        tam = GeriKoy(k5, Yedek(null, ("", foto)), out konan);
        Denetle("eski yedeğin boş anahtarı ortak yol gibi okunuyor: aygıt yolu verilmeden",
            tam && konan == 1 && k5.Yazilanlar.Count == 1 && k5.Yazilanlar[0].Aygit is null && k5.Yazilanlar[0].Yol == foto, Yazimlar(k5));
        SahteKabuk k6 = Kabuk(new Kayit(Aus, SOk, Sinir(0), f15));
        tam = GeriKoy(k6, Yedek(null, ("", foto), (Aus, foto2)), out konan);
        Denetle("boş anahtara hiç yazılmıyor; ekran kendi kaydına",
            tam && konan == 1 && k6.Yazilanlar.Count == 1 && k6.Yazilanlar[0].Aygit == Aus && k6.Yazilanlar[0].Yol == foto2, Yazimlar(k6));

        // ------------------------------------------ ekranda takvim kalmış mı
        Denetle("hayaletteki takvim sayılmıyor (açılışta gereksiz geri yükleme yok)", ZeminVar(Bugunku(foto, f16)) == false);
        Denetle("ekrandaki (boş kimlikli) kayıttaki takvim sayılıyor", ZeminVar(Bugunku(f15, foto)) == true);
        SahteKabuk okunamayan = Bugunku(f15, foto);
        okunamayan.OkunamayanKimlikler.Add("");
        Denetle("canlı kaydın dosyası okunamazsa aygıt yolu verilmeden okunan dosyaya bakılıyor", ZeminVar(okunamayan) == true);
        okunamayan.Kayitlar[0].Dosya = foto;
        Denetle("o da takvim değilse bilinmiyor", ZeminVar(okunamayan) is null);
        SahteKabuk yalnizHayalet = Kabuk(new Kayit(Aus, EFail, Rectangle.Empty, foto));
        yalnizHayalet.AygitsizIlkCanliyiVerir = false;
        yalnizHayalet.Ortak = f15;
        Denetle("canlı kayıt yokken aygıt yolu verilmeden okunan dosyaya bakılıyor", ZeminVar(yalnizHayalet) == true);
        var korunan = (HashSet<string>?)Statik(Servis, "EkrandakiDosyalar", Bugunku(foto, f16));
        Denetle("silmeden korumada hayaletin dosyası yine sayılıyor", korunan is not null && korunan.Contains(f16));

        // Kaldırmada "ayarlarımı da sil": zemin klasörü bütün kayıtlara bakılarak korunuyor.
        bool Korunmali(SahteKabuk t) => (bool)Statik(Servis, "ZeminKlasoruKorunmali", t)!;
        Denetle("kaldırmada hayalet takvimin zeminini gösteriyorsa zemin klasörü korunuyor (ekranda görünmese de)",
            ZeminVar(Bugunku(foto, f16)) == false && Korunmali(Bugunku(foto, f16)));
        Denetle("hiçbir kayıtta takvimin zemini yoksa korunmuyor", !Korunmali(Bugunku(foto, foto2)));
        SahteKabuk okunamaz = Bugunku(foto, f16);
        okunamaz.SayimdaHata = true;
        Denetle("ekrandakiler okunamazsa zemin klasörü korunuyor", Korunmali(okunamaz));
        Denetle("kaldırma bunu soruyor (derlenmiş koddan)",
            Komutlar(Tip("Kalender.Core.Installer").GetMethod("ScheduleFileRemoval", Hepsi)!)
                .Any(x => Cagri(x, Servis, "ZeminKlasoruKorunmali")));

        // Yedek yokken geri yükleme ekranda görünene bakıyor: ekranda fotoğraf, hayalette takvim; uyarı yok.
        YedekleriSil();
        SahteKabuk yedeksiz = Bugunku(foto, f16);
        FabrikayiBagla(() => yedeksiz);
        object?[] cikti = { true };
        bool yuklendi = (bool)Servis.GetMethod("Restore", Hepsi, new[] { typeof(bool).MakeByRefType() })!.Invoke(null, cikti)!;
        Denetle("yedek yokken ekranda fotoğraf duruyorsa (takvim yalnız hayalette) geri yükleme başarılı, uyarı yok",
            yuklendi && !(bool)cikti[0]! && yedeksiz.Yazilanlar.Count == 0, $"{yuklendi} / eksik {cikti[0]}");
        yedeksiz.Kayitlar[0].Dosya = f15;
        cikti[0] = false;
        yuklendi = (bool)Servis.GetMethod("Restore", Hepsi, new[] { typeof(bool).MakeByRefType() })!.Invoke(null, cikti)!;
        Denetle("ekranda takvim duruyorsa eksik (katman oraya takvimsiz zemin yazar)", !yuklendi && (bool)cikti[0]!);
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
        AlanAta(y, "_renderer", Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"), true)!);
        AlanAta(y, "_configSignature", Statik(t, "BuildConfigSignature", Cagir(y, "EkranlariOku")));
        return y;
    }

    /// <summary>
    /// Katmanın bir duvar kâğıdı denetim turu (Nobet); takvimi yeniden
    /// yazdırdıysa (Forget) true. Tur boyunca katman kapanıyormuş gibi
    /// gösterilir: nöbetin ekran yapılandırması denetimi (EnsureAlive) ve
    /// yeniden çizim hiçbir şey yapmaz, duvar kâğıdı denetimi yine yapılır.
    /// Önceden gerçek ekran tur sırasında bir an kimliksiz okununca (ekran
    /// kapalıyken monitör veri yolundan düşüp kalkıyor) katman gerçek ekranla
    /// yeniden kurulabiliyordu: kimlik beklenirken duvar kâğıdı denetimi
    /// atlanıyor, kimlik geri gelince geri çekilme sıfırlanıyor, takvim gerçek
    /// ekrana çizilmeye kalkıyordu (Lite'ta 2026-10-17'de bir kez görüldü).
    /// </summary>
    private static bool NobetTuru(object katman)
    {
        object tuval = AlanOku(katman, "_canvas");
        AlanAta(katman, "_nobetSayaci", 3);
        ((IDictionary)AlanOku(tuval, "_hashes"))["iz"] = "iz";
        AlanAta(katman, "_disposed", true);
        try
        {
            Cagir(katman, "Nobet");
        }
        finally
        {
            AlanAta(katman, "_disposed", false);
        }

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

        bool b1 = Isle("Gorunmuyor", t0), b2 = Isle("YolDegisti", t0);
        Denetle("ilk iki uyuşmazlıkta beklenmiyor, haber yok; yol değişimi de sayılıyor",
            !b1 && !b2 && Zamani(t0) && ArdArda() == 2);
        Denetle("bilinmeyen sonuç sayılmıyor, sayacı da bırakmıyor", !Isle("Bilinmiyor", t0) && ArdArda() == 2 && Zamani(t0));
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
        List<Komut> balon = Komutlar(host.GetMethod("OnDuvarKagidiGuncellenemiyor", Hepsi)!);
        int goster = Bul(balon, x => Cagri(x, Tip("Kalender.Tray.TrayIcon"), "ShowBalloon"));
        string metin = string.Concat(balon.Select(x => x.Dize));
        Denetle("balon uyarı olarak, sade dille: Windows masaüstü resmini yenilemiyor, oturumu kapatıp açmak düzeltir",
            goster > 0 && balon[goster - 1].Kod == OpCodes.Ldc_I4_1
            && metin.Contains("Windows masaüstü resmini yenilemiyor; takvim eski görünebilir.", StringComparison.Ordinal)
            && metin.Contains("Oturumu kapatıp açmak ya da bilgisayarı yeniden başlatmak düzeltir.", StringComparison.Ordinal), metin);

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

        // Uyanmada kurulan 20 saniyelik güvence yazımı Explorer hemen ardından yeniden başlayınca öne çekilmiyor.
        Cagir(katman, "TazelemeIste", TimeSpan.FromSeconds(20));
        Cagir(uygulama, "OnShellRestarted");
        Denetle("bekleyen daha geç güvence yazımı (uyanma, açılış) Explorer yeniden başlayınca kısalmıyor",
            tazeleme.IsEnabled && tazeleme.Interval == TimeSpan.FromSeconds(20), $"{tazeleme.IsEnabled} {tazeleme.Interval}");
        Cagir(katman, "TazelemeIste", TimeSpan.FromSeconds(60));
        Denetle("daha geç bir istek bekleyeni uzatıyor (son istek yine geçerli)",
            tazeleme.IsEnabled && tazeleme.Interval == TimeSpan.FromSeconds(60), $"{tazeleme.IsEnabled} {tazeleme.Interval}");
        tazeleme.Stop();
        kurma.Stop();
        Denetle("günlükte bozuk liste için bir, art arda uyuşmazlık için bir hata satırı",
            Hatalar.Count == hata + 2, string.Join(" | ", Hatalar.Skip(hata)));

        // Yeni bozukluk dönemi (ekranda yine eski gün): günlüğe yine bir hata,
        // kullanıcıya ikinci kez haber yok; balon süreç başına bir kez.
        k.AygitsizBosKaydaUlasmaz = true;
        k.Kayitlar[0].Dosya = f15;
        int hataYeni = Hatalar.Count;
        var yeniTurlar = new List<bool> { NobetTuru(katman), NobetTuru(katman), NobetTuru(katman) };
        Denetle("yeni bozukluk döneminde üçüncü uyuşmazlıkta günlüğe yine bir hata; kullanıcıya ikinci kez haber verilmiyor",
            yeniTurlar.All(x => x) && haber == 1 && Hatalar.Count == hataYeni + 1 && Hatalar[^1].Contains("art arda 3"),
            $"{string.Join(", ", yeniTurlar)}; haber {haber}; " + string.Join(" | ", Hatalar.Skip(hataYeni)));
        Denetle("geri çekilme yine 5 dakika", !NobetTuru(katman) && (int)Oku(nobeti, "ArdArda")! == 3);
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

        // Bağlı olmayan ekranın dosyası okunamıyor (ölçülmedi; dizüstünden çıkarılmış
        // haricî ekranda kalıcı olabilir): olağan temizlik durmuyor, o ekranın en son
        // görüntüsü (adındaki kısa kimlikle) korunuyor. Dursaydı ekran yeniden
        // takılana dek her yazım büyük bir dosya bırakırdı.
        string kisa = (string)Statik(Tip("Kalender.Desktop.WallpaperCanvas"), "ShortId", @"\\?\DISPLAY#DEL4321#1")!;
        string hariciEski = ZeminDosyasi($"zemin-{kisa}-20261016-090000-000.bmp");
        File.SetLastWriteTimeUtc(hariciEski, DateTime.UtcNow.AddMinutes(-20));
        string hariciSon = ZeminDosyasi($"zemin-{kisa}-20261016-100000-000.bmp");
        f0 = ZeminDosyasi("zemin-dddddddd-20261014-090000-000.bmp");
        FabrikayiBagla(() => bagliDegil);
        hata = Hatalar.Count;
        Cagir(tuval, "CleanUnusedFiles", new object?[] { null });
        Denetle("bağlı olmayan ekranın dosyası okunamasa da olağan temizlik sürüyor: o ekranın son görüntüsü ve canlı kaydınki "
            + "kalıyor, öbür eski dosyalar siliniyor; günlüğe hata yok",
            File.Exists(hariciSon) && !File.Exists(hariciEski) && File.Exists(f15) && !File.Exists(f0)
            && Hatalar.Count == hata && !(bool)AlanOku(tuval, "_temizlikOkunamadiYazildi"));
        Denetle("kaldırmada ve geri yüklemede (EkrandakiDosyalar) yine bilinmiyor: zemin klasörü korunuyor",
            Statik(servis, "EkrandakiDosyalar", bagliDegil) is null && (bool)Statik(servis, "ZeminKlasoruKorunmali", bagliDegil)!);
    }

    // ------------------------------------------------------------------ S15

    private static void S15()
    {
        Console.WriteLine("S15) Canlı kayıt yokken: aygıt yolu verilmeden yazılıyor, nöbet aygıt yolu verilmeden okunan dosyaya bakıyor");
        // Ekran bağlı değil (S_FALSE); Windows aygıt yolu verilmeden okununca kendi ortak yolunu veriyor.
        SahteKabuk k = Kabuk(new Kayit(Aus, SFalse, Rectangle.Empty, Foto));
        k.AygitsizIlkCanliyiVerir = false;
        k.Ortak = Foto;
        object ekran = Ekran(Aus, 0);
        object tuval = Tuval(1);
        Uygula(tuval, false, (ekran, Color.Navy));
        string yazilan = k.Yazilanlar.Count == 1 ? k.Yazilanlar[0].Yol : "?";
        Denetle("tek yazım, aygıt yolu verilmeden", k.Yazilanlar.Count == 1 && k.Yazilanlar[0].Aygit is null, Yazimlar(k));
        Denetle("nöbet: aygıt yolu verilmeden okunan dosya bizimki, yerinde", Denetim(tuval) == "Yerinde");

        k.Ortak = Foto;
        Denetle("aygıt yolu verilmeden okunan dosya değişince görüntü yok sayılıyor (kayıt aynı kalsa da)",
            k.Kayitlar[0].Dosya == yazilan && Denetim(tuval) == "Gorunmuyor", Denetim(tuval));

        k.OkumadaHata = true;
        Denetle("aygıt yolu verilmeden okunamazsa bilinmiyor", Denetim(tuval) == "Bilinmiyor");
        k.OkumadaHata = false;

        // Yalnız hayalet kayıt (bozuk liste, canlı kayıt yok).
        int hata = Hatalar.Count;
        SahteKabuk h = Kabuk(new Kayit(Aus, EFail, Rectangle.Empty, Foto));
        h.AygitsizIlkCanliyiVerir = false;
        h.Ortak = Foto;
        object tuval2 = Tuval(1);
        Uygula(tuval2, false, (ekran, Color.Navy));
        Denetle("yalnız hayalet kayıtta da aygıt yolu verilmeden; yerinde; günlüğe bir kez hata",
            h.Yazilanlar.Count == 1 && h.Yazilanlar[0].Aygit is null && Denetim(tuval2) == "Yerinde" && Hatalar.Count == hata + 1,
            Yazimlar(h));
    }

    // ------------------------------------------------------------------ S16

    private static void S16()
    {
        Console.WriteLine("S16) Geri yükleme eksik kalınca takvimin durduğu ekranlar bütün bağlı ekranlarla aranıyor");
        string aId = @"\\?\DISPLAY#AAA0001#1";
        string bId = @"\\?\DISPLAY#BBB0002#1";
        object a = Ekran(aId, 0, 1920, 1080);
        object b = Ekran(bId, 1920, 1920, 1080);
        string takvimli = Path.Combine(ZeminKlasoru, "zemin-aaaaaaaa-20261017-101010-123.bmp");
        SahteKabuk k = Kabuk(new Kayit(aId, SOk, Sinir(0, 1920, 1080), takvimli), new(bId, SOk, Sinir(1920, 1920, 1080), Foto));
        HashSet<string> Gosterenler(params object[] ekranlar) => (HashSet<string>)Statik(
            Tip("Kalender.Desktop.WallpaperCanvas"), "TakvimGosterenler", EkranListesi(ekranlar), k)!;
        Denetle("iki ekran verilince takvimin durduğu ekran bulunuyor", Gosterenler(a, b).SetEquals(new[] { aId }));
        Denetle("yalnız biri verilseydi tek ekran sanılır, bulunamazdı (bu yüzden bütün bağlı ekranlar veriliyor)",
            Gosterenler(a).Count == 0);

        // Derlenmiş koddan: katman çizilebilen ekranları değil bütün bağlı ekranları veriyor.
        Type katman = Tip("Kalender.Desktop.DesktopLayerManager");
        List<Komut> k2 = Komutlar(katman.GetMethod("DuvarKagidiniGeriYukle", Hepsi)!);
        int gosteren = Bul(k2, x => Cagri(x, Tip("Kalender.Desktop.WallpaperCanvas"), "TakvimGosterenler"));
        Denetle("geri yüklemede takvimi gösteren ekranlar bütün bağlı ekranlarla (_monitors) aranıyor",
            gosteren > 0 && k2.GetRange(0, gosteren).Any(x => x.Uye is FieldInfo f && f.Name == "_monitors")
            && !k2.GetRange(0, gosteren).Any(x => x.Uye is FieldInfo f && f.Name == "_screens"));
    }

    // ------------------------------------------------------------------ S19

    private static void S19()
    {
        Console.WriteLine("S19) Duvar kâğıdı arayüzünün bildirimi ve yalnız fabrikada açılması (derlenmiş koddan)");

        // Sahte kabuklar (DispatchProxy) yöntemi adıyla çağırıyor; COM yuvasına ve
        // PreserveSig'e bakmıyor. Bildirim bozulursa (bir birleştirmede PreserveSig
        // düşer, yöntemler yer değiştirir) bütün sınamalar geçer ama gerçek arayüzde
        // yanlış yuva çağrılır ya da bağlı olmayan ekranın S_FALSE'u başarı sayılır.
        Type arayuz = Tip("Kalender.Interop.IDesktopWallpaper");
        string[] windowsSirasi =
        {
            "SetWallpaper", "GetWallpaper", "GetMonitorDevicePathAt", "GetMonitorDevicePathCount", "GetMonitorRECT",
            "SetBackgroundColor", "GetBackgroundColor", "SetPosition", "GetPosition", "SetSlideshow", "GetSlideshow",
            "SetSlideshowOptions", "GetSlideshowOptions", "AdvanceSlideshow", "GetStatus", "Enable",
        };
        List<MethodInfo> yontemler = arayuz.GetMethods().OrderBy(m => m.MetadataToken).ToList();
        Denetle("yöntemler Windows'un arayüzündeki sırada (COM yuvaları bildirim sırasıyla veriliyor)",
            yontemler.Select(m => m.Name).SequenceEqual(windowsSirasi), string.Join(", ", yontemler.Select(m => m.Name)));

        MethodInfo rect = arayuz.GetMethod("GetMonitorRECT")!;
        ParameterInfo[] p = rect.GetParameters();
        Denetle("GetMonitorRECT sonucu dönüş değeri olarak veriyor (PreserveSig, int; kimlik LPWStr, dikdörtgen RECT'e out)",
            (rect.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0 && rect.ReturnType == typeof(int)
            && p.Length == 2 && p[0].ParameterType == typeof(string)
            && p[0].GetCustomAttribute<MarshalAsAttribute>()?.Value == UnmanagedType.LPWStr
            && p[1].IsOut && p[1].ParameterType == Tip("Kalender.Interop.NativeMethods+RECT").MakeByRefType(),
            $"{rect.MethodImplementationFlags} {rect.ReturnType.Name} ({string.Join(", ", p.Select(x => x.ParameterType.Name))})");
        Denetle("öbür yöntemlerde hata istisnaya dönüyor (PreserveSig yalnız GetMonitorRECT'te)",
            yontemler.Where(m => m.Name != "GetMonitorRECT")
                .All(m => (m.MethodImplementationFlags & MethodImplAttributes.PreserveSig) == 0));
        Denetle("arayüz ve sınıf Windows'un kimlikleriyle, arayüz IUnknown tabanlı",
            arayuz.IsImport && arayuz.GUID == new Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")
            && arayuz.GetCustomAttribute<InterfaceTypeAttribute>()?.Value == ComInterfaceType.InterfaceIsIUnknown
            && Tip("Kalender.Interop.DesktopWallpaperClass").GUID == new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));

        // Sınamaların "gerçek duvar kâğıdına dokunulmaz" güvencesi, uygulamanın
        // arayüzü her yerde fabrikadan (DuvarKagidiKayitlari.Ac) açmasına dayanıyor.
        // 1.14'te üç yerde ayrı ayrı açılıyordu; biri geri gelirse sınama gerçek
        // arayüzü sessizce açardı (ör. DesktopRefresher'ın arka plan rengi yazımı).
        Type sinif = Tip("Kalender.Interop.DesktopWallpaperClass");
        bool Fabrikada(Type? t)
        {
            for (; t is not null; t = t.DeclaringType)
            {
                if (t == Kayitlar)
                {
                    return true;
                }
            }

            return false;
        }

        const BindingFlags Bildirilen = Hepsi | BindingFlags.DeclaredOnly;
        var acanlar = new List<MethodBase>();
        var cozulemeyenler = new List<string>();
        int taranan = 0;
        foreach (Type t in Uyg.GetTypes())
        {
            foreach (MethodBase m in t.GetMethods(Bildirilen).Cast<MethodBase>().Concat(t.GetConstructors(Bildirilen)))
            {
                List<Komut> komutlar;
                try
                {
                    if (m.GetMethodBody() is null)
                    {
                        continue;
                    }

                    komutlar = Komutlar(m);
                }
                catch (Exception ex)
                {
                    cozulemeyenler.Add($"{t.FullName}.{m.Name}: {ex.GetType().Name}");
                    continue;
                }

                taranan++;
                if (komutlar.Any(k => (k.Kod == OpCodes.Newobj && k.Uye is ConstructorInfo c && c.DeclaringType == sinif)
                        || (k.Belirtec is Type tur && tur == sinif)))
                {
                    acanlar.Add(m);
                }
            }
        }

        Denetle("derlemenin bütün yöntem gövdeleri tarandı",
            taranan > 1000 && cozulemeyenler.Count == 0, $"{taranan} yöntem; çözülemeyen: {string.Join(", ", cozulemeyenler.Take(5))}");
        Denetle("duvar kâğıdı arayüzü yalnız fabrikada açılıyor (DesktopWallpaperClass'a başka hiçbir yerde başvurulmuyor)",
            acanlar.Count == 1 && Fabrikada(acanlar[0].DeclaringType),
            string.Join(", ", acanlar.Select(m => $"{m.DeclaringType?.FullName}.{m.Name}")));
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

    /// <summary>
    /// Doğruysa aygıt yolu verilmeden yazılan görüntü hayalet kayda (dikdörtgen
    /// sorusu hata veren) ulaşmıyor; ölçülmedi (Windows aygıt yolu verilmeden
    /// okurken hayaleti saymıyor).
    /// </summary>
    public bool AygitsizHayaleteUlasmaz { get; set; }

    /// <summary>Doğruysa aygıt yolu verilmeden yazım hata verir.</summary>
    public bool AygitsizYazimdaHata { get; set; }

    /// <summary>Doğruysa kayıt sayısı okunamıyor.</summary>
    public bool SayimdaHata { get; set; }

    /// <summary>Doğruysa GetWallpaper hata verir.</summary>
    public bool OkumadaHata { get; set; }

    /// <summary>Doğruysa yalnız aygıt yolu verilmeden okuma (GetWallpaper(NULL)) hata verir.</summary>
    public bool AygitsizOkumadaHata { get; set; }

    /// <summary>Bu kimliklerde GetWallpaper hata verir (yalnız o kayıt okunamıyor; boş kimlik de olur).</summary>
    public HashSet<string> OkunamayanKimlikler { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bu sıralardaki kayıtların kimliği okunamaz (GetMonitorDevicePathAt hata verir).</summary>
    public HashSet<int> KimligiOkunamayanSiralar { get; } = new();

    /// <summary>
    /// Bu kimliklerde dikdörtgen sorusu Explorer'a ulaşamaz: çağrı istisna verir
    /// (aktarım hatası; Explorer'ın kendi cevabı dönüş değeri olarak gelir).
    /// </summary>
    public HashSet<string> DikdortgeniSorulamayanlar { get; } = new(StringComparer.OrdinalIgnoreCase);

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
                int sira = (int)(uint)a![0]!;
                if (KimligiOkunamayanSiralar.Contains(sira))
                {
                    throw new COMException("Kaydın kimliği okunamadı (deneme).", EFail);
                }

                return Kayitlar[sira].Kimlik;

            case "GetMonitorRECT":
                if (DikdortgeniSorulamayanlar.Contains((string)a![0]!))
                {
                    throw new InvalidComObjectException("Explorer'a ulaşılamadı (deneme).");
                }

                Kayit? kayit = Bul((string)a[0]!);
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

                if (AygitsizOkumadaHata)
                {
                    throw new COMException("Aygıt yolu verilmeden okunamadı (deneme).", EFail);
                }

                return AygitsizIlkCanliyiVerir ? Kayitlar.FirstOrDefault(k => k.Canli)?.Dosya ?? string.Empty : Ortak;

            case "SetWallpaper":
                string yol = (string)a![1]!;
                if (a[0] is null && AygitsizYazimdaHata)
                {
                    throw new COMException("Aygıt yolu verilmeden yazılamadı (deneme).", EFail);
                }

                Yazilanlar.Add(((string?)a[0], yol));
                if (a[0] is string hedef)
                {
                    // Hayalete de hatasız yazılır; ekrana çıkmaz.
                    (Bul(hedef) ?? throw new COMException("Bilinmeyen aygıt yolu (deneme).", EInvalidArg)).Dosya = yol;
                }
                else
                {
                    Ortak = yol;
                    foreach (Kayit k in Kayitlar.Where(k => !(AygitsizBosKaydaUlasmaz && k.Kimlik.Length == 0)
                                 && !(AygitsizHayaleteUlasmaz && k.Hr < 0)))
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
