using System.Collections;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;
using DPoint = System.Drawing.Point;
using DPointF = System.Drawing.PointF;
using DRectangle = System.Drawing.Rectangle;
using DRectangleF = System.Drawing.RectangleF;

// Kalender'de genel kod incelemesinin (2026-10-12) düzeltmelerinin sınaması
// (yansımayla). Madde numaraları kalender.md'deki listeden:
//   3. "Takvimi Windows duvar kâğıdına işle" kapatılınca önceki duvar kâğıdı
//      geri yüklenir (bir kez); kapanışta takvimsiz zemin ayara değil, ekranda
//      hâlâ bu sürecin görüntüsü durup durmadığına bakarak yazılır; tepsideki
//      "Eski duvar kâğıdıma dön" önce yazmayı kapatır.
//   4. Yazma kapalıyken takvimin eski yeri sağ tık, ok ve gün kutusu için
//      isabet vermez.
//   6. Geri yükleme: yedekte olmayan ekrana ortak yol ya da yedekteki ilk
//      ekranın yolu konur; bir ekrandaki hata öbürlerini durdurmaz; dosyası
//      olmayan fotoğraf denenmez; ekranda duran zemin dosyası silinmez; takvimin
//      kendi görüntüsü yedeğe yazılmaz; yedek kullanılınca silinir.
//  25. Yazma yeniden açılınca sıkıştırma kalitesi yeniden denetlenir.
//  32. Ayarlar penceresi açıkken ayar başka yerden değişince duvar kâğıdı
//      anahtarı ona eşitlenir.
//
// Gerçek duvar kâğıdına yazılmaz: Windows'un duvar kâğıdı arayüzü yerine bu
// programdaki sahtesi kullanılır; gerçeği yalnızca okunur. Uygulamanın klasörü
// (Paths.RootFolder) %TEMP% altında geçici bir klasöre çevrilir: kurulu
// Kalender'in ayarlarına ve duvar kâğıdı yedeğine dokunulmaz (sonda
// denetlenir). Geçici klasör sonunda silinir. Çıkış kodu 0 = hepsi geçti.
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

    private static void Yaz(object o, string ad, object? deger) => o.GetType().GetProperty(ad, Hepsi)!.SetValue(o, deger);

    private static object AlanOku(object o, string ad) => o.GetType().GetField(ad, Hepsi)!.GetValue(o)!;

    private static void AlanAta(object o, string ad, object? deger) => o.GetType().GetField(ad, Hepsi)!.SetValue(o, deger);

    private static object? Cagir(object o, string ad, params object?[] a) => o.GetType().GetMethod(ad, Hepsi)!.Invoke(o, a);

    private static object? Statik(Type t, string ad, params object?[] a) => t.GetMethod(ad, Hepsi)!.Invoke(null, a);

    // ------------------------------------------------- derlenmiş koddaki komutlar

    /// <summary>Bir IL komutu: yeri, işlem kodu, varsa çağrılan yöntem ya da alan, dallanıyorsa hedefi.</summary>
    private sealed record Komut(int Yer, OpCode Kod, MemberInfo? Uye, int Hedef);

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
            int yer = i;
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

            int uzunluk = kod.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };

            int hedef = kod.OperandType switch
            {
                OperandType.ShortInlineBrTarget => i + 1 + unchecked((sbyte)il[i]),
                OperandType.InlineBrTarget => i + 4 + BitConverter.ToInt32(il, i),
                _ => -1,
            };

            i += uzunluk;
            sonuc.Add(new Komut(yer, kod, uye, hedef));
        }

        return sonuc;
    }

    private static bool Cagri(Komut k, Type tip, string ad)
        => k.Uye is MethodBase m && m.DeclaringType == tip && m.Name == ad;

    private static bool AlanKomutu(Komut k, OpCode kod, string ad)
        => k.Kod == kod && k.Uye is FieldInfo a && a.Name == ad;

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

    /// <summary><paramref name="yer"/>'i try bölümünde tutan finally bloğu (yoksa null).</summary>
    private static ExceptionHandlingClause? Finally(MethodBase yontem, int yer)
        => yontem.GetMethodBody()!.ExceptionHandlingClauses.FirstOrDefault(c =>
            c.Flags == ExceptionHandlingClauseOptions.Finally
            && yer >= c.TryOffset && yer < c.TryOffset + c.TryLength);

    private static bool Icinde(ExceptionHandlingClause? c, int yer)
        => c is not null && yer >= c.HandlerOffset && yer < c.HandlerOffset + c.HandlerLength;

    // ------------------------------------------------------ deneme nesneleri

    /// <summary>
    /// Kurucusu çalıştırılmamış bir masaüstü katmanı: zamanlayıcıları işlemez,
    /// ayar dosyası okunmaz. Ayarlar bellekte; duvar kâğıdına yazma kapalı.
    /// </summary>
    private static object Yonetici()
    {
        Type t = Tip("Kalender.Desktop.DesktopLayerManager");
        object y = RuntimeHelpers.GetUninitializedObject(t);

        object depo = RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Settings.SettingsStore"));
        AlanAta(depo, "_gate", new object());
        object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;
        Yaz(ayarlar, "SyncSystemWallpaper", false);
        Yaz(depo, "Current", ayarlar);

        AlanAta(y, "_settings", depo);
        AlanAta(y, "_renderer", Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"), true)!);
        AlanAta(y, "_canvas", Activator.CreateInstance(Tip("Kalender.Desktop.WallpaperCanvas"), true)!);
        AlanAta(y, "_screens", Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Desktop.EkrandakiTakvim")))!);
        AlanAta(y, "_rebuildTimer", new DispatcherTimer());
        AlanAta(y, "_watchdogTimer", new DispatcherTimer());
        AlanAta(y, "_tazelemeTimer", new DispatcherTimer());
        AlanAta(y, "_bilinenKimlikler", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        AlanAta(y, "_monitors", Liste());
        AlanAta(y, "_data", Tip("Kalender.Calendar.CalendarDataSnapshot").GetProperty("Empty")!.GetValue(null)!);
        AlanAta(y, "_today", new DateOnly(2026, 10, 12));
        AlanAta(y, "_viewMonth", new DateOnly(2026, 10, 1));
        AlanAta(y, "_configSignature", string.Empty);
        return y;
    }

    /// <summary>
    /// Kimliği okunamamış görünen hayali bir ekran: katman onun için ayarlara
    /// kayıt açmaz, yani ayar dosyası hiç yazılmaz.
    /// </summary>
    private static object Ekran(string aygit, int x, int g, int y)
    {
        object m = Activator.CreateInstance(Tip("Kalender.Interop.MonitorTarget"), true)!;
        Yaz(m, "Handle", IntPtr.Zero);
        Yaz(m, "DeviceName", aygit);
        Yaz(m, "StableId", aygit);
        Yaz(m, "KimlikOkunamadi", true);
        Yaz(m, "Bounds", new DRectangle(x, 0, g, y));
        Yaz(m, "WorkArea", new DRectangle(x, 0, g, y - 48));
        Yaz(m, "Dpi", 96u);
        Yaz(m, "IsPrimary", x == 0);
        return m;
    }

    private static object Liste(params object[] ekranlar)
    {
        var liste = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Interop.MonitorTarget")))!;
        foreach (object m in ekranlar)
        {
            liste.Add(m);
        }

        return liste;
    }

    /// <summary>Windows'un duvar kâğıdı arayüzünün sahtesi; aygıt yolları verilir, ekranlar boş başlar.</summary>
    private static SahteDuvarKagidi Sahte(params string[] aygitlar)
    {
        var s = (SahteDuvarKagidi)DispatchProxy.Create(Tip("Kalender.Interop.IDesktopWallpaper"), typeof(SahteDuvarKagidi));
        s.Aygitlar.AddRange(aygitlar);
        foreach (string a in aygitlar)
        {
            s.Ekranda[a] = string.Empty;
        }

        return s;
    }

    /// <summary>Dağıtıcıda bekleyen işleri çalıştırır.</summary>
    private static void Bosalt()
    {
        var cerceve = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => cerceve.Continue = false));
        Dispatcher.PushFrame(cerceve);
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

    private static string? DosyaOku(string yol) => File.Exists(yol) ? Convert.ToHexString(File.ReadAllBytes(yol)) : null;

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        PropertyInfo kok = Tip("Kalender.Core.Paths").GetProperty("RootFolder", Hepsi)!;
        string gercekKok = (string)kok.GetValue(null)!;
        string ayarDosyasi = Path.Combine(gercekKok, "ayarlar.json");
        string yedekDosyasi = Path.Combine(gercekKok, "onceki-duvar-kagidi.json");
        string? ayarOnce = DosyaOku(ayarDosyasi);
        string? yedekOnce = DosyaOku(yedekDosyasi);

        string gecici = Path.Combine(Path.GetTempPath(), "Kalender Kod İncelemesi ğüşıöç " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gecici);
        kok.SetValue(null, gecici);

        try
        {
            Bolum(Madde32);
            Bolum(Madde4);
            Bolum(Madde3);
            Bolum(() => Madde6(gecici));
            Bolum(Madde25);
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

        Denetle("kurulu Kalender'in ayar dosyasına ve duvar kâğıdı yedeğine dokunulmadı",
            DosyaOku(ayarDosyasi) == ayarOnce && DosyaOku(yedekDosyasi) == yedekOnce);

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ 32. madde

    private static void Madde32()
    {
        Console.WriteLine("32) Ayarlar penceresindeki duvar kâğıdı anahtarı ayara eşitleniyor");
        Type pencereTipi = Tip("Kalender.Ui.SettingsWindow");
        Type depoTipi = Tip("Kalender.Settings.SettingsStore");

        bool Abone(MethodBase yontem, string olay)
        {
            List<Komut> k = Komutlar(yontem);
            int cagri = Bul(k, x => Cagri(x, depoTipi, olay));
            int isleyici = Bul(k, x => x.Kod == OpCodes.Ldftn && x.Uye?.Name == "OnSettingsChanged");
            return cagri > 0 && isleyici >= 0 && isleyici < cagri;
        }

        Denetle("pencere açılınca ayar değişikliklerine abone oluyor",
            Abone(pencereTipi.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(), "add_Changed"));
        Denetle("pencere kapanınca abonelik kalkıyor",
            Abone(pencereTipi.GetMethod("OnClosed", Hepsi)!, "remove_Changed"));

        // Pencerenin kendisi kurulmadan: yalnız anahtar ve işleyici.
        object pencere = RuntimeHelpers.GetUninitializedObject(pencereTipi);
        FieldInfo? dagitici = typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.NonPublic | BindingFlags.Instance);
        Denetle("pencere nesnesine dağıtıcı bağlanabildi", dagitici is not null);
        if (dagitici is null)
        {
            return;
        }

        dagitici.SetValue(pencere, Dispatcher.CurrentDispatcher);

        var kutu = new CheckBox { IsChecked = true };
        AlanAta(pencere, "WallpaperSyncBox", kutu);
        FieldInfo yukleniyor = pencereTipi.GetField("_loading", Hepsi)!;

        // Anahtarın kendi işleyicisi sessiz kipte (yükleme) değilken çağrılsaydı
        // ayara geri yazardı; burada yalnız sayılıyor.
        int sessizOlmayan = 0;
        System.Windows.RoutedEventHandler izle = (_, _) =>
        {
            if (!(bool)yukleniyor.GetValue(pencere)!)
            {
                sessizOlmayan++;
            }
        };
        kutu.Checked += izle;
        kutu.Unchecked += izle;

        object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;

        Yaz(ayarlar, "SyncSystemWallpaper", false);
        Cagir(pencere, "OnSettingsChanged", ayarlar);
        Bosalt();
        Denetle("tepsiden yazma kapatılınca açık penceredeki anahtar kapanıyor", kutu.IsChecked == false);

        Yaz(ayarlar, "SyncSystemWallpaper", true);
        Cagir(pencere, "OnSettingsChanged", ayarlar);
        Bosalt();
        Denetle("yazma açılınca anahtar açılıyor", kutu.IsChecked == true);
        Denetle("anahtar sessizce güncelleniyor (ayara geri yazılmıyor)",
            sessizOlmayan == 0 && !(bool)yukleniyor.GetValue(pencere)!, $"{sessizOlmayan} kez");
    }

    // ------------------------------------------------------------------- 4. madde

    private static void Madde4()
    {
        Console.WriteLine("4) Yazma kapalıyken takvimin eski yeri isabet vermiyor");
        object y = Yonetici();
        AlanAta(y, "_monitors", Liste(Ekran(@"\\.\DISPLAY1", 0, 2560, 1440)));

        // Yazma kapalı: görüntü üretilir, yalnız takvimin yeri tutulur.
        Cagir(y, "RefreshOnce");
        var ekranlar = (IList)AlanOku(y, "_screens");
        object? duzen = ekranlar.Count == 1 ? Oku(ekranlar[0]!, "Layout") : null;
        Denetle("takvimin yeri hesaplandı", duzen is not null);
        if (duzen is null)
        {
            return;
        }

        static DPoint Nokta(DPointF p) => new((int)p.X, (int)p.Y);
        var blok = (DRectangleF)Oku(duzen, "Bounds")!;
        var blokOrtasi = new DPoint((int)(blok.X + (blok.Width / 2)), (int)(blok.Y + (blok.Height / 2)));
        DPoint ok = Nokta((DPointF)Oku(duzen, "PrevNavCenter")!);
        DPoint gun = Nokta((DPointF)Oku(((IList)Oku(duzen, "Cells")!)[10]!, "Center")!);

        object ayarlar = Oku(AlanOku(y, "_settings"), "Current")!;

        // Ayar yalnız bellekte açılıyor; çizim çağrılmadığı için hiçbir şey yazılmaz.
        Yaz(ayarlar, "SyncSystemWallpaper", true);
        Denetle("yazma açıkken takvimin üstü isabet veriyor (sağ tık, ok, gün kutusu)",
            Cagir(y, "HitTestBlock", blokOrtasi) is not null
            && Cagir(y, "HitTestNav", ok)!.ToString() == "Previous"
            && Cagir(y, "HitTest", gun) is not null);

        Yaz(ayarlar, "SyncSystemWallpaper", false);
        Denetle("yazma kapalıyken sağ tık menüsü açılmıyor", Cagir(y, "HitTestBlock", blokOrtasi) is null);
        Denetle("yazma kapalıyken ok tıklaması yutulmuyor", Cagir(y, "HitTestNav", ok)!.ToString() == "None");
        Denetle("yazma kapalıyken gün kutusu açılmıyor", Cagir(y, "HitTest", gun) is null);
    }

    // ------------------------------------------------------------------- 3. madde

    private static void Madde3()
    {
        Console.WriteLine("3) Yazma kapatılınca takvim duvar kâğıdından kalkıyor; kapanış ekrana bakıyor");
        Type yoneticiTipi = Tip("Kalender.Desktop.DesktopLayerManager");
        Type tuvalTipi = Tip("Kalender.Desktop.WallpaperCanvas");
        Type servis = Tip("Kalender.Desktop.WallpaperService");
        Type ayarTipi = Tip("Kalender.Settings.AppSettings");
        Type depoTipi = Tip("Kalender.Settings.SettingsStore");

        // Davranış: geçici klasörde yedek yok; geri yükleme gerçek duvar kâğıdına
        // dokunmadan biter, yalnız zemin klasörünü temizler. Bu süreç önceden
        // yazmış gibi gösteriliyor: ekranda olmayan bir dosya, takvimdeki
        // ekranlarla eşleşmeyen bir kimlikle; takvimsiz zemin hiçbir ekrana yazılamaz.
        string zemin = (string)Tip("Kalender.Core.Paths").GetProperty("CanvasFolder")!.GetValue(null)!;
        Directory.CreateDirectory(zemin);
        string Artik(string ad)
        {
            string yol = Path.Combine(zemin, ad);
            File.WriteAllText(yol, "deneme");
            return yol;
        }

        object y = Yonetici();
        object tuval = AlanOku(y, "_canvas");
        AlanAta(y, "_monitors", Liste(Ekran(@"\\.\DISPLAY1", 0, 2560, 1440)));
        var son = (IDictionary)AlanOku(tuval, "_sonYazilanlar");
        PropertyInfo yazdi = tuvalTipi.GetProperty("HasPainted")!;

        son["MON-X"] = ValueTuple.Create((string?)null, @"C:\yok\zemin-deneme.bmp");
        yazdi.SetValue(tuval, true);
        string artik1 = Artik("zemin-deneme-1.bmp");
        Cagir(y, "RefreshOnce");
        Denetle("yazma kapalıyken ilk çizimde önceki duvar kâğıdı geri yükleniyor", !File.Exists(artik1));
        Denetle("geri yüklemeden sonra bu süreç ekranda bir şey tutmuyor (kapanışta zemin yazılmaz)",
            !(bool)yazdi.GetValue(tuval)! && son.Count == 0);

        string artik2 = Artik("zemin-deneme-2.bmp");
        Cagir(y, "RefreshOnce");
        Denetle("geri yükleme bir kez yapılıyor, sonraki çizimler yinelemiyor", File.Exists(artik2));

        yazdi.SetValue(tuval, true);
        AlanAta(y, "_geriYukleniyor", true);
        Cagir(y, "RefreshOnce");
        Denetle("geri yükleme sürerken gelen çizim yeniden geri yüklemiyor", File.Exists(artik2));
        AlanAta(y, "_geriYukleniyor", false);
        yazdi.SetValue(tuval, false);

        // Çizimdeki koşul (derlenmiş koddan): yazma kapalı, bu süreç yazmış,
        // geri yükleme sürmüyor.
        List<Komut> cizim = Komutlar(yoneticiTipi.GetMethod("RefreshOnce", Hepsi)!);
        int geri = Bul(cizim, x => Cagri(x, yoneticiTipi, "DuvarKagidiniGeriYukle"));
        List<Komut> once = geri < 0 ? new() : cizim.GetRange(Math.Max(0, geri - 12), Math.Min(12, geri));
        Denetle("çizimde geri yükleme yazma kapalıyken ve bu süreç yazmışsa yapılıyor",
            geri >= 0
            && once.Any(x => Cagri(x, ayarTipi, "get_SyncSystemWallpaper"))
            && once.Any(x => Cagri(x, tuvalTipi, "get_HasPainted"))
            && once.Any(x => AlanKomutu(x, OpCodes.Ldfld, "_geriYukleniyor")));

        // Geri yükleme: yedek geri konur; olmayan ya da geri konamayan ekrana
        // takvimsiz zemin yalnız takvim hâlâ duruyorsa yazılır; sonra bırakılır.
        MethodInfo kaldir = yoneticiTipi.GetMethod("DuvarKagidiniGeriYukle", Hepsi)!;
        List<Komut> k = Komutlar(kaldir);
        int bayrak = Bul(k, x => AlanKomutu(x, OpCodes.Stfld, "_geriYukleniyor"));
        int restore = Bul(k, x => Cagri(x, servis, "Restore"));
        int duranlar = Bul(k, x => Cagri(x, tuvalTipi, "EkrandaDuranlar"), restore);
        int zeminYaz = Bul(k, x => Cagri(x, yoneticiTipi, "PaintPlainBackground"), duranlar);
        int birak = Bul(k, x => Cagri(x, tuvalTipi, "Birak"), restore);
        int bayrakKalkar = Bul(k, x => AlanKomutu(x, OpCodes.Stfld, "_geriYukleniyor"), bayrak + 1);
        Denetle("geri yükleme: önce yedek, sonra takvimin kaldığı ekrana zemin, sonra bırakma",
            bayrak >= 0 && restore > bayrak && duranlar > restore && zeminYaz > duranlar && birak > zeminYaz,
            $"{bayrak} / {restore} / {duranlar} / {zeminYaz} / {birak}");
        Denetle("iç içe girme bayrağı her durumda (finally) iniyor",
            restore >= 0 && bayrakKalkar > restore && Icinde(Finally(kaldir, k[restore].Yer), k[bayrakKalkar].Yer));

        // Takvimsiz zemin: yalnız verilen ekranlara, yedeğe ve kaliteye dokunmadan.
        List<Komut> zeminK = Komutlar(yoneticiTipi.GetMethod("PaintPlainBackground", Hepsi)!);
        int uygula = Bul(zeminK, x => Cagri(x, tuvalTipi, "Apply"));
        Denetle("takvimsiz zemin yedek açmadan ve kaliteye dokunmadan yazılıyor",
            uygula > 0 && zeminK[uygula - 1].Kod == OpCodes.Ldc_I4_1);

        // Kapanış: ayara değil, ekranda bu sürecin görüntüsü durup durmadığına
        // bakılıyor; güncellemede (KeepWallpaperOnExit) hiç dokunulmuyor.
        List<Komut> kapanis = Komutlar(yoneticiTipi.GetMethod("Dispose", Hepsi)!);
        int birak2 = Bul(kapanis, x => Cagri(x, yoneticiTipi, "get_KeepWallpaperOnExit"));
        int yazdiMi = Bul(kapanis, x => Cagri(x, tuvalTipi, "get_HasPainted"));
        int ekrandakiler = Bul(kapanis, x => Cagri(x, tuvalTipi, "EkrandaDuranlar"));
        int kapanisZemin = Bul(kapanis, x => Cagri(x, yoneticiTipi, "PaintPlainBackground"));
        Denetle("kapanışta zemin yazma ayara bağlı değil",
            Bul(kapanis, x => Cagri(x, ayarTipi, "get_SyncSystemWallpaper")) < 0);
        Denetle("kapanışta önce güncelleme, sonra yazılmış mı, sonra ekrandakiler soruluyor",
            birak2 >= 0 && yazdiMi > birak2 && ekrandakiler > yazdiMi && kapanisZemin > ekrandakiler,
            $"{birak2} / {yazdiMi} / {ekrandakiler} / {kapanisZemin}");

        // Ekranda hâlâ duran görüntü: Windows'tan yalnız okunur.
        object okuyucu = Activator.CreateInstance(tuvalTipi, true)!;
        var okuyucuSon = (IDictionary)AlanOku(okuyucu, "_sonYazilanlar");
        Denetle("hiç yazılmadıysa ekranda bu sürecin görüntüsü yok",
            ((HashSet<string>)Cagir(okuyucu, "EkrandaDuranlar")!).Count == 0);

        Type arayuz = Tip("Kalender.Interop.IDesktopWallpaper");
        object gercek = Activator.CreateInstance(Tip("Kalender.Interop.DesktopWallpaperClass"), true)!;
        string aygit = (string)arayuz.GetMethod("GetMonitorDevicePathAt")!.Invoke(gercek, new object[] { 0u })!;
        string gorunen = (string?)arayuz.GetMethod("GetWallpaper")!.Invoke(gercek, new object?[] { aygit }) ?? string.Empty;
        okuyucuSon["MON-A"] = ValueTuple.Create((string?)aygit, gorunen);
        okuyucuSon["MON-B"] = ValueTuple.Create((string?)aygit, @"C:\yok\zemin-deneme.bmp");
        var duranEkranlar = (HashSet<string>)Cagir(okuyucu, "EkrandaDuranlar")!;
        Denetle("ekranda duran görüntü bulunuyor, başkası (geri yüklenen) sayılmıyor",
            duranEkranlar.Contains("MON-A") && !duranEkranlar.Contains("MON-B"));

        // Tepsideki "Eski duvar kâğıdıma dön": önce yazma kapanır, sonra katman geri yükler.
        Type hostTipi = Tip("Kalender.Core.AppHost");
        MethodInfo tepsi = hostTipi.GetMethod("RestoreWallpaper", Hepsi)!;
        List<Komut> t = Komutlar(tepsi);
        int guncelle = Bul(t, x => Cagri(x, depoTipi, "Update"));
        int katman = Bul(t, x => Cagri(x, yoneticiTipi, "DuvarKagidiniGeriYukle"));
        Komut? lambda = guncelle < 0 ? null : t.Take(guncelle).LastOrDefault(x => x.Kod == OpCodes.Ldftn);
        bool kapatiyor = false;
        if (lambda?.Uye is MethodBase govde)
        {
            List<Komut> g = Komutlar(govde);
            int ata = Bul(g, x => Cagri(x, ayarTipi, "set_SyncSystemWallpaper"));
            kapatiyor = ata > 0 && g[ata - 1].Kod == OpCodes.Ldc_I4_0;
        }

        Denetle("tepsiden geri dönüşte önce yazma kapanıyor, sonra katman geri yüklüyor",
            kapatiyor && guncelle >= 0 && katman > guncelle, $"{guncelle} / {katman}");
    }

    // ------------------------------------------------------------------- 6. madde

    private static void Madde6(string gecici)
    {
        Console.WriteLine("6) Geri yükleme ve yedek");
        Type servis = Tip("Kalender.Desktop.WallpaperService");
        Type yedekTipi = Tip("Kalender.Desktop.WallpaperBackup");
        Type yollar = Tip("Kalender.Core.Paths");
        string zemin = (string)yollar.GetProperty("CanvasFolder")!.GetValue(null)!;

        bool ZeminMi(string? yol) => (bool)Statik(servis, "ZeminDosyasiMi", yol)!;
        Denetle("zemin klasöründeki zemin-… dosyası takvimin kendi görüntüsü",
            ZeminMi(Path.Combine(zemin, "zemin-1a2b3c4d-20261012-101010-123.bmp"))
            && ZeminMi(Path.Combine(zemin.ToUpperInvariant(), "ZEMIN-1a2b3c4d.png")));
        Denetle("başka klasördeki ya da başka adlı dosya kullanıcının",
            !ZeminMi(@"C:\Users\deneme\Pictures\zemin-deniz.jpg") && !ZeminMi(Path.Combine(zemin, "deniz.jpg"))
            && !ZeminMi(null) && !ZeminMi(string.Empty));

        string fotolar = Path.Combine(gecici, "fotoğraflar");
        Directory.CreateDirectory(fotolar);
        string foto1 = Path.Combine(fotolar, "deniz.jpg");
        string foto2 = Path.Combine(fotolar, "dağ.jpg");
        string silinmis = Path.Combine(fotolar, "silinmiş.jpg");
        File.WriteAllText(foto1, "deneme");
        File.WriteAllText(foto2, "deneme");
        string bizim = Path.Combine(zemin, "zemin-aaaaaaaa-20261012-101010-123.bmp");

        // ------------------------------------------------ yedek alma
        SahteDuvarKagidi s = Sahte("A", "B");
        s.Ekranda["A"] = foto1;
        s.Ekranda["B"] = bizim;
        object? yedek = Statik(servis, "YedekHazirla", s);
        var ekranBasina = yedek is null ? null : (IDictionary<string, string>)Oku(yedek, "PerMonitor")!;
        Denetle("yedekte kullanıcının fotoğrafı var, takvimin kendi görüntüsü yok",
            ekranBasina is not null && ekranBasina["A"] == foto1 && !ekranBasina.ContainsKey("B")
            && (bool)Oku(yedek!, "WallpaperEnabled")!);

        s.Ekranda["A"] = bizim;
        s.Ortak = bizim;
        Denetle("ekranlarda yalnız takvimin görüntüsü varsa yedek yazılmıyor", Statik(servis, "YedekHazirla", s) is null);

        s.Ekranda["A"] = string.Empty;
        s.Ekranda["B"] = string.Empty;
        s.Ortak = string.Empty;
        object? duz = Statik(servis, "YedekHazirla", s);
        Denetle("düz renk kullananın yedeği yine yazılıyor", duz is not null && !(bool)Oku(duz, "WallpaperEnabled")!);

        // ------------------------------------------------ ekranları geri koyma
        object Yedek(string? ortak, params (string Aygit, string Yol)[] ekranlar)
        {
            object b = Activator.CreateInstance(yedekTipi, true)!;
            Yaz(b, "Wallpaper", ortak);
            Yaz(b, "Position", 2);
            var d = (IDictionary<string, string>)Oku(b, "PerMonitor")!;
            foreach ((string aygit, string yol) in ekranlar)
            {
                d[aygit] = yol;
            }

            return b;
        }

        SahteDuvarKagidi Takvimli(params string[] aygitlar)
        {
            SahteDuvarKagidi t = Sahte(aygitlar);
            foreach (string a in aygitlar)
            {
                t.Ekranda[a] = bizim;
            }

            return t;
        }

        bool GeriKoy(SahteDuvarKagidi t, object b) => (bool)Statik(servis, "EkranlariGeriYukle", t, b)!;

        s = Takvimli("A", "B", "C");
        bool tam = GeriKoy(s, Yedek(null, ("A", foto1), ("B", foto2)));
        Denetle("yedekteki ekranlar kendi fotoğrafına dönüyor", s.Ekranda["A"] == foto1 && s.Ekranda["B"] == foto2);
        Denetle("yedekten sonra takılan ekrana yedekteki ilk ekranın fotoğrafı konuyor, takvim kalmıyor",
            s.Ekranda["C"] == foto1 && tam);
        Denetle("yerleşim biçimi geri konuyor", s.Konum == 2);

        s = Takvimli("A", "B", "C");
        tam = GeriKoy(s, Yedek(foto2, ("A", foto1), ("B", foto1)));
        Denetle("yedekte olmayan ekrana önce ortak yol konuyor", s.Ekranda["C"] == foto2 && tam);

        s = Takvimli("A", "B", "C");
        s.Reddet.Add(foto1);
        tam = GeriKoy(s, Yedek(null, ("A", foto1), ("B", foto2), ("C", silinmis)));
        Denetle("bir ekrandaki hata öbür ekranları durdurmuyor", s.Ekranda["B"] == foto2 && !tam);
        Denetle("Windows'un kabul etmediği ekran olduğu gibi kalıyor", s.Ekranda["A"] == bizim);
        Denetle("dosyası silinmiş fotoğraf denenmiyor", s.Ekranda["C"] == bizim && s.Yazilanlar.All(w => w.Yol != silinmis));

        s = Takvimli("A");
        tam = GeriKoy(s, Yedek(foto1));
        Denetle("ekran başına kaydı olmayan yedekte ortak yol bütün ekranlara konuyor",
            tam && s.Yazilanlar.Count == 1 && s.Yazilanlar[0].Aygit is null && s.Yazilanlar[0].Yol == foto1);

        // ------------------------------------------------ zemin dosyalarının temizliği
        s = Sahte("A", "B");
        s.Ekranda["A"] = bizim;
        s.Ekranda["B"] = foto1;
        var ekrandakiler = (HashSet<string>?)Statik(servis, "EkrandakiDosyalar", s);
        Denetle("ekranlarda duran dosyalar okunuyor (büyük/küçük harf ayrımsız)",
            ekrandakiler is not null && ekrandakiler.Contains(bizim.ToUpperInvariant()) && ekrandakiler.Contains(foto1));
        s.OkumadaHata = true;
        Denetle("ekrandakiler okunamazsa bilinmiyor sayılıyor", Statik(servis, "EkrandakiDosyalar", s) is null);

        string klasor = Path.Combine(gecici, "zemin sınaması");
        Directory.CreateDirectory(klasor);
        string Dosya(string ad)
        {
            string yol = Path.Combine(klasor, ad);
            File.WriteAllText(yol, "deneme");
            return yol;
        }

        string ekranda = Dosya("zemin-aaaaaaaa-1.bmp");
        string eski1 = Dosya("zemin-aaaaaaaa-2.bmp");
        string eski2 = Dosya("zemin-bbbbbbbb-a.png");
        string baska = Dosya("deniz.jpg");
        Statik(servis, "ZeminDosyalariniSil", klasor, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ekranda.ToUpperInvariant() });
        Denetle("ekranda duran zemin dosyası silinmiyor", File.Exists(ekranda));
        Denetle("ekranda olmayan zemin dosyaları siliniyor (BMP ve eski PNG)", !File.Exists(eski1) && !File.Exists(eski2));
        Denetle("zemin olmayan dosyaya dokunulmuyor", File.Exists(baska));

        string eski3 = Dosya("zemin-cccccccc-3.bmp");
        Statik(servis, "ZeminDosyalariniSil", klasor, null);
        Denetle("ekranda ne durduğu bilinmiyorsa hiçbir dosya silinmiyor", File.Exists(eski3) && File.Exists(ekranda));

        // ------------------------------------------------ geri yükleme bütünü
        MethodInfo geriYukle = servis.GetMethod("Restore", Hepsi, new[] { typeof(bool).MakeByRefType() })!;
        List<Komut> k = Komutlar(geriYukle);
        int ekranlar = Bul(k, x => Cagri(x, servis, "EkranlariGeriYukle"));
        int yedekYolu = Bul(k, x => Cagri(x, yollar, "get_BackupFile"), ekranlar);
        Denetle("yedek kullanılınca siliniyor (yazma yeniden açılınca taze yedek alınır)",
            ekranlar >= 0 && yedekYolu > ekranlar && Cagri(k[yedekYolu + 1], typeof(File), "Delete"));
        Denetle("zemin temizliği ekrandakileri okuyarak yapılıyor",
            Komutlar(servis.GetMethod("ClearCanvasFiles", Hepsi)!).Any(x => Cagri(x, servis, "EkrandakiDosyalar")));

        // Geçici klasörde yedek yok: gerçek duvar kâğıdına dokunulmadan biter.
        object?[] cikti = { false };
        bool yuklendi = (bool)geriYukle.Invoke(null, cikti)!;
        Denetle("yedek yoksa geri yüklenmedi ve ekranda takvim kalmış olabilir deniyor", !yuklendi && (bool)cikti[0]!);
    }

    // ------------------------------------------------------------------ 25. madde

    private static void Madde25()
    {
        Console.WriteLine("25) Yazma yeniden açılınca sıkıştırma kalitesi yeniden denetleniyor");
        Type tuvalTipi = Tip("Kalender.Desktop.WallpaperCanvas");
        Type servis = Tip("Kalender.Desktop.WallpaperService");
        object tuval = Activator.CreateInstance(tuvalTipi, true)!;
        FieldInfo kalite = tuvalTipi.GetField("_qualityChecked", Hepsi)!;

        kalite.SetValue(tuval, true);
        Cagir(tuval, "Forget");
        Denetle("her şeyin yeniden yazılması kalite denetimini de yeniliyor", !(bool)kalite.GetValue(tuval)!);

        kalite.SetValue(tuval, true);
        Yaz(tuval, "HasPainted", true);
        ((IDictionary)AlanOku(tuval, "_hashes"))["MON-A"] = "özet";
        ((IDictionary)AlanOku(tuval, "_sonYazilanlar"))["MON-A"] = ValueTuple.Create((string?)null, "zemin-deneme.bmp");
        Cagir(tuval, "Birak");
        Denetle("geri yüklemeden sonra her şey (kalite dâhil) baştan yazılacak",
            !(bool)kalite.GetValue(tuval)! && !(bool)Oku(tuval, "HasPainted")!
            && ((IDictionary)AlanOku(tuval, "_hashes")).Count == 0
            && ((IDictionary)AlanOku(tuval, "_sonYazilanlar")).Count == 0);

        // Takvim kaldırılırken yazılan zemin yedeğe ve kaliteye dokunmuyor:
        // "kaldirirken" doğruysa ikisini de atlayan dal.
        List<Komut> k = Komutlar(tuvalTipi.GetMethod("Apply", Hepsi)!);
        int yedek = Bul(k, x => Cagri(x, servis, "EnsureBackup"));
        int kaliteCagri = Bul(k, x => Cagri(x, tuvalTipi, "EnsureImportQuality"));
        int dal = Bul(k, x => x.Kod == OpCodes.Ldarg_2);
        Komut? atla = dal >= 0 && dal + 1 < k.Count ? k[dal + 1] : null;
        Denetle("takvim kaldırılırken yedek ve kalite atlanıyor",
            atla is not null && (atla.Kod == OpCodes.Brtrue || atla.Kod == OpCodes.Brtrue_S)
            && yedek > dal && kaliteCagri > dal && k[yedek].Yer < atla.Hedef && k[kaliteCagri].Yer < atla.Hedef);
    }
}

/// <summary>
/// Windows'un duvar kâğıdı arayüzünün (IDesktopWallpaper) sahtesi: gerçek
/// masaüstüne dokunmadan yedekleme ve geri yükleme mantığını sınamak için.
/// </summary>
public class SahteDuvarKagidi : DispatchProxy
{
    /// <summary>Bağlı ekranların aygıt yolları, sırasıyla.</summary>
    public List<string> Aygitlar { get; } = new();

    /// <summary>Aygıt yolu -> ekranda duran duvar kâğıdı.</summary>
    public Dictionary<string, string> Ekranda { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ortak yol (aygıt yolu verilmeden okunan).</summary>
    public string Ortak { get; set; } = string.Empty;

    /// <summary>Windows'un kabul etmediği yollar: SetWallpaper bunlarda hata verir.</summary>
    public HashSet<string> Reddet { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Doğruysa GetWallpaper hata verir.</summary>
    public bool OkumadaHata { get; set; }

    /// <summary>SetWallpaper çağrıları, sırasıyla.</summary>
    public List<(string? Aygit, string Yol)> Yazilanlar { get; } = new();

    public int Konum { get; set; } = 4;

    public uint Renk { get; set; }

    protected override object? Invoke(MethodInfo? yontem, object?[]? a)
    {
        switch (yontem!.Name)
        {
            case "GetMonitorDevicePathCount":
                return (uint)Aygitlar.Count;

            case "GetMonitorDevicePathAt":
                return Aygitlar[(int)(uint)a![0]!];

            case "GetWallpaper":
                if (OkumadaHata)
                {
                    throw new COMException("Duvar kâğıdı okunamadı (deneme).", unchecked((int)0x80004005));
                }

                return a![0] is string aygit ? (Ekranda.TryGetValue(aygit, out string? yol) ? yol : string.Empty) : Ortak;

            case "SetWallpaper":
                string yeni = (string)a![1]!;
                if (Reddet.Contains(yeni))
                {
                    throw new COMException("Dosya bulunamadı (deneme).", unchecked((int)0x80070002));
                }

                Yazilanlar.Add(((string?)a[0], yeni));
                if (a[0] is string hedef)
                {
                    Ekranda[hedef] = yeni;
                }
                else
                {
                    Ortak = yeni;
                    foreach (string ekran in Aygitlar)
                    {
                        Ekranda[ekran] = yeni;
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
}
