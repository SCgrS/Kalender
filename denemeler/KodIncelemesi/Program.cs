using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using DBitmap = System.Drawing.Bitmap;
using DBrushes = System.Drawing.Brushes;
using DColor = System.Drawing.Color;
using DGraphics = System.Drawing.Graphics;
using DImage = System.Drawing.Image;
using DImageFormat = System.Drawing.Imaging.ImageFormat;
using DPixelFormat = System.Drawing.Imaging.PixelFormat;
using DSize = System.Drawing.Size;
using DPoint = System.Drawing.Point;
using DPointF = System.Drawing.PointF;
using DRectangle = System.Drawing.Rectangle;
using DRectangleF = System.Drawing.RectangleF;

// Kalender'de genel kod incelemesinin (2026-10-12) düzeltmelerinin sınaması
// (yansımayla). Madde numaraları kalender.md'deki listeden:
//   3. "Takvimi Windows duvar kâğıdına işle" kapatılınca önceki duvar kâğıdı
//      geri yüklenir (bir kez); açılıştaki ilk çizimde yazma kapalıysa ekranda
//      kalmış takvime de bakılır (önceki sürümlerden); kapanışta takvimsiz zemin
//      ayara değil, ekranda hâlâ bu sürecin görüntüsü durup durmadığına bakarak
//      yazılır; tepsideki "Eski duvar kâğıdıma dön" önce yazmayı kapatır; yedek
//      yokken ekranda takvim de yoksa geri dönüş başarılı sayılır.
//   4. Yazma kapalıyken takvimin eski yeri sağ tık, ok ve gün kutusu için
//      isabet vermez.
//   5. Kurucu kurulu exe'nin sürümüne bakar: kurulu sürüm daha yeniyse kurmaz
//      (pencere, kurulum ve güncelleme yolu), aynı sürümde "Yeniden kur" der.
//      (Bilinmeyen ayar alanlarının korunması Guncelleme sınamasında.)
//   6. Geri yükleme: yedekte olmayan ekrana ortak yol ya da yedekteki ilk
//      ekranın yolu konur, kendi fotoğrafı konamayan ekrana da (dosyası olmayan
//      yol yerine geçmez); bir ekrandaki hata öbürlerini durdurmaz; dosyası
//      olmayan fotoğraf denenmez; ekranda duran zemin dosyası silinmez (kaldırmada
//      da); takvimin kendi görüntüsü yedeğe yazılmaz; yedek kullanılınca silinir,
//      hiçbir ekrana geri konamazsa bekler; takvimsiz zemin yazılırken geri konan
//      yerleşim (Yay dışında) korunur.
//   7. Ortala kipinde fotoğraf piksel boyutuyla, tam ortada çizilir (fotoğrafın
//      ve yüzeyin DPI'ı etkilemez); Döşe de piksel boyutuyla.
//   8. Fotoğrafın EXIF yön etiketi uygulanır (büyük ve küçük sonlu), etiket
//      kalkar, ön bellekte döndürülmüş hâli durur.
//  10. Ayarlar > Google'da bütün takvim ya da görev listesi anahtarları
//      kapatılınca seçime "hiçbiri" işareti yazılır (boş liste yine "hepsi");
//      pencere yeniden açılınca anahtarlar kapalı kurulur. (Eşitlemenin bu
//      işarete uyması GoogleEsitleme sınamasında.)
//  12.Gün kutusu ölçeği farklı ekranda da hücrenin altında açılır: boyutu
//      hücrenin ekranının ölçeğiyle hesaplanır, ölçek değişiminden sonra yeri
//      WPF'in taşımasından sonra yeniden konur; kapalı kutu görünür olmaz
//      (görünmeyen ayrı bir Windows masaüstünde, programın ikinci kopyasında).
//  13. Ayar dosyası önce geçici dosyaya yazılıp diske boşaltılır, önceki
//      hâli ayarlar.json.yedek olur; okunamayan dosya ayarlar.bozuk-<tarih>.json
//      adıyla kenara alınır (üstüne yazılmaz), yerine yedek okunur; 1.10'dan
//      kalan düz metin gizli anahtar yedekte de kalmaz.
//  18. Tatil listesi akıştaki bütün yılları saklar (yalnız 14 ayı değil); ‹ ›
//      ile uzak aylara gidince de tatiller kırmızı. 1.12'nin önbelleği okunur,
//      30 gün beklenmeden koşulsuz istekle yeniden indirilir; yeni dosyayı eski
//      sürüm de okuyabilir. Google'ın akışı yerine bu programdaki sahtesi.
//  19. Ayarlardan açılan "Windows ile başlat", Windows'un başlangıç
//      listesindeki kapalı işaretini kaldırır; açılıştaki eşitleme kaldırmaz.
//  20. Esc ya da × ile kapatınca istemci kimliği ve gizli anahtar kaydedilir.
//  23. Menü açıkken ay okuna basılınca önce menü kapanır, ay sonra değişir.
//  24. Tuş basılıyken imleç takvimden çıkarsa fare kancası yuttuğu basışın
//      bırakışını bekler (bırakış kabuğa gitmez, sağ tıkta menü basılan noktada
//      açılır), sonra sökülür; ay değişirken yine hemen sökülür.
//  25. Yazma yeniden açılınca sıkıştırma kalitesi yeniden denetlenir.
//  26. Duvar kâğıdına yazımlar durunca, bekleme süresi dolduğunda eski zemin
//      dosyaları silinir; ekranda duran dosyaya dokunulmaz.
//  27. Fotoğraf açılamazsa takvim düz zemine göre (yazı rengi otomatik)
//      çizilir, karartma yapılmaz; günlüğe her dosya için bir kez hata yazılır.
//  28. Ekrana özel fotoğraf da genel Yerleşim seçimiyle çizilir.
//  29. Fotoğraf ön belleği birden çok fotoğraf tutar; turda çizilmeyen atılır.
//  30. Fotoğraf yalnız bazı ekranlardaysa öbürlerinin teması (yerleştirme
//      ekranında da) düz zemine göre kurulur; ayarlardaki Otomatik anahtarı
//      fotoğrafsız ekran varken açık kalır.
//  31. Duvar kâğıdını etkilemeyen ayarlar yalnızca kaydedilir; ayarlar
//      penceresini kapatmak ancak bekleyen bir değişiklik varsa uygular.
//  32. Ayarlar penceresi açıkken ayar başka yerden değişince duvar kâğıdı
//      anahtarı ona eşitlenir.
//  34. Saat değişiminde (WM_TIMECHANGE) .NET'in saat dilimi önbelleği atılır:
//      gün ve gece yarısı zamanlayıcısı yeni dilime göre, dilim değişimi
//      bildirilir; uygulama Google verisini yeni dilimle baştan alır. Windows'un
//      dilimi değiştirilmez: .NET'in önbelleğine sahte bir dilim konur.
//  36. Windows'un sessizce söktüğü fare kancası anlaşılır (imleç oynadığı
//      hâlde kanca çağrılmadı) ve yeniden takılır. Fare kancası hiç takılmaz;
//      yapay olaylar doğrudan kancanın işleyicisine verilir.
//  37. Ayarlar penceresi ekran satırlarını kurarken ayarlara kayıt açmaz;
//      kayıt, kullanıcı satırda bir şey değiştirince açılır.
//
// Kayıt defterinde yalnızca HKCU altında bu sınamanın açtığı geçici bir anahtara
// yazılır ve sonunda silinir; gerçek açılış kaydına ve Windows'un başlangıç
// listesine dokunulmaz (sonda denetlenir). Kurucu ve kurulu uygulama hiç
// başlatılmaz. Gerçek duvar kâğıdına yazılmaz: Windows'un duvar kâğıdı arayüzü yerine bu
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
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 2 && args[0] == "--gun-kutusu")
        {
            return GunKutusuKopyasi(args[1]);
        }

        PropertyInfo kok = Tip("Kalender.Core.Paths").GetProperty("RootFolder", Hepsi)!;
        string gercekKok = (string)kok.GetValue(null)!;
        string ayarDosyasi = Path.Combine(gercekKok, "ayarlar.json");
        string yedekDosyasi = Path.Combine(gercekKok, "onceki-duvar-kagidi.json");
        string? ayarOnce = DosyaOku(ayarDosyasi);
        string? yedekOnce = DosyaOku(yedekDosyasi);
        string? ayarYedegiOnce = DosyaOku(ayarDosyasi + ".yedek");

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
            Bolum(() => Madde5(gecici));
            Bolum(Madde19);
            Bolum(Madde13);
            Bolum(() => Madde20(gecici));
            Bolum(Madde31);
            Bolum(Madde37);
            Bolum(Madde10);
            Bolum(() => Madde12(gecici));
            Bolum(Madde23);
            Bolum(Madde24);
            Bolum(Madde36);
            Bolum(Madde18);
            Bolum(Madde34);

            string fotograflar = Path.Combine(gecici, "fotoğraflar");
            Directory.CreateDirectory(fotograflar);
            Bolum(() => Madde7(fotograflar));
            Bolum(() => Madde8(fotograflar));
            Bolum(() => Madde27(fotograflar));
            Bolum(() => Madde28(fotograflar));
            Bolum(() => Madde29(fotograflar));
            Bolum(() => Madde30(fotograflar));
            Bolum(Madde26);
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

        Denetle("kurulu Kalender'in ayar dosyasına (yedeği dâhil) ve duvar kâğıdı yedeğine dokunulmadı",
            DosyaOku(ayarDosyasi) == ayarOnce && DosyaOku(ayarDosyasi + ".yedek") == ayarYedegiOnce
            && DosyaOku(yedekDosyasi) == yedekOnce);

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

        // Açılış: hiç yazmamış süreç, yazma kapalı. Gerçek ekran yalnız okunuyor;
        // uygulamanın klasörü geçici olduğu için oradaki görüntü takvimin zemini
        // sayılmaz, geri yükleme yapılmamalı.
        object acilis = Yonetici();
        AlanAta(acilis, "_monitors", Liste(Ekran(@"\\.\DISPLAY1", 0, 2560, 1440)));
        string artik3 = Artik("zemin-deneme-3.bmp");
        Cagir(acilis, "RefreshOnce");
        Denetle("açılışta ekranda takvimin zemini yoksa geri yükleme yapılmıyor (gerçek ekran yalnız okunuyor)",
            File.Exists(artik3) && (bool)AlanOku(acilis, "_acilistaBakildi"));

        // Çizimdeki koşul (derlenmiş koddan): yazma kapalı, geri yükleme sürmüyor;
        // bu süreç yazmış ya da ilk çizimde ekranda takvimin zemini duruyor.
        List<Komut> cizim = Komutlar(yoneticiTipi.GetMethod("RefreshOnce", Hepsi)!);
        int geri = Bul(cizim, x => Cagri(x, yoneticiTipi, "DuvarKagidiniGeriYukle"));
        int bakildi = Bul(cizim, x => AlanKomutu(x, OpCodes.Stfld, "_acilistaBakildi"));
        List<Komut> once = geri < 0 || bakildi < 0 || bakildi > geri ? new() : cizim.GetRange(bakildi, geri - bakildi);
        int yazmis = once.FindIndex(x => Cagri(x, tuvalTipi, "get_HasPainted"));
        int zeminVar = once.FindIndex(x => Cagri(x, servis, "EkrandaZeminVar"));
        Denetle("çizimde geri yükleme yazma kapalıyken ve bu süreç yazmışsa yapılıyor",
            geri >= 0
            && once.Any(x => Cagri(x, ayarTipi, "get_SyncSystemWallpaper"))
            && yazmis >= 0
            && once.Any(x => AlanKomutu(x, OpCodes.Ldfld, "_geriYukleniyor")));
        Denetle("açılıştaki ilk çizimde ekranda kalmış takvime de bakılıyor, yalnız bir kez",
            bakildi > 0 && cizim[bakildi - 1].Kod == OpCodes.Ldc_I4_1
            && Bul(cizim, x => AlanKomutu(x, OpCodes.Ldfld, "_acilistaBakildi")) is int okuma && okuma >= 0 && okuma < bakildi
            && zeminVar > yazmis);

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

        MethodInfo geriKoyma = servis.GetMethod("EkranlariGeriYukle", Hepsi)!;
        int konan = -1;
        bool GeriKoy(SahteDuvarKagidi t, object b)
        {
            object?[] a = { t, b, 0 };
            bool hepsi = (bool)geriKoyma.Invoke(null, a)!;
            konan = (int)a[2]!;
            return hepsi;
        }

        s = Takvimli("A", "B", "C");
        bool tam = GeriKoy(s, Yedek(null, ("A", foto1), ("B", foto2)));
        Denetle("yedekteki ekranlar kendi fotoğrafına dönüyor", s.Ekranda["A"] == foto1 && s.Ekranda["B"] == foto2);
        Denetle("yedekten sonra takılan ekrana yedekteki ilk ekranın fotoğrafı konuyor, takvim kalmıyor",
            s.Ekranda["C"] == foto1 && tam && konan == 3);
        Denetle("yerleşim biçimi geri konuyor", s.Konum == 2);

        s = Takvimli("A", "B", "C");
        tam = GeriKoy(s, Yedek(foto2, ("A", foto1), ("B", foto1)));
        Denetle("yedekte olmayan ekrana önce ortak yol konuyor", s.Ekranda["C"] == foto2 && tam);

        s = Takvimli("A", "B", "C");
        s.Reddet.Add(foto1);
        tam = GeriKoy(s, Yedek(null, ("A", foto1), ("B", foto2), ("C", silinmis)));
        Denetle("bir ekrandaki hata öbür ekranları durdurmuyor", s.Ekranda["B"] == foto2 && !tam && konan == 1);
        Denetle("Windows'un kabul etmediği ekran olduğu gibi kalıyor", s.Ekranda["A"] == bizim);
        Denetle("dosyası silinmiş fotoğraf denenmiyor", s.Ekranda["C"] == bizim && s.Yazilanlar.All(w => w.Yol != silinmis));

        s = Takvimli("A");
        tam = GeriKoy(s, Yedek(foto1));
        Denetle("ekran başına kaydı olmayan yedekte ortak yol bütün ekranlara konuyor",
            tam && konan == 1 && s.Yazilanlar.Count == 1 && s.Yazilanlar[0].Aygit is null && s.Yazilanlar[0].Yol == foto1);

        s = Takvimli("A", "B");
        tam = GeriKoy(s, Yedek(null, ("A", silinmis), ("B", foto2)));
        Denetle("kendi fotoğrafı silinmiş ekrana öbür ekranın fotoğrafı konuyor, takvimin zemini kalmıyor",
            tam && konan == 2 && s.Ekranda["A"] == foto2 && s.Ekranda["B"] == foto2);
        Denetle("dosyası olmayan yol yerine geçmiyor", s.Yazilanlar.All(w => w.Yol != silinmis));

        s = Takvimli("A");
        s.Reddet.Add(foto2);
        tam = GeriKoy(s, Yedek(foto1, ("A", foto2)));
        Denetle("Windows'un kabul etmediği fotoğrafın yerine ortak yol konuyor", tam && s.Ekranda["A"] == foto1);

        s = Takvimli("A", "B");
        tam = GeriKoy(s, Yedek(null, ("A", silinmis), ("B", silinmis + "2")));
        Denetle("hiçbir ekran geri konamayınca sayı sıfır, ekranlar olduğu gibi",
            !tam && konan == 0 && s.Ekranda["A"] == bizim && s.Ekranda["B"] == bizim);

        // ------------------------------------------------ ekranda takvimin zemini
        s = Sahte("A", "B");
        s.Ekranda["A"] = foto1;
        bool? ZeminVar(SahteDuvarKagidi t) => (bool?)Statik(servis, "EkrandaZeminVar", t);
        Denetle("ekranlarda kullanıcının duvar kâğıdı varken takvimin zemini yok sayılıyor", ZeminVar(s) == false);
        s.Ekranda["B"] = bizim.ToUpperInvariant();
        Denetle("bir ekranda takvimin zemini duruyor", ZeminVar(s) == true);
        s.OkumadaHata = true;
        Denetle("ekrandakiler okunamazsa bilinmiyor", ZeminVar(s) is null);

        // ------------------------------------------------ takvimsiz zeminde yerleşim
        Type tuvalTipi = Tip("Kalender.Desktop.WallpaperCanvas");
        Type konumTipi = Tip("Kalender.Interop.DesktopWallpaperPosition");
        bool Cek(string konum, bool kaldirirken)
            => (bool)Statik(tuvalTipi, "DoldurYerlesimineCekilsinMi", Enum.Parse(konumTipi, konum), kaldirirken)!;
        Denetle("takvim yazılırken yerleşim Doldur'a çekiliyor",
            new[] { "Center", "Tile", "Stretch", "Fit", "Span" }.All(ad => Cek(ad, false)) && !Cek("Fill", false));
        Denetle("takvim kaldırılırken geri konan yerleşim korunuyor (Ortala, Döşe, Uzat, Sığdır)",
            new[] { "Center", "Tile", "Stretch", "Fit", "Fill" }.All(ad => !Cek(ad, true)));
        Denetle("takvim kaldırılırken Yay Doldur'a çekiliyor (ekran boyutundaki görüntüde fark eden tek yerleşim)",
            Cek("Span", true));
        List<Komut> uygula = Komutlar(tuvalTipi.GetMethod("Apply", Hepsi)!);
        int karar = Bul(uygula, x => Cagri(x, tuvalTipi, "DoldurYerlesimineCekilsinMi"));
        Denetle("yazımda yerleşim kararı takvimin kaldırılıp kaldırılmadığına bakıyor",
            karar > 0 && uygula[karar - 1].Kod == OpCodes.Ldarg_2);

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

        // ------------------------------------------------ yedeğin kaldırılması ve bekleyen yedek
        // Uygulamanın klasörü geçici: yedek dosyaları oraya yazılıyor.
        string yedekDosyasi = (string)yollar.GetProperty("BackupFile")!.GetValue(null)!;
        string bekleyen = (string)servis.GetProperty("BekleyenYedekDosyasi", Hepsi)!.GetValue(null)!;
        Denetle("bekleyen yedek uygulamanın klasöründe, ayrı adla",
            Path.GetDirectoryName(bekleyen) == Path.GetDirectoryName(yedekDosyasi) && bekleyen != yedekDosyasi);

        File.WriteAllText(yedekDosyasi, "{}");
        Statik(servis, "YedegiKaldir", yedekDosyasi, false);
        Denetle("hiçbir ekrana geri konamayan yedek silinmiyor, bekliyor", !File.Exists(yedekDosyasi) && File.Exists(bekleyen));
        Statik(servis, "YedegiKaldir", bekleyen, false);
        Denetle("bekleyen yedek yine geri konamazsa bekliyor", File.Exists(bekleyen));

        s = Takvimli("A", "B");
        Statik(servis, "EnsureBackup", s);
        Denetle("ekranda yalnız takvim varken bekleyen yedek yeniden geçerli oluyor",
            File.Exists(yedekDosyasi) && !File.Exists(bekleyen) && File.ReadAllText(yedekDosyasi) == "{}");

        Statik(servis, "YedegiKaldir", yedekDosyasi, false);
        s = Takvimli("A", "B");
        s.Ekranda["A"] = foto1;
        Statik(servis, "EnsureBackup", s);
        object? taze = File.Exists(yedekDosyasi) ? JsonSerializer.Deserialize(File.ReadAllText(yedekDosyasi), yedekTipi) : null;
        Denetle("ekranda kullanıcının duvar kâğıdı varsa bekleyenin yerine o yedekleniyor",
            taze is not null && !File.Exists(bekleyen)
            && ((IDictionary<string, string>)Oku(taze, "PerMonitor")!).TryGetValue("A", out string? tazeYol) && tazeYol == foto1);

        Statik(servis, "YedegiKaldir", yedekDosyasi, true);
        Denetle("önceki duvar kâğıdı geri gelince yedek siliniyor", !File.Exists(yedekDosyasi) && !File.Exists(bekleyen));

        // ------------------------------------------------ geri yükleme bütünü
        MethodInfo geriYukle = servis.GetMethod("Restore", Hepsi, new[] { typeof(bool).MakeByRefType() })!;
        List<Komut> k = Komutlar(geriYukle);
        int ekranlar = Bul(k, x => Cagri(x, servis, "EkranlariGeriYukle"));
        int kaldir = Bul(k, x => Cagri(x, servis, "YedegiKaldir"), ekranlar);
        Denetle("geri yüklemeden sonra yedek kaldırılıyor (yazma yeniden açılınca taze yedek alınır)",
            ekranlar >= 0 && kaldir > ekranlar);
        int bekleyenOku = Bul(k, x => Cagri(x, servis, "get_BekleyenYedekDosyasi"));
        int okuma = Bul(k, x => Cagri(x, typeof(File), "ReadAllText"));
        Denetle("yedek yoksa bekleyen yedekle geri yükleniyor", bekleyenOku >= 0 && okuma > bekleyenOku);
        Denetle("yedek yokken ekranda takvimin zemini duruyor mu diye bakılıyor",
            Bul(k, x => x.Kod == OpCodes.Ldftn && x.Uye?.Name == "ZeminDosyasiMi") is int zeminSor
            && zeminSor > Bul(k, x => Cagri(x, servis, "ClearCanvasFiles")) && zeminSor < okuma);
        Denetle("zemin temizliği ekrandakileri okuyarak yapılıyor",
            Komutlar(servis.GetMethod("ClearCanvasFiles", Hepsi)!).Any(x => Cagri(x, servis, "EkrandakiDosyalar")));

        // Geçici klasörde yedek yok: gerçek duvar kâğıdı yalnız okunur. Gerçek
        // ekrandaki görüntü bu klasörde olmadığı için takvimin zemini sayılmaz.
        Denetle("yedek dosyaları yok (gerçek duvar kâğıdına yazılamaz)", !File.Exists(yedekDosyasi) && !File.Exists(bekleyen));
        if (!File.Exists(yedekDosyasi) && !File.Exists(bekleyen))
        {
            object?[] cikti = { true };
            bool yuklendi = (bool)geriYukle.Invoke(null, cikti)!;
            Denetle("yedek yok ama ekranda takvim de yoksa önceki duvar kâğıdı yerinde sayılıyor (ikinci basışta uyarı yok)",
                yuklendi && !(bool)cikti[0]!);
        }

        // ------------------------------------------------ kaldırmada ekrandaki zemin
        Type kurucu = Tip("Kalender.Core.Installer");
        PropertyInfo kok = yollar.GetProperty("RootFolder", Hepsi)!;
        string asilKok = (string)kok.GetValue(null)!;
        string kaldirmaKoku = Path.Combine(gecici, "kaldırma kökü");
        try
        {
            kok.SetValue(null, kaldirmaKoku);
            foreach (string alt in new[] { "app", "logs", "zemin" })
            {
                Directory.CreateDirectory(Path.Combine(kaldirmaKoku, alt));
            }

            string ekrandaki = Path.Combine(kaldirmaKoku, "zemin", "zemin-aaaaaaaa-1.bmp");
            string ayar = Path.Combine(kaldirmaKoku, "ayarlar.json");
            File.WriteAllText(ekrandaki, "deneme");
            File.WriteAllText(ayar, "{}");

            var butun = (List<string>)Statik(kurucu, "VeriSilmeHedefleri", false)!;
            Denetle("ekranda takvimin zemini yoksa kök klasörün tamamı siliniyor",
                butun.Count == 1 && butun[0] == kaldirmaKoku && File.Exists(ayar));

            var hedefler = (List<string>)Statik(kurucu, "VeriSilmeHedefleri", true)!;
            Denetle("ekranda takvimin zemini duruyorsa zemin klasörü silinmiyor",
                hedefler.All(h => !string.Equals(Path.GetFileName(h), "zemin", StringComparison.OrdinalIgnoreCase))
                && File.Exists(ekrandaki));
            Denetle("öbür klasörler kapanışta, kökteki dosyalar hemen siliniyor",
                hedefler.Count == 2 && hedefler.Contains(Path.Combine(kaldirmaKoku, "app"))
                && hedefler.Contains(Path.Combine(kaldirmaKoku, "logs")) && !File.Exists(ayar));
        }
        finally
        {
            kok.SetValue(null, asilKok);
        }

        List<Komut> silme = Komutlar(kurucu.GetMethod("ScheduleFileRemoval", Hepsi)!);
        int bakis = Bul(silme, x => Cagri(x, servis, "EkrandaZeminVar"));
        Denetle("kaldırmada \"ayarlarımı da sil\" ekrandaki zemine bakıyor",
            bakis >= 0 && Bul(silme, x => Cagri(x, kurucu, "VeriSilmeHedefleri"), bakis) > bakis);
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

    // ------------------------------------------------------------------- 5. madde

    private static void Madde5(string gecici)
    {
        Console.WriteLine("5) Kurucu kurulu sürüme bakıyor: eskiye dönmüyor, aynı sürümde yeniden kuruyor");
        Type kurucu = Tip("Kalender.Core.Installer");
        Type pencereTipi = Tip("Kalender.Ui.SetupWindow");
        var bu = (Version)Tip("Kalender.Core.UpdateService").GetProperty("CurrentVersion")!.GetValue(null)!;
        string Kisa(Version v) => (string)Statik(kurucu, "VersionText", v)!;
        Version? SurumOku(string yol) => (Version?)Statik(kurucu, "ReadVersion", yol);

        // Sürümü belli dosyalar: uygulamanın kendisi (aynı sürüm), bu sınama
        // (1.0, daha eski), .NET'in kendi kütüphanesi (10.x, daha yeni).
        string ayni = Uyg.Location;
        string eski = typeof(Program).Assembly.Location;
        string yeni = typeof(object).Assembly.Location;
        Version? yeniSurum = SurumOku(yeni);
        Denetle("dosyanın sürümü üç parçalı okunuyor",
            SurumOku(ayni) == bu && SurumOku(eski) == new Version(1, 0, 0) && yeniSurum is not null && yeniSurum > bu,
            $"{SurumOku(ayni)} / {SurumOku(eski)} / {yeniSurum}");
        string bos = Path.Combine(gecici, "sürümsüz.exe");
        File.WriteAllBytes(bos, new byte[] { 0x4D, 0x5A });
        Denetle("dosya yoksa ya da sürüm bilgisi taşımıyorsa sürüm bilinmiyor",
            SurumOku(Path.Combine(gecici, "olmayan.exe")) is null && SurumOku(bos) is null);

        // Kurulu exe geçici klasörde (Paths.RootFolder çevrildi).
        string kuruluExe = (string)kurucu.GetProperty("InstalledExe")!.GetValue(null)!;
        Denetle("kurulu exe deneme klasöründe", kuruluExe.StartsWith(gecici, StringComparison.OrdinalIgnoreCase), kuruluExe);
        if (!kuruluExe.StartsWith(gecici, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(kuruluExe)!);
        Version? DahaYeni() => (Version?)kurucu.GetProperty("NewerInstalledVersion")!.GetValue(null);
        object kurulum = Enum.Parse(Tip("Kalender.Ui.SetupMode"), "Install");

        // Kurulu dosyayı koyar (null: kurulu değil), kurulum penceresini kurar
        // (gösterilmez), yazılarını ve düğmelerini döndürür.
        (string Baslik, string Govde, string Birinci, string Ikinci, bool Klasor, bool DahaYeniMi) Pencere(string? kaynak)
        {
            if (kaynak is null)
            {
                File.Delete(kuruluExe);
            }
            else
            {
                File.Copy(kaynak, kuruluExe, overwrite: true);
            }

            var p = (System.Windows.Window)Activator.CreateInstance(pencereTipi, Hepsi, null, new[] { kurulum }, null)!;
            try
            {
                return (((TextBlock)p.FindName("HeadingText")).Text, ((TextBlock)p.FindName("BodyText")).Text,
                    (string)((Button)p.FindName("PrimaryButton")).Content, (string)((Button)p.FindName("CancelButton")).Content,
                    ((System.Windows.FrameworkElement)p.FindName("PathBox")).Visibility == System.Windows.Visibility.Visible,
                    (bool)AlanOku(p, "_newerInstalled"));
            }
            finally
            {
                p.Close();
            }
        }

        var ilk = Pencere(null);
        Denetle("kurulu değilse: Kur", ilk.Baslik == "Kalender bu bilgisayara kurulacak" && ilk.Birinci == "Kur"
            && ilk.Ikinci == "Vazgeç" && DahaYeni() is null, ilk.Baslik);

        var eskiKurulu = Pencere(eski);
        Denetle("kurulu sürüm eskiyse: Güncelle, iki sürüm de yazıyor",
            eskiKurulu.Birinci == "Güncelle" && eskiKurulu.Baslik == "Kalender güncellenecek"
            && eskiKurulu.Govde.StartsWith($"Kurulu 1.0 sürümünün üzerine {Kisa(bu)} sürümü yazılacak") && DahaYeni() is null,
            eskiKurulu.Govde);

        var ayniKurulu = Pencere(ayni);
        Denetle("aynı sürüm kuruluysa: Yeniden kur",
            ayniKurulu.Birinci == "Yeniden kur" && ayniKurulu.Baslik == $"Kalender {Kisa(bu)} zaten kurulu"
            && !ayniKurulu.DahaYeniMi && DahaYeni() is null, ayniKurulu.Baslik);

        var bilinmeyen = Pencere(bos);
        Denetle("kurulu sürüm okunamazsa eskisi gibi: Güncelle",
            bilinmeyen.Birinci == "Güncelle" && bilinmeyen.Govde.StartsWith("Kurulu sürümün üzerine") && DahaYeni() is null,
            bilinmeyen.Govde);

        var yeniKurulu = Pencere(yeni);
        Denetle("kurulu sürüm daha yeniyse kurulum yok; kurulu olanı aç ya da kapat",
            yeniKurulu.DahaYeniMi && yeniKurulu.Birinci == "Kalender'i aç" && yeniKurulu.Ikinci == "Kapat" && !yeniKurulu.Klasor
            && yeniKurulu.Baslik == $"Bu bilgisayarda daha yeni bir sürüm ({Kisa(yeniSurum!)}) kurulu"
            && DahaYeni() == yeniSurum, yeniKurulu.Baslik);
        File.Delete(kuruluExe);

        // Derlenmiş koddan: düğme kurmadan kurulu olanı açıyor; kurulum ve
        // güncelleme yolu, çalışan Kalender'i kapatmadan ve dosyayı yazmadan önce bakıyor.
        MethodInfo dugme = pencereTipi.GetMethod("OnPrimary", Hepsi)!;
        List<Komut> d = Komutlar(dugme.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType.GetMethod("MoveNext", Hepsi)!);
        int bayrak = Bul(d, x => AlanKomutu(x, OpCodes.Ldfld, "_newerInstalled"));
        int ac = Bul(d, x => Cagri(x, kurucu, "LaunchInstalled"));
        int kur = Bul(d, x => Cagri(x, kurucu, "Install"));
        Denetle("pencerede daha yeni kuruluyken düğme kurulu olanı açıyor, kurmuyor",
            bayrak >= 0 && ac > bayrak && kur > ac, $"{bayrak} / {ac} / {kur}");

        List<Komut> kurma = Komutlar(kurucu.GetMethod("Install", Hepsi)!);
        int bak = Bul(kurma, x => Cagri(x, kurucu, "get_NewerInstalledVersion"));
        int kapat = Bul(kurma, x => Cagri(x, Tip("Kalender.Core.AppInstance"), "StopRunning"));
        int kopya = Bul(kurma, x => Cagri(x, typeof(File), "Copy"));
        Denetle("kurulum, çalışanı kapatmadan ve kopyalamadan önce sürüme bakıyor",
            bak >= 0 && kapat > bak && kopya > kapat, $"{bak} / {kapat} / {kopya}");

        List<Komut> g = Komutlar(typeof(Kalender.App).GetMethod("TryRunUpdate", Hepsi)!);
        int gBak = Bul(g, x => Cagri(x, kurucu, "get_NewerInstalledVersion"));
        int gAc = Bul(g, x => Cagri(x, kurucu, "LaunchInstalled"));
        int gKur = Bul(g, x => Cagri(x, kurucu, "Install"));
        Denetle("güncelleme yolunda daha yeni kuruluysa kurulu olan açılıyor, kurulmuyor",
            gBak >= 0 && gAc > gBak && gKur > gAc, $"{gBak} / {gAc} / {gKur}");
    }

    // ------------------------------------------------------------------ 19. madde

    private static void Madde19()
    {
        Console.WriteLine("19) Ayarlardan açılan \"Windows ile başlat\" Windows'taki kapalı işaretini kaldırıyor");
        Type sm = Tip("Kalender.Core.StartupManager");
        PropertyInfo runOzelligi = sm.GetProperty("RunKey", Hepsi)!;
        PropertyInfo onayOzelligi = sm.GetProperty("StartupApprovedKey", Hepsi)!;
        string gercekRun = (string)runOzelligi.GetValue(null)!;
        string gercekOnay = (string)onayOzelligi.GetValue(null)!;

        static object? DegerOku(string anahtar)
        {
            using RegistryKey? k = Registry.CurrentUser.OpenSubKey(anahtar);
            return k?.GetValue("Kalender");
        }

        static string Ozet(object? deger) => deger switch
        {
            null => "yok",
            byte[] b => Convert.ToHexString(b),
            _ => deger.ToString()!,
        };

        string runOnce = Ozet(DegerOku(gercekRun));
        string onayOnce = Ozet(DegerOku(gercekOnay));

        string kok = @"Software\Kalender Sınama " + Guid.NewGuid().ToString("N");
        string run = kok + @"\Run";
        string onay = kok + @"\StartupApproved\Run";
        runOzelligi.SetValue(null, run);
        onayOzelligi.SetValue(null, onay);
        try
        {
            MethodInfo uygula = sm.GetMethod("Apply", Hepsi, new[] { typeof(bool) })!;
            MethodInfo kullanici = sm.GetMethod("ApplyByUser", Hepsi)!;
            void Acilis(bool acik) => uygula.Invoke(null, new object[] { acik });
            void Ayarlar(bool acik) => kullanici.Invoke(null, new object[] { acik });

            // Görev Yöneticisi'nin yazdığı biçim: ilk bayt 3 = kapalı, ardından kapatıldığı an.
            void WindowsKapatti()
            {
                using RegistryKey k = Registry.CurrentUser.CreateSubKey(onay);
                k.SetValue("Kalender", new byte[] { 3, 0, 0, 0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0xD8, 0x01 },
                    RegistryValueKind.Binary);
            }

            string beklenen = "\"" + Environment.ProcessPath + "\"";

            WindowsKapatti();
            Acilis(true);
            Denetle("açılıştaki eşitleme açılış kaydını yazıyor, Windows'ta verilen kararı ezmiyor",
                DegerOku(run) as string == beklenen && DegerOku(onay) is byte[]);

            Ayarlar(true);
            Denetle("ayarlardan açınca Windows'taki kapalı işareti kalkıyor, açılış kaydı duruyor",
                DegerOku(run) as string == beklenen && DegerOku(onay) is null);

            WindowsKapatti();
            Ayarlar(false);
            Denetle("ayarlardan kapatınca açılış kaydı da işaret de siliniyor", DegerOku(run) is null && DegerOku(onay) is null);

            // Bulgudaki senaryo: Windows'ta kapatıldı, sonra ayarlarda kapatılıp açıldı.
            Acilis(true);
            WindowsKapatti();
            Ayarlar(false);
            Ayarlar(true);
            Denetle("Windows'ta kapatılıp ayarlarda kapatılıp açılınca açılışta yeniden başlıyor",
                DegerOku(run) as string == beklenen && DegerOku(onay) is null);

            WindowsKapatti();
            sm.GetMethod("Apply", Hepsi, new[] { typeof(bool), typeof(string) })!.Invoke(null, new object?[] { false, null });
            Denetle("kaldırmada işaret de siliniyor (Windows'un listesinde artık kalmıyor)",
                DegerOku(run) is null && DegerOku(onay) is null);
        }
        finally
        {
            runOzelligi.SetValue(null, gercekRun);
            onayOzelligi.SetValue(null, gercekOnay);
            Registry.CurrentUser.DeleteSubKeyTree(kok, throwOnMissingSubKey: false);
        }

        Denetle("gerçek açılış kaydına ve Windows'un başlangıç listesine dokunulmadı",
            Ozet(DegerOku(gercekRun)) == runOnce && Ozet(DegerOku(gercekOnay)) == onayOnce
            && Registry.CurrentUser.OpenSubKey(kok) is null);

        // Çağıranlar (derlenmiş koddan): ayarlar penceresi ApplyByUser'ı, açılış Apply'ı.
        List<Komut> anahtar = Komutlar(Tip("Kalender.Ui.SettingsWindow").GetMethod("OnStartupChanged", Hepsi)!);
        Denetle("ayarlardaki anahtar işareti de kaldıran yolu çağırıyor",
            Bul(anahtar, x => Cagri(x, sm, "ApplyByUser")) >= 0 && Bul(anahtar, x => Cagri(x, sm, "Apply")) < 0);
        List<Komut> acilis = Komutlar(Tip("Kalender.Core.AppHost").GetMethod("Start", Hepsi)!);
        Denetle("açılıştaki eşitleme işarete dokunmayan yolu çağırıyor",
            Bul(acilis, x => Cagri(x, sm, "Apply")) >= 0 && Bul(acilis, x => Cagri(x, sm, "ApplyByUser")) < 0);
    }

    // ------------------------------------------------------------------ 13. madde

    private static void Madde13()
    {
        Console.WriteLine("13) Ayar dosyası: yazım diske boşaltılıyor, okunamayan dosya kenara alınıp yedek okunuyor");
        PropertyInfo duzey = Tip("Kalender.Core.Log").GetProperty("Level", Hepsi)!;
        object oncekiDuzey = duzey.GetValue(null)!;
        string klasor = Path.Combine(Path.GetTempPath(), "Kalender Ayar Dosyası ğüşıöç " + Guid.NewGuid().ToString("N"));
        try
        {
            // Sınamanın bilerek bozduğu dosyalar günlüğe hata olarak yazılmasın (4 = kapalı).
            duzey.SetValue(null, Enum.ToObject(duzey.PropertyType, 4));
            Directory.CreateDirectory(Path.Combine(klasor, "dosya"));
            Madde13Dosya(Path.Combine(klasor, "dosya"));
            Directory.CreateDirectory(Path.Combine(klasor, "depo"));
            Madde13Depo(Path.Combine(klasor, "depo"));
        }
        finally
        {
            duzey.SetValue(null, oncekiDuzey);
            try
            {
                Directory.Delete(klasor, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   (deneme klasörü silinemedi: " + ex.Message + ")");
            }
        }
    }

    /// <summary>
    /// SettingsStore'un kendisiyle: uygulamanın klasörü bu bölüm boyunca
    /// <paramref name="klasor"/>'e çevrilir.
    /// </summary>
    private static void Madde13Depo(string klasor)
    {
        Type depoTipi = Tip("Kalender.Settings.SettingsStore");
        PropertyInfo kok = Tip("Kalender.Core.Paths").GetProperty("RootFolder", Hepsi)!;
        string onceki = (string)kok.GetValue(null)!;
        kok.SetValue(null, klasor);
        try
        {
            string dosya = Path.Combine(klasor, "ayarlar.json");
            string yedek = dosya + ".yedek";

            object YeniDepo()
            {
                object d = Activator.CreateInstance(depoTipi, nonPublic: true)!;
                Cagir(d, "Load");
                return d;
            }

            object depo = YeniDepo();
            Yaz(Oku(depo, "Current")!, "LogLevel", 2);
            Cagir(depo, "Save");
            Yaz(Oku(depo, "Current")!, "LogLevel", 3);
            Cagir(depo, "Save");
            Denetle("uygulamanın kaydında yedek oluşuyor",
                File.Exists(dosya) && File.Exists(yedek) && Duzey(Oku(YeniDepo(), "Current")) == 3);

            File.WriteAllBytes(dosya, Array.Empty<byte>());
            object depo2 = YeniDepo();
            Denetle("açılışta boş ayar dosyası yerine yedek okunuyor", Duzey(Oku(depo2, "Current")) == 2);
            Cagir(depo2, "Save");
            Denetle("ardından kaydedince kenara alınan dosya korunuyor, ayarlar yedekteki değerlerle",
                Bozuklar(klasor).Length == 1 && new FileInfo(Bozuklar(klasor)[0]).Length == 0
                && Duzey(Oku(YeniDepo(), "Current")) == 2);

            // 1.10'un düz metin yazdığı gizli anahtar: dönüşüm yedekte de düz metin bırakmıyor.
            const string Sir = "GOCSPX-duz-metin-deneme";
            File.WriteAllText(dosya, "{ \"LogLevel\": 3, \"Google\": { \"ClientId\": \"deneme\", \"ClientSecret\": \"" + Sir + "\" } }");
            object google = Oku(Oku(YeniDepo(), "Current")!, "Google")!;
            string[] sirli = Directory.GetFiles(klasor)
                .Where(f => File.ReadAllText(f).Contains(Sir, StringComparison.Ordinal)).ToArray();
            Denetle("düz metin gizli anahtar okundu, dosyaya şifreli yazıldı",
                (string?)Oku(google, "ClientSecret") == Sir
                && File.ReadAllText(dosya).Contains("ClientSecretSifreli", StringComparison.Ordinal));
            Denetle("klasördeki hiçbir dosyada (yedek dâhil) düz metin kalmadı", sirli.Length == 0,
                string.Join(", ", sirli.Select(Path.GetFileName)));
            Denetle("düz metinli yedek silindi", !File.Exists(yedek));
        }
        finally
        {
            kok.SetValue(null, onceki);
        }
    }

    // ------------------------------------------------------------------ 20. madde

    private static void Madde20(string gecici)
    {
        Console.WriteLine("20) Esc ya da × ile kapatınca istemci kimliği ve gizli anahtar kaydediliyor");
        PropertyInfo kok = Tip("Kalender.Core.Paths").GetProperty("RootFolder", Hepsi)!;
        string onceki = (string)kok.GetValue(null)!;
        string klasor = Path.Combine(gecici, "20 istemci bilgileri");
        Directory.CreateDirectory(klasor);
        kok.SetValue(null, klasor);
        try
        {
            string dosya = Path.Combine(klasor, "ayarlar.json");
            object depo = BellekDeposu();
            object google = Oku(Oku(depo, "Current")!, "Google")!;
            SahteAyarKapisi kapi = Kapi(depo);

            // Ayarda değer yok (null), kutular boş: kapanış bir şey yazdırmıyor.
            Cagir(AyarPenceresi(kapi), "OnClosed", EventArgs.Empty);
            Denetle("boş kutu ile girilmemiş değer aynı sayılıyor, dosya yazılmadı",
                !File.Exists(dosya) && Oku(google, "ClientId") is null && Oku(google, "ClientSecret") is null);

            // Kutulara yapıştırıldı, odak çıkmadan Esc'ye basıldı (LostFocus gelmedi).
            object pencere = AyarPenceresi(kapi);
            ((TextBox)AlanOku(pencere, "ClientIdBox")).Text = " deneme.apps.googleusercontent.com ";
            ((PasswordBox)AlanOku(pencere, "ClientSecretBox")).Password = "GOCSPX-deneme";
            Cagir(pencere, "OnClosed", EventArgs.Empty);
            string metin = File.Exists(dosya) ? File.ReadAllText(dosya) : string.Empty;
            Denetle("kapanışta iki değer de ayara yazıldı",
                (string?)Oku(google, "ClientId") == "deneme.apps.googleusercontent.com"
                && (string?)Oku(google, "ClientSecret") == "GOCSPX-deneme");
            Denetle("ve dosyaya kaydedildi (gizli anahtar şifreli)",
                metin.Contains("deneme.apps.googleusercontent.com", StringComparison.Ordinal)
                && metin.Contains("ClientSecretSifreli", StringComparison.Ordinal)
                && !metin.Contains("GOCSPX-deneme", StringComparison.Ordinal));
            Denetle("duvar kâğıdı yeniden yazılmadı", kapi.Uygulama == 0);

            // Odak çıkınca (LostFocus) da aynı yol: yalnızca kaydediliyor.
            Type pencereTipi = Tip("Kalender.Ui.SettingsWindow");
            List<Komut> k = Komutlar(pencereTipi.GetMethod("OnGoogleCredentialsChanged", Hepsi)!);
            Denetle("odak çıkınca da aynı kayıt yolu, duvar kâğıdı yeniden yazılmıyor",
                Bul(k, x => Cagri(x, pencereTipi, "SaveGoogleCredentials")) >= 0
                && Bul(k, x => Cagri(x, Tip("Kalender.Ui.IAppearanceHost"), "ApplyAppearance")) < 0);
        }
        finally
        {
            kok.SetValue(null, onceki);
        }
    }

    // ------------------------------------------------------------------ 13. madde

    /// <summary>Okunan ayarların günlük düzeyi; okunacak bir şey yoksa -1.</summary>
    private static int Duzey(object? ayarlar) => ayarlar is null ? -1 : (int)Oku(ayarlar, "LogLevel")!;

    /// <summary>Klasördeki "ayarlar.bozuk-…" dosyaları.</summary>
    private static string[] Bozuklar(string klasor) => Directory.GetFiles(klasor, "ayarlar.bozuk-*.json");

    /// <summary>
    /// Ayar dosyasının okunması ve yazılması: geçici klasördeki bir dosyayla,
    /// yalnızca SettingsStore'un dosya yardımcılarıyla.
    /// </summary>
    private static void Madde13Dosya(string klasor)
    {
        Type depo = Tip("Kalender.Settings.SettingsStore");
        string dosya = Path.Combine(klasor, "ayarlar.json");
        string yedek = (string)Statik(depo, "YedekAdi", dosya)!;
        object? Okunan() => Statik(depo, "DosyadanOku", dosya);
        void Yazilan(string json) => Statik(depo, "DosyayaYaz", dosya, json);
        static string Json(int duzey) => "{ \"LogLevel\": " + duzey + ", \"StartWithWindows\": false }";
        string Adlar() => string.Join(", ", Bozuklar(klasor).Select(Path.GetFileName));

        Denetle("yedeğin adı ayarlar.json.yedek", Path.GetFileName(yedek) == "ayarlar.json.yedek");
        Denetle("dosya yokken okunacak bir şey yok, klasöre bir şey yazılmıyor",
            Okunan() is null && Directory.GetFiles(klasor).Length == 0);

        Yazilan(Json(2));
        Denetle("ilk yazımda dosya yazılıyor, geçici dosya kalmıyor",
            File.ReadAllText(dosya) == Json(2) && !File.Exists(dosya + ".tmp") && !File.Exists(yedek));

        Yazilan(Json(3));
        Denetle("sonraki yazımda önceki hâl yedek oluyor",
            File.ReadAllText(dosya) == Json(3) && File.ReadAllText(yedek) == Json(2));

        Yazilan(Json(4));
        Denetle("yedek her yazımda bir önceki hâl, geçici dosya kalmıyor",
            File.ReadAllText(dosya) == Json(4) && File.ReadAllText(yedek) == Json(3) && !File.Exists(dosya + ".tmp"));
        Denetle("dosya okunuyor", Duzey(Okunan()) == 4);

        // Elektrik kesintisinden kalan boş dosya.
        File.WriteAllBytes(dosya, Array.Empty<byte>());
        Denetle("boş dosya yerine yedek okunuyor", Duzey(Okunan()) == 3);
        string[] bozuk = Bozuklar(klasor);
        Denetle("boş dosya ayarlar.bozuk-<tarih>.json adıyla kenara alındı",
            !File.Exists(dosya) && bozuk.Length == 1 && new FileInfo(bozuk[0]).Length == 0, Adlar());
        Denetle("yedeğe dokunulmadı", File.ReadAllText(yedek) == Json(3));

        Yazilan(Json(2));
        Denetle("ardından yazılan ayar kenara alınan dosyanın üstüne yazılmıyor, yedek korunuyor",
            File.ReadAllText(dosya) == Json(2) && File.ReadAllText(yedek) == Json(3)
            && Bozuklar(klasor).Length == 1 && new FileInfo(Bozuklar(klasor)[0]).Length == 0);

        // İkisi de okunamıyor: yarım içerik ve sıfırlarla dolu yedek.
        const string Yarim = "{ \"LogLevel\": 2, \"Appear";
        File.WriteAllText(dosya, Yarim);
        File.WriteAllBytes(yedek, new byte[64]);
        Denetle("ikisi de okunamazsa varsayılanlara dönülüyor", Okunan() is null);
        bozuk = Bozuklar(klasor);
        Denetle("ikinci bozuk dosya ayrı adla kenara alındı, ilki korunuyor",
            !File.Exists(dosya) && bozuk.Length == 2 && bozuk.Count(f => new FileInfo(f).Length == 0) == 1
            && bozuk.Any(f => File.ReadAllText(f) == Yarim), Adlar());

        // Yerine koyma yarıda kaldı: asıl dosya yok, yalnız yedek var.
        File.WriteAllText(yedek, Json(3));
        Denetle("asıl dosya yoksa yedek okunuyor", Duzey(Okunan()) == 3 && Bozuklar(klasor).Length == 2);

        // Bu sürümün tanımadığı bir değer (ör. eski sürüme dönülünce) de okunamayan dosya sayılıyor.
        Yazilan(Json(4));
        File.WriteAllText(dosya, "{ \"Appearance\": { \"BackgroundMode\": \"Yepyeni\" } }");
        Denetle("tanınmayan değerde de dosya kenara alınıyor, yedek okunuyor",
            Duzey(Okunan()) == 3 && !File.Exists(dosya) && Bozuklar(klasor).Length == 3, Adlar());

        // Derlenmiş kodda: geçici dosya diske boşaltılıyor, değiştirmede yedek adı veriliyor.
        List<Komut> k = Komutlar(depo.GetMethod("DosyayaYaz", Hepsi)!);
        int bosalt = Bul(k, x => x.Uye is MethodInfo m && m.Name == "Flush"
            && m.GetParameters() is [{ ParameterType: var p }] && p == typeof(bool));
        Denetle("geçici dosya diske boşaltılıyor (Flush(true))", bosalt > 0 && k[bosalt - 1].Kod == OpCodes.Ldc_I4_1);
        int degistir = Bul(k, x => Cagri(x, typeof(File), "Replace"));
        Denetle("asıl dosya yedek adıyla değiştiriliyor", degistir > 0 && Cagri(k[degistir - 1], depo, "YedekAdi"));
        Denetle("açılıştaki okuma yeni yoldan", Bul(Komutlar(depo.GetMethod("Load", Hepsi)!), x => Cagri(x, depo, "DosyadanOku")) >= 0);
    }

    // ------------------------------------------------- ayarlar penceresi (31, 37)

    /// <summary>Ayarları bellekte tutan bir depo; Save çağrılırsa Paths.SettingsFile'a yazar.</summary>
    private static object BellekDeposu()
    {
        object depo = RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Settings.SettingsStore"));
        AlanAta(depo, "_gate", new object());
        Yaz(depo, "Current", Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!);
        return depo;
    }

    /// <summary>Ayarlar penceresinin uygulamaya açılan kapısının sahtesi.</summary>
    private static SahteAyarKapisi Kapi(object depo, params object[] ekranlar)
    {
        var kapi = (SahteAyarKapisi)DispatchProxy.Create(Tip("Kalender.Ui.IAppearanceHost"), typeof(SahteAyarKapisi));
        kapi.Ayarlar = depo;
        kapi.Guncellemeler = RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Core.UpdateService"));
        kapi.Ekranlar = Liste(ekranlar);
        return kapi;
    }

    /// <summary>
    /// Kurucusu çalıştırılmamış bir ayarlar penceresi: ekranda açılmaz, yalnızca
    /// sınanan işleyicilerin kullandığı alanlar dolu. Uygulamaya açılan kapısı
    /// sahte: duvar kâğıdına hiçbir şey yazılmaz, uygulama yalnızca sayılır.
    /// </summary>
    private static object AyarPenceresi(SahteAyarKapisi kapi)
    {
        Type tip = Tip("Kalender.Ui.SettingsWindow");
        object pencere = RuntimeHelpers.GetUninitializedObject(tip);
        typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(pencere, Dispatcher.CurrentDispatcher);
        AlanAta(pencere, "_host", kapi);
        AlanAta(pencere, "_applyTimer", new DispatcherTimer { Interval = TimeSpan.FromHours(1) });
        AlanAta(pencere, "_rows", Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Ui.MonitorRow")))!);
        AlanAta(pencere, "MonitorList", new ItemsControl());
        AlanAta(pencere, "ClientIdBox", new TextBox());
        AlanAta(pencere, "ClientSecretBox", new PasswordBox());
        return pencere;
    }

    // ------------------------------------------------------------------ 37. madde

    private static void Madde37()
    {
        Console.WriteLine("37) Ayarlar penceresi ekran satırlarını kurarken ayarlara kayıt açmıyor");
        object geciciAdli = Ekran(@"\\.\DISPLAY1", 0, 2560, 1440);
        object bilinen = Ekran(@"\\.\DISPLAY2", 2560, 3440, 1440);
        Yaz(bilinen, "StableId", @"DENEME\EKRAN-3440");
        Yaz(bilinen, "KimlikOkunamadi", false);

        object depo = BellekDeposu();
        var kayitlar = (IDictionary)Oku(Oku(depo, "Current")!, "Monitors")!;
        object pencere = AyarPenceresi(Kapi(depo, geciciAdli, bilinen));
        var satirlar = (IList)AlanOku(pencere, "_rows");
        var sayac = (DispatcherTimer)AlanOku(pencere, "_applyTimer");

        Cagir(pencere, "BuildMonitorRows");
        Denetle("her ekrana bir satır kuruldu",
            satirlar.Count == 2 && ReferenceEquals(((ItemsControl)AlanOku(pencere, "MonitorList")).ItemsSource, satirlar));
        Denetle("satırlar kurulurken ayarlara kayıt açılmadı (geçici ad da yazılmadı)", kayitlar.Count == 0, $"{kayitlar.Count} kayıt");

        object varsayilan = Statik(Tip("Kalender.Settings.PlacementDefaults"), "For", bilinen)!;
        object satir = satirlar[1]!;
        object yer = Oku(satir, "Placement")!;
        Denetle("kaydı olmayan ekranda katmanın hesapladığı varsayılan yer gösteriliyor",
            (double)Oku(yer, "XRatio")! == (double)Oku(varsayilan, "XRatio")!
            && (double)Oku(yer, "YRatio")! == (double)Oku(varsayilan, "YRatio")!,
            $"{Oku(yer, "XRatio")} / {Oku(yer, "YRatio")}");

        Yaz(satir, "Enabled", false);
        Denetle("satırda değişiklik yapılınca o ekranın kaydı açılıyor",
            kayitlar.Count == 1 && ReferenceEquals(kayitlar[@"DENEME\EKRAN-3440"], yer));
        Denetle("değişiklik masaüstüne uygulanmak üzere zamanlandı", sayac.IsEnabled);
        sayac.Stop();

        Cagir(pencere, "BuildMonitorRows");
        Denetle("kayıtlı ekranda kaydın kendisi kullanılıyor",
            ReferenceEquals(Oku(satirlar[1]!, "Placement"), yer) && kayitlar.Count == 1);

        // Derlenmiş kodda: kayıt açan PlacementFor satırlar kurulurken çağrılmıyor;
        // ekrana fotoğraf seçilince yerleşim ayarlara yazılıyor.
        Type pencereTipi = Tip("Kalender.Ui.SettingsWindow");
        Denetle("satırları kuran yöntem PlacementFor çağırmıyor",
            Bul(Komutlar(pencereTipi.GetMethod("BuildMonitorRows", Hepsi)!),
                x => Cagri(x, Tip("Kalender.Settings.AppSettings"), "PlacementFor")) < 0);
        Denetle("ekrana fotoğraf seçilince yerleşim ayarlara yazılıyor",
            Bul(Komutlar(pencereTipi.GetMethod("OnPickMonitorPhoto", Hepsi)!), x => x.Uye is MethodBase m
                && m.Name == "set_Item" && m.DeclaringType is { IsGenericType: true } d
                && d.GetGenericTypeDefinition() == typeof(Dictionary<,>)) >= 0);
    }

    // ------------------------------------------------------------------ 10. madde

    /// <summary>
    /// Takvim ve görev listesi satırlarını kurabilen bir ayarlar penceresi:
    /// SettingsWindow'un kurucusu çalışmaz, yalnız Window'unki çalışır ki
    /// satırların biçemleri pencerenin kaynaklarından bulunabilsin. Ekranda açılmaz.
    /// </summary>
    private static object KaynakPenceresi(SahteAyarKapisi kapi)
    {
        object pencere = AyarPenceresi(kapi);
        typeof(System.Windows.Window).GetConstructor(Type.EmptyTypes)!.Invoke(pencere, null);
        ((System.Windows.FrameworkElement)pencere).Resources = new System.Windows.ResourceDictionary
        {
            ["Satir"] = new System.Windows.Style(typeof(DockPanel)),
            ["SatirBasligi"] = new System.Windows.Style(typeof(TextBlock)),
            ["SatirAciklamasi"] = new System.Windows.Style(typeof(TextBlock)),
        };
        AlanAta(pencere, "_sourceSignatures", new Dictionary<StackPanel, string>());
        AlanAta(pencere, "CalendarListPanel", new StackPanel());
        AlanAta(pencere, "TaskListPanel", new StackPanel());
        return pencere;
    }

    /// <summary>Paneldeki anahtarlar, görünüş sırasıyla.</summary>
    private static List<CheckBox> Anahtarlar(StackPanel panel)
        => panel.Children.OfType<DockPanel>().SelectMany(d => d.Children.OfType<CheckBox>()).ToList();

    private static void Madde10()
    {
        Console.WriteLine("10) Bütün takvim ya da görev listesi anahtarları kapatılınca hiçbiri gösteriliyor");
        string hicbiri = Tip("Kalender.Settings.GoogleSettings").GetField("HicbiriSecili", Hepsi)?.GetValue(null) as string ?? "(yok)";

        object depo = BellekDeposu();
        object google = Oku(Oku(depo, "Current")!, "Google")!;
        var takvimler = (List<string>)Oku(google, "SelectedCalendarIds")!;
        var gorevListeleri = (List<string>)Oku(google, "SelectedTaskListIds")!;
        SahteAyarKapisi kapi = Kapi(depo);
        var kaynaklar = new List<(string Id, string Name)> { ("a@deneme", "İş"), ("b@deneme", "Ev") };
        string Secim() => takvimler.Count == 0 ? "(boş: hepsi)" : string.Join(", ", takvimler);

        object pencere = KaynakPenceresi(kapi);
        var takvimPaneli = (StackPanel)AlanOku(pencere, "CalendarListPanel");
        Cagir(pencere, "BuildSourceList", takvimPaneli, kaynaklar, takvimler);
        List<CheckBox> anahtarlar = Anahtarlar(takvimPaneli);
        Denetle("boş seçimde (eski ayar dosyası) bütün anahtarlar açık",
            anahtarlar.Count == 2 && anahtarlar.All(k => k.IsChecked == true));

        anahtarlar[0].IsChecked = false;
        Denetle("biri kapatılınca yalnız öbürü seçili", takvimler.SequenceEqual(new[] { "b@deneme" }), Secim());

        anahtarlar[1].IsChecked = false;
        Denetle("hepsi kapatılınca seçimde \"hiçbiri\" işareti var (boş liste \"hepsi\" sayılırdı)",
            takvimler.SequenceEqual(new[] { hicbiri }), Secim());
        Denetle("her değişiklikte zorlanmış yenileme istendi", kapi.ZorlanmisYenileme == 2, $"{kapi.ZorlanmisYenileme} kez");

        // Pencere kapatılıp yeniden açılıyor.
        object yeniPencere = KaynakPenceresi(kapi);
        var yeniPanel = (StackPanel)AlanOku(yeniPencere, "CalendarListPanel");
        Cagir(yeniPencere, "BuildSourceList", yeniPanel, kaynaklar, takvimler);
        List<CheckBox> yeniAnahtarlar = Anahtarlar(yeniPanel);
        Denetle("pencere yeniden açılınca anahtarlar kapalı",
            yeniAnahtarlar.Count == 2 && yeniAnahtarlar.All(k => k.IsChecked == false));

        yeniAnahtarlar[0].IsChecked = true;
        Denetle("birini açınca işaret kalkıyor, yalnız o seçili", takvimler.SequenceEqual(new[] { "a@deneme" }), Secim());
        yeniAnahtarlar[1].IsChecked = true;
        Denetle("hepsi açılınca seçim yine boş (\"hepsi\")", takvimler.Count == 0, Secim());

        // Tek görev listesi: anahtarı kapatınca görevler gösterilmiyor.
        var gorevPaneli = (StackPanel)AlanOku(yeniPencere, "TaskListPanel");
        Cagir(yeniPencere, "BuildSourceList", gorevPaneli, new List<(string Id, string Name)> { ("liste", "Görevlerim") },
            gorevListeleri);
        Anahtarlar(gorevPaneli)[0].IsChecked = false;
        Denetle("tek görev listesinin anahtarı kapatılınca \"hiçbiri\"", gorevListeleri.SequenceEqual(new[] { hicbiri }),
            string.Join(", ", gorevListeleri));
        Denetle("takvim seçimine dokunulmadı", takvimler.Count == 0);
    }

    // ------------------------------------------------------------------ 31. madde

    private static void Madde31()
    {
        Console.WriteLine("31) Duvar kâğıdını etkilemeyen ayarlar ve pencereyi kapatmak duvar kâğıdını yeniden yazdırmıyor");
        Type pencereTipi = Tip("Kalender.Ui.SettingsWindow");
        Type kapiTipi = Tip("Kalender.Ui.IAppearanceHost");
        Type depoTipi = Tip("Kalender.Settings.SettingsStore");

        foreach (string ad in new[] { "OnThemeToggled", "OnStartupChanged", "OnLogLevelChanged", "OnPollIntervalChanged", "SaveGoogleCredentials" })
        {
            List<Komut> k = Komutlar(pencereTipi.GetMethod(ad, Hepsi)!);
            Denetle(ad + " yalnızca kaydediyor, duvar kâğıdını yeniden yazdırmıyor",
                Bul(k, x => Cagri(x, depoTipi, "Save")) >= 0
                && Bul(k, x => Cagri(x, pencereTipi, "ScheduleApply") || Cagri(x, kapiTipi, "ApplyAppearance")) < 0);
        }

        // Kapanış: pencere kurulmadan, sahte kapıyla.
        SahteAyarKapisi kapi = Kapi(BellekDeposu());
        Cagir(AyarPenceresi(kapi), "OnClosed", EventArgs.Empty);
        Denetle("bekleyen değişiklik yokken kapatmak duvar kâğıdını yeniden yazdırmıyor", kapi.Uygulama == 0);

        object pencere = AyarPenceresi(kapi);
        var sayac = (DispatcherTimer)AlanOku(pencere, "_applyTimer");
        sayac.Start();
        Cagir(pencere, "OnClosed", EventArgs.Empty);
        Denetle("bekleyen değişiklik varken kapanışta bir kez uygulanıyor", kapi.Uygulama == 1 && !sayac.IsEnabled);
    }

    // ------------------------------------------------ çizim ve fotoğraf (7, 8, 26-30)

    private static readonly DColor Mavi = DColor.FromArgb(30, 60, 200);

    private static readonly DColor Kagit = DColor.FromArgb(0xF2, 0xF0, 0xEC);

    private static bool Yesil(DColor c) => c.G > 180 && c.R < 110 && c.B < 110;

    private static bool MaviMi(DColor c) => c.B > 150 && c.R < 90 && c.G < 110;

    private static bool Siyah(DColor c) => c.R < 20 && c.G < 20 && c.B < 20;

    private static bool Koyu(DColor c) => c.R < 90 && c.G < 90 && c.B < 90;

    private static bool Beyaz(DColor c) => c.R > 235 && c.G > 235 && c.B > 235;

    private static object Kip(string tur, string ad) => Enum.Parse(Tip("Kalender.Settings." + tur), ad);

    private static object Cizici() => Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"), true)!;

    private static object Ressam() => Activator.CreateInstance(Tip("Kalender.Rendering.BackgroundPainter"), true)!;

    private static object YeniYerlesim(string? foto = null)
    {
        object yer = Activator.CreateInstance(Tip("Kalender.Settings.MonitorPlacement"), true)!;
        Yaz(yer, "PhotoPath", foto);
        return yer;
    }

    /// <summary>Fotoğraf kipinde, genel fotoğrafı <paramref name="foto"/> olan ayarlar.</summary>
    private static object FotografliAyarlar(string? foto, string yerlesim = "Fill", string zemin = "#000000")
    {
        object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;
        object gorunum = Oku(ayarlar, "Appearance")!;
        Yaz(gorunum, "BackgroundMode", Kip("BackgroundMode", "Photo"));
        Yaz(gorunum, "PhotoPath", foto);
        Yaz(gorunum, "PhotoFit", Kip("PhotoFit", yerlesim));
        Yaz(gorunum, "SolidColor", zemin);
        Yaz(gorunum, "PresetId", null);
        return ayarlar;
    }

    /// <summary>Yalnız zemin (takvimsiz), yerleştirme ekranının kullandığı yoldan.</summary>
    private static DBitmap Zemin(object cizici, object ayarlar, object yerlesim, int g = 300, int y = 200)
        => (DBitmap)cizici.GetType().GetMethod("RenderBackgroundOnly")!.Invoke(cizici, new[] { new DSize(g, y), ayarlar, yerlesim })!;

    /// <summary>Masaüstü katmanının ürettiği tam ekran görüntü ve takvim bloğunun sınırı.</summary>
    private static (DBitmap Resim, DRectangle Blok) Ciz(object cizici, object ayarlar, object yerlesim, bool takvim = true)
    {
        object yuzey = cizici.GetType().GetMethod("Render")!.Invoke(cizici, new[]
        {
            new DSize(1280, 800), ayarlar, yerlesim,
            Tip("Kalender.Calendar.CalendarDataSnapshot").GetProperty("Empty")!.GetValue(null)!,
            new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 1), takvim,
        })!;

        var resim = (DBitmap)Oku(yuzey, "Image")!;
        object? duzen = Oku(yuzey, "Layout");
        return (resim, duzen is null ? DRectangle.Empty : DRectangle.Round((DRectangleF)Oku(duzen, "Bounds")!));
    }

    private static int Say(DBitmap resim, DRectangle alan, Func<DColor, bool> kosul)
    {
        alan.Intersect(new DRectangle(0, 0, resim.Width, resim.Height));
        int n = 0;
        for (int y = alan.Top; y < alan.Bottom; y++)
        {
            for (int x = alan.Left; x < alan.Right; x++)
            {
                if (kosul(resim.GetPixel(x, y)))
                {
                    n++;
                }
            }
        }

        return n;
    }

    /// <summary>
    /// Deneme fotoğrafı (JPEG): düz renk, sol üst köşesindeki 16 × 16 kare yeşil.
    /// İstenirse JFIF bölümünün yerine yalnız yön etiketi taşıyan bir EXIF bölümü
    /// konur; telefonlar etiketi büyük ya da küçük sonlu yazıyor.
    /// </summary>
    private static string Fotograf(string klasor, string ad, int g, int y, DColor renk,
        float dpi = 72, int yon = 0, bool buyukSonlu = false)
    {
        using var resim = new DBitmap(g, y, DPixelFormat.Format24bppRgb);
        using (DGraphics c = DGraphics.FromImage(resim))
        {
            c.Clear(renk);
            c.FillRectangle(DBrushes.Lime, 0, 0, 16, 16);
        }

        resim.SetResolution(dpi, dpi);
        using var akis = new MemoryStream();
        resim.Save(akis, DImageFormat.Jpeg);
        byte[] jpeg = akis.ToArray();

        if (yon != 0)
        {
            var tiff = new List<byte>();
            void Iki(int d)
            {
                tiff.Add((byte)(buyukSonlu ? d >> 8 : d));
                tiff.Add((byte)(buyukSonlu ? d : d >> 8));
            }

            void Dort(int d)
            {
                Iki(buyukSonlu ? d >> 16 : d & 0xFFFF);
                Iki(buyukSonlu ? d & 0xFFFF : d >> 16);
            }

            tiff.AddRange(buyukSonlu ? "MM"u8.ToArray() : "II"u8.ToArray());
            Iki(42);
            Dort(8);
            Iki(1);
            Iki(0x0112);
            Iki(3);
            Dort(1);
            Iki(yon);
            Iki(0);
            Dort(0);

            byte[] govde = [.. "Exif\0\0"u8.ToArray(), .. tiff];
            int uzunluk = govde.Length + 2;
            int jfifSonu = 4 + ((jpeg[4] << 8) | jpeg[5]);
            jpeg = [0xFF, 0xD8, 0xFF, 0xE1, (byte)(uzunluk >> 8), (byte)uzunluk, .. govde, .. jpeg[jfifSonu..]];
        }

        string yol = Path.Combine(klasor, ad);
        File.WriteAllBytes(yol, jpeg);
        return yol;
    }

    private static bool Birakildi(DImage resim)
    {
        try
        {
            return resim.Width < 0;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    // ------------------------------------------------------------------- 7. madde

    private static void Madde7(string klasor)
    {
        Console.WriteLine("7) Ortala kipinde fotoğraf piksel boyutuyla, tam ortada çiziliyor");
        string foto72 = Fotograf(klasor, "ortala-72.jpg", 100, 60, Mavi, dpi: 72);
        string foto300 = Fotograf(klasor, "ortala-300.jpg", 100, 60, Mavi, dpi: 300);
        using (var d72 = DImage.FromFile(foto72))
        using (var d300 = DImage.FromFile(foto300))
        {
            Denetle("deneme fotoğrafları 72 ve 300 DPI", Math.Abs(d72.HorizontalResolution - 72) < 0.5
                && Math.Abs(d300.HorizontalResolution - 300) < 0.5, $"{d72.HorizontalResolution} / {d300.HorizontalResolution}");
        }

        object cizici = Cizici();
        foreach ((string foto, string ad) in new[] { (foto72, "72 DPI"), (foto300, "300 DPI") })
        {
            // 300 × 200 yüzeyde 100 × 60'lık fotoğraf (100, 70)-(200, 130) arasına düşmeli.
            using DBitmap resim = Zemin(cizici, FotografliAyarlar(foto, "Center"), YeniYerlesim());
            Denetle($"{ad}: sol üst köşe (100, 70)'te",
                Yesil(resim.GetPixel(104, 74)) && Siyah(resim.GetPixel(97, 74)) && Siyah(resim.GetPixel(104, 67)));
            Denetle($"{ad}: sağ alt köşe (200, 130)'da",
                MaviMi(resim.GetPixel(196, 126)) && Siyah(resim.GetPixel(203, 126)) && Siyah(resim.GetPixel(196, 133)));
        }

        // Yüzeyin DPI'ı da etkilemiyor (Windows ölçeği %150 iken 144 olabiliyor); Döşe de piksel boyutuyla.
        object ressam = Ressam();
        object gorunum = Oku(FotografliAyarlar(foto72, "Center"), "Appearance")!;
        object tema = Statik(Tip("Kalender.Rendering.Theme"), "From", gorunum)!;
        object resim72 = Cagir(ressam, "Fotograf", gorunum, foto72)!;
        foreach (string kip in new[] { "Center", "Tile" })
        {
            using var yuzey = new DBitmap(300, 200, DPixelFormat.Format32bppPArgb);
            yuzey.SetResolution(144, 144);
            using (DGraphics g = DGraphics.FromImage(yuzey))
            {
                Cagir(ressam, "Paint", g, new DRectangle(0, 0, 300, 200), tema, resim72, Kip("PhotoFit", kip));
            }

            bool dogru = kip == "Center"
                ? Yesil(yuzey.GetPixel(104, 74)) && Siyah(yuzey.GetPixel(97, 74))
                    && MaviMi(yuzey.GetPixel(196, 126)) && Siyah(yuzey.GetPixel(203, 126))
                : Yesil(yuzey.GetPixel(8, 8)) && Yesil(yuzey.GetPixel(108, 8)) && Yesil(yuzey.GetPixel(208, 68))
                    && MaviMi(yuzey.GetPixel(50, 30)) && MaviMi(yuzey.GetPixel(150, 90));
            Denetle($"144 DPI'lık yüzeyde {(kip == "Center" ? "Ortala" : "Döşe")} piksel boyutuyla", dogru);
        }

        ((IDisposable)ressam).Dispose();
        ((IDisposable)cizici).Dispose();
    }

    // ------------------------------------------------------------------- 8. madde

    private static void Madde8(string klasor)
    {
        Console.WriteLine("8) Fotoğrafın EXIF yön etiketi uygulanıyor");
        object ressam = Ressam();
        object gorunum = Oku(FotografliAyarlar(null), "Appearance")!;

        // Yön, büyük sonlu mu, beklenen boyut ve yeşil karenin yeri (özgün resim 64 × 32, kare sol üstte).
        var durumlar = new (int Yon, bool Buyuk, int G, int Y, int X, int Yy, string Ad)[]
        {
            (0, false, 64, 32, 8, 8, "etiketsiz"),
            (1, false, 64, 32, 8, 8, "1 (olduğu gibi)"),
            (2, false, 64, 32, 56, 8, "2 (aynalı)"),
            (3, false, 64, 32, 56, 24, "3 (baş aşağı)"),
            (6, false, 32, 64, 24, 8, "6 (dikey, küçük sonlu)"),
            (6, true, 32, 64, 24, 8, "6 (dikey, büyük sonlu)"),
            (8, true, 32, 64, 8, 56, "8 (öbür yana dikey)"),
        };

        foreach (var d in durumlar)
        {
            string yol = Fotograf(klasor, $"yon-{d.Yon}-{(d.Buyuk ? "mm" : "ii")}.jpg", 64, 32,
                DColor.FromArgb(40, 40, 120), yon: d.Yon, buyukSonlu: d.Buyuk);

            bool etiketVardi;
            using (var ham = DImage.FromFile(yol))
            {
                etiketVardi = d.Yon == 0 || (Array.IndexOf(ham.PropertyIdList, 0x0112) >= 0
                    && BitConverter.ToUInt16(ham.GetPropertyItem(0x0112)!.Value!, 0) == d.Yon);
            }

            var resim = (DBitmap?)Cagir(ressam, "Fotograf", gorunum, yol);
            if (resim is null)
            {
                Denetle($"yön {d.Ad}: fotoğraf açıldı", false);
                continue;
            }

            bool boyut = resim.Width == d.G && resim.Height == d.Y;
            bool kare = Yesil(resim.GetPixel(d.X, d.Yy)) && (d.X == 8 && d.Yy == 8 || !Yesil(resim.GetPixel(8, 8)));
            bool etiketKalkti = Array.IndexOf(resim.PropertyIdList, 0x0112) < 0;
            bool onbellekte = ReferenceEquals(Cagir(ressam, "Fotograf", gorunum, yol), resim);
            Denetle($"yön {d.Ad}: {d.G} × {d.Y}, yeşil kare ({d.X}, {d.Yy})'de, etiket kalktı, ön bellekte döndürülmüş hâli",
                etiketVardi && boyut && kare && etiketKalkti && onbellekte,
                $"{resim.Width} × {resim.Height}" + (d.Yon == 0 ? "" : $", etiket dosyada {(etiketVardi ? "vardı" : "yoktu")}"));
        }

        ((IDisposable)ressam).Dispose();
    }

    // ------------------------------------------------------------------ 27. madde

    private static void Madde27(string klasor)
    {
        Console.WriteLine("27) Fotoğraf açılamazsa takvim düz zemine göre çiziliyor, günlüğe bir kez yazılıyor");
        string yok = Path.Combine(klasor, "silinmiş fotoğraf.jpg");
        string bozuk = Path.Combine(klasor, "bozuk.jpg");
        File.WriteAllText(bozuk, "bu bir resim değil");
        object cizici = Cizici();
        var bildirilenler = (IEnumerable)AlanOku(AlanOku(cizici, "_background"), "_bildirilenler");

        foreach ((string foto, string yazi, string ad) in new[]
        {
            (yok, "Auto", "silinmiş fotoğraf, yazı rengi otomatik"),
            (yok, "Manual", "silinmiş fotoğraf, yazı rengi elle beyaz"),
            (bozuk, "Manual", "açılamayan dosya"),
        })
        {
            object ayarlar = FotografliAyarlar(foto, zemin: "#F2F0EC");
            object gorunum = Oku(ayarlar, "Appearance")!;
            Yaz(gorunum, "TextColorMode", Kip("TextColorMode", yazi));
            Yaz(gorunum, "TextColor", "#FFFFFF");
            Yaz(gorunum, "BackdropDim", 60);

            (DBitmap resim, DRectangle blok) = Ciz(cizici, ayarlar, YeniYerlesim());
            using (resim)
            {
                int koyu = Say(resim, blok, Koyu);
                Denetle($"{ad}: zemin Kâğıt rengi, takvim yazısı koyu, karartma yok",
                    resim.GetPixel(5, 5) == Kagit && resim.GetPixel(blok.Left - 8, blok.Top + (blok.Height / 2)) == Kagit && koyu > 300,
                    $"{koyu} koyu piksel");
            }
        }

        Ciz(cizici, FotografliAyarlar(yok), YeniYerlesim()).Resim.Dispose();
        Denetle("açılamayan her fotoğraf bir kez bildiriliyor", bildirilenler.Cast<object>().Count() == 2,
            string.Join(", ", bildirilenler.Cast<object>()));

        // Uygulamanın klasörü geçici klasöre çevrildiği için günlük de orada.
        string gunlukKlasoru = (string)Tip("Kalender.Core.Paths").GetProperty("LogFolder", Hepsi)!.GetValue(null)!;
        string[] satirlar = Directory.Exists(gunlukKlasoru)
            ? Directory.GetFiles(gunlukKlasoru, "*.log").SelectMany(File.ReadAllLines).ToArray()
            : Array.Empty<string>();
        int bulunamadi = satirlar.Count(x => x.Contains("[HATA] Zemin fotoğrafı bulunamadı", StringComparison.Ordinal)
            && x.Contains("silinmiş fotoğraf.jpg", StringComparison.Ordinal));
        int acilamadi = satirlar.Count(x => x.Contains("[HATA] Zemin fotoğrafı açılamadı", StringComparison.Ordinal)
            && x.Contains("bozuk.jpg", StringComparison.Ordinal));
        Denetle("günlüğe hata düzeyinde, her dosya için bir satır", bulunamadi == 1 && acilamadi == 1,
            $"bulunamadı {bulunamadi}, açılamadı {acilamadi}");

        // Fotoğraf yeniden açılabilince bildirim sıfırlanıyor: sonraki bir hata yine yazılır.
        Fotograf(klasor, "silinmiş fotoğraf.jpg", 64, 32, Mavi);
        Ciz(cizici, FotografliAyarlar(yok), YeniYerlesim(), takvim: false).Resim.Dispose();
        Denetle("fotoğraf yerine gelince yeniden bildirilebilir", bildirilenler.Cast<object>().Count() == 1);
        ((IDisposable)cizici).Dispose();
    }

    // ------------------------------------------------------------------ 28. madde

    private static void Madde28(string klasor)
    {
        Console.WriteLine("28) Ekrana özel fotoğrafta genel Yerleşim seçimi uygulanıyor");
        object yerlesim = YeniYerlesim(Fotograf(klasor, "ekrana-ozel.jpg", 100, 60, Mavi, dpi: 96));
        Denetle("ekranın kaydındaki yerleşim Kapla", Oku(yerlesim, "PhotoFit")!.Equals(Kip("PhotoFit", "Fill")));
        object cizici = Cizici();
        foreach ((string kip, string ad) in new[] { ("Center", "Ortala"), ("Fit", "Sığdır"), ("Tile", "Döşe"), ("Fill", "Kapla") })
        {
            using DBitmap resim = Zemin(cizici, FotografliAyarlar(null, kip), yerlesim);

            // 300 × 200 yüzey, 100 × 60 fotoğraf: Ortala'da sol kenar boş; Sığdır'da 300 × 180, üst kenar
            // boş; Döşe'de yeşil kare her 100 piksel; Kapla'da her yer fotoğraf.
            bool dogru = kip switch
            {
                "Center" => Siyah(resim.GetPixel(20, 100)) && MaviMi(resim.GetPixel(150, 100)),
                "Fit" => Siyah(resim.GetPixel(150, 3)) && MaviMi(resim.GetPixel(290, 100)),
                "Tile" => Yesil(resim.GetPixel(108, 8)) && Yesil(resim.GetPixel(208, 68)),
                _ => MaviMi(resim.GetPixel(150, 3)) && MaviMi(resim.GetPixel(20, 100)) && !Yesil(resim.GetPixel(108, 8)),
            };
            Denetle($"genel Yerleşim {ad}: ekrana özel fotoğraf da {ad} ile çiziliyor", dogru);
        }

        ((IDisposable)cizici).Dispose();
    }

    // ------------------------------------------------------------------ 29. madde

    private static void Madde29(string klasor)
    {
        Console.WriteLine("29) Ekranlarda farklı fotoğraf varken fotoğraflar her çizimde yeniden açılmıyor");
        string a = Fotograf(klasor, "ekran-a.jpg", 320, 200, Mavi);
        string b = Fotograf(klasor, "ekran-b.jpg", 320, 200, DColor.FromArgb(32, 32, 32));
        object cizici = Cizici();
        Type ciziciTipi = cizici.GetType();
        var onbellek = (IDictionary)AlanOku(AlanOku(cizici, "_background"), "_onbellek");
        object ayarlar = FotografliAyarlar(null);
        object yerA = YeniYerlesim(a);
        object yerB = YeniYerlesim(b);

        // Masaüstü katmanının bir turu: her ekran çizilir, sonra kullanılmayanlar atılır.
        void Tur(params object[] yerler)
        {
            foreach (object yer in yerler)
            {
                Ciz(cizici, ayarlar, yer, takvim: false).Resim.Dispose();
            }

            Cagir(cizici, "KullanilmayanFotograflariAt");
        }

        DImage? Resim(string yol)
        {
            string? anahtar = onbellek.Keys.Cast<string>()
                .FirstOrDefault(k => k.StartsWith(Path.GetFullPath(yol) + "|", StringComparison.OrdinalIgnoreCase));
            return anahtar is null ? null : (DImage)onbellek[anahtar]!;
        }

        Tur(yerA, yerB);
        DImage? resimA = Resim(a);
        DImage? resimB = Resim(b);
        Tur(yerA, yerB);
        Tur(yerB, yerA);
        Denetle("iki ekranın fotoğrafı da ön bellekte; turlar arasında yeniden açılmıyor",
            onbellek.Count == 2 && resimA is not null && resimB is not null
            && ReferenceEquals(Resim(a), resimA) && ReferenceEquals(Resim(b), resimB), $"{onbellek.Count} fotoğraf");

        Tur(yerB);
        Denetle("turda hiçbir ekranın çizmediği fotoğraf atılıyor, öbürü kalıyor",
            onbellek.Count == 1 && ReferenceEquals(Resim(b), resimB) && resimA is not null && Birakildi(resimA));

        object ressam = Ressam();
        object gorunum = Oku(ayarlar, "Appearance")!;
        var ikinciOnbellek = (IDictionary)AlanOku(ressam, "_onbellek");
        int enCok = 0;
        for (int i = 0; i < 12; i++)
        {
            string kopya = Path.Combine(klasor, $"kopya-{i}.jpg");
            File.Copy(a, kopya, overwrite: true);
            Cagir(ressam, "Fotograf", gorunum, kopya);
            enCok = Math.Max(enCok, ikinciOnbellek.Count);
        }

        Denetle("atma çağrılmayan çizicide de ön bellek sınırlı (en çok 8)", enCok <= 8, $"en çok {enCok}");
        ((IDisposable)ressam).Dispose();

        List<Komut> k = Komutlar(Tip("Kalender.Desktop.DesktopLayerManager").GetMethod("RefreshOnce", Hepsi)!);
        int cizim = Bul(k, x => Cagri(x, ciziciTipi, "Render"));
        Denetle("masaüstü katmanı bütün ekranları çizdikten sonra kullanılmayanları atıyor",
            cizim >= 0 && Bul(k, x => Cagri(x, ciziciTipi, "KullanilmayanFotograflariAt"), cizim) > cizim);
        ((IDisposable)cizici).Dispose();
    }

    // ------------------------------------------------------------------ 30. madde

    private static void Madde30(string klasor)
    {
        Console.WriteLine("30) Fotoğraf yalnız bir ekrandaysa öbür ekranın takvimi düz zemine göre çiziliyor");
        string koyuFoto = Fotograf(klasor, "koyu.jpg", 320, 200, DColor.FromArgb(32, 32, 32));
        object ayarlar = FotografliAyarlar(null, zemin: "#F2F0EC");
        object gorunum = Oku(ayarlar, "Appearance")!;
        Yaz(gorunum, "TextColor", "#FFFFFF");
        Yaz(gorunum, "BackdropDim", 60);
        object fotografli = YeniYerlesim(koyuFoto);
        object fotografsiz = YeniYerlesim();
        object cizici = Cizici();

        (DBitmap resim, DRectangle blok) = Ciz(cizici, ayarlar, fotografsiz);
        using (resim)
        {
            int koyu = Say(resim, blok, Koyu);
            DColor kenar = resim.GetPixel(blok.Left - 8, blok.Top + (blok.Height / 2));
            Denetle("fotoğrafsız ekranda yazı rengi otomatik (Kâğıt'ta koyu)", koyu > 300, $"{koyu} koyu piksel");
            Denetle("fotoğrafsız ekranda karartma yok", kenar == Kagit, kenar.ToString());
        }

        (resim, blok) = Ciz(cizici, ayarlar, fotografli);
        using (resim)
        {
            int beyaz = Say(resim, blok, Beyaz);
            Denetle("fotoğraflı ekranda yazı seçilen renkte (beyaz)", beyaz > 300, $"{beyaz} beyaz piksel");
        }

        MethodInfo blokCiz = cizici.GetType().GetMethod("RenderCalendarBlock")!;
        object bos = Tip("Kalender.Calendar.CalendarDataSnapshot").GetProperty("Empty")!.GetValue(null)!;
        foreach ((object yer, string ad) in new[] { (fotografsiz, "fotoğrafsız"), (fotografli, "fotoğraflı") })
        {
            using var tas = (DBitmap)blokCiz.Invoke(cizici, new[] { 0.5f, ayarlar, yer, bos, new DateOnly(2026, 10, 12) })!;
            var tum = new DRectangle(0, 0, tas.Width, tas.Height);
            int koyu = Say(tas, tum, c => c.A > 200 && Koyu(c));
            int beyaz = Say(tas, tum, c => c.A > 200 && Beyaz(c));
            Denetle($"yerleştirme ekranında da {ad} ekranın renkleri", ad == "fotoğrafsız" ? koyu > 300 && beyaz < 50 : beyaz > 300 && koyu < 50,
                $"{koyu} koyu, {beyaz} beyaz");
        }

        // Elle seçilen yazı rengi fotoğrafsız ekranda da geçerli: anahtar kullanıcının elinde.
        Yaz(gorunum, "TextColorMode", Kip("TextColorMode", "Manual"));
        (resim, blok) = Ciz(cizici, ayarlar, fotografsiz);
        using (resim)
        {
            Denetle("anahtar kapalıyken fotoğrafsız ekranda da seçilen renk", Say(resim, blok, Beyaz) > 300 && Say(resim, blok, Koyu) < 50);
        }

        ((IDisposable)cizici).Dispose();

        // Ayarlar penceresi: fotoğrafsız ekran varken Otomatik anahtarı açık kalıyor.
        object depo = BellekDeposu();
        object s = Oku(depo, "Current")!;
        object a = Oku(s, "Appearance")!;
        Yaz(a, "BackgroundMode", Kip("BackgroundMode", "Photo"));
        Yaz(a, "SolidColor", "#F2F0EC");
        var kayitlar = (IDictionary)Oku(s, "Monitors")!;
        kayitlar[@"\\.\DISPLAY2"] = YeniYerlesim(koyuFoto);

        object pencere = AyarPenceresi(Kapi(depo, Ekran(@"\\.\DISPLAY1", 0, 1280, 800), Ekran(@"\\.\DISPLAY2", 1280, 1280, 800)));
        var anahtar = new CheckBox();
        var not = new TextBlock();
        AlanAta(pencere, "AutoTextColorBox", anahtar);
        AlanAta(pencere, "AutoTextNote", not);
        AlanAta(pencere, "DimPanel", new DockPanel());

        Cagir(pencere, "UpdateTextColorControls");
        Denetle("fotoğraf bir ekrandayken Otomatik anahtarı açık ve kullanılabilir",
            anahtar.IsEnabled && anahtar.IsChecked == true && not.Text.Contains("fotoğrafsız"), not.Text);

        kayitlar[@"\\.\DISPLAY1"] = YeniYerlesim(koyuFoto);
        Cagir(pencere, "UpdateTextColorControls");
        Denetle("bütün ekranlarda fotoğraf varken anahtar kapalı ve kilitli", !anahtar.IsEnabled && anahtar.IsChecked == false);

        kayitlar.Clear();
        Yaz(a, "PhotoPath", koyuFoto);
        Cagir(pencere, "UpdateTextColorControls");
        Denetle("genel fotoğraf varken anahtar kilitli", !anahtar.IsEnabled && anahtar.IsChecked == false);

        Yaz(a, "PhotoPath", null);
        Yaz(a, "BackgroundMode", Kip("BackgroundMode", "Solid"));
        Cagir(pencere, "UpdateTextColorControls");
        Denetle("düz zeminde anahtar açık", anahtar.IsEnabled && anahtar.IsChecked == true);
    }

    // ------------------------------------------------------------------ 26. madde

    private static void Madde26()
    {
        Console.WriteLine("26) Peş peşe yazılan zemin dosyaları yazımlar durunca siliniyor");
        Type tuval = Tip("Kalender.Desktop.WallpaperCanvas");
        object canvas = Activator.CreateInstance(tuval, true)!;

        // Uygulamanın klasörü geçici klasöre çevrildi; zemin dosyaları orada.
        string klasor = (string)Tip("Kalender.Core.Paths").GetProperty("CanvasFolder", Hepsi)!.GetValue(null)!;
        Directory.CreateDirectory(klasor);
        DateTime eski = DateTime.UtcNow.AddMinutes(-10);
        string Dosya(string ad, bool eskiMi)
        {
            string yol = Path.Combine(klasor, ad);
            File.WriteAllText(yol, "deneme");
            if (eskiMi)
            {
                File.SetLastWriteTimeUtc(yol, eski);
            }

            return yol;
        }

        string ekranda = Dosya("zemin-aaaaaaaa-20261012-100000-000.bmp", true);
        string ara1 = Dosya("zemin-aaaaaaaa-20261012-100000-040.bmp", true);
        string ara2 = Dosya("zemin-bbbbbbbb-20261012-100000-080.bmp", true);
        string yeni = Dosya("zemin-aaaaaaaa-20261012-100959-000.bmp", false);
        string baska = Dosya("deniz.jpg", true);
        ((IDictionary)AlanOku(canvas, "_sonYazilanlar"))["A"] = ("aygıt-A", ekranda);

        Cagir(canvas, "TemizligiPlanla");
        var zamanlayici = (DispatcherTimer?)tuval.GetField("_temizlikZamanlayicisi", Hepsi)!.GetValue(canvas);
        Denetle("yazımdan sonra temizlik bekleme süresi dolunca yapılmak üzere planlanıyor",
            zamanlayici is { IsEnabled: true } && zamanlayici.Interval >= TimeSpan.FromMinutes(2), $"{zamanlayici?.Interval}");
        if (zamanlayici is null)
        {
            return;
        }

        // Süre beklenmeden: zamanlayıcı kısaltılıp dağıtıcı çalıştırılıyor.
        zamanlayici.Interval = TimeSpan.FromMilliseconds(20);
        for (int i = 0; i < 100 && zamanlayici.IsEnabled; i++)
        {
            Thread.Sleep(20);
            Bosalt();
        }

        Denetle("süre dolunca zamanlayıcı bir kez çalışıp durdu", !zamanlayici.IsEnabled);
        Denetle("ekranda olmayan eski dosyalar silindi", !File.Exists(ara1) && !File.Exists(ara2));
        Denetle("ekranda duran ve yeni yazılan dosya silinmedi, zemin olmayana dokunulmadı",
            File.Exists(ekranda) && File.Exists(yeni) && File.Exists(baska));

        Cagir(canvas, "TemizligiPlanla");
        Cagir(canvas, "Birak");
        Denetle("takvim duvar kâğıdından kaldırılınca bekleyen temizlik iptal", !zamanlayici.IsEnabled);

        List<Komut> k = Komutlar(tuval.GetMethod("Apply", Hepsi)!);
        int temizle = Bul(k, x => Cagri(x, tuval, "CleanUnusedFiles"));
        Denetle("her yazımdan sonra temizlik yeniden planlanıyor",
            temizle >= 0 && Bul(k, x => Cagri(x, tuval, "TemizligiPlanla"), temizle) > temizle);

        MethodInfo? tik = tuval.GetNestedTypes(BindingFlags.NonPublic)
            .SelectMany(t => t.GetMethods(Hepsi | BindingFlags.DeclaredOnly))
            .FirstOrDefault(m => m.Name.Contains("TemizligiPlanla", StringComparison.Ordinal));
        Denetle("süre dolunca zamanlayıcı durup temizlik yapılıyor", tik is not null
            && Bul(Komutlar(tik), x => Cagri(x, typeof(DispatcherTimer), "Stop")) >= 0
            && Bul(Komutlar(tik), x => Cagri(x, tuval, "CleanUnusedFiles")) >= 0);
    }

    // ------------------------------------------------ fare kancası (23, 24, 36)

    private static readonly List<DPoint> _menuNoktalari = new();
    private static readonly List<string> _dugmeler = new();

    /// <summary>MSLLHOOKSTRUCT: alt düzey fare kancasının olay bilgisi.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FareOlayi
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    /// <summary>
    /// Takvimin fare kancası; takvim x &lt; 1000, önceki ay oku 500-532. Kanca
    /// hiç takılmaz, gerçek fareye dokunulmaz: "takılı" görünsün diye tanıtıcısına
    /// sahte bir değer yazılır; kapatılmış sayıldığı için Arm gerçek kanca takmaz.
    /// Olaylar doğrudan işleyiciye verilir (<see cref="Gonder"/>).
    /// </summary>
    private static object Kanca()
    {
        Type t = Tip("Kalender.Desktop.CalendarMouseHook");
        Type dugmeTipi = Tip("Kalender.Calendar.NavButton");
        Func<DPoint, bool> takvimde = p => p.X < 1000;
        Delegate dugme = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(DPoint), dugmeTipi),
            typeof(Program).GetMethod(nameof(DugmeBul), Hepsi)!.MakeGenericMethod(dugmeTipi));
        object kanca = Activator.CreateInstance(t, Dispatcher.CurrentDispatcher, takvimde, dugme)!;

        AlanAta(kanca, "_disposed", true);
        AlanAta(kanca, "_hook", new IntPtr(1));
        t.GetEvent("RightClicked")!.AddEventHandler(kanca, new Action<DPoint>(_menuNoktalari.Add));
        t.GetEvent("NavClicked")!.AddEventHandler(kanca, Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(dugmeTipi),
            typeof(Program).GetMethod(nameof(DugmeKaydet), Hepsi)!.MakeGenericMethod(dugmeTipi)));
        return kanca;
    }

    private static T DugmeBul<T>(DPoint p) where T : struct, Enum
        => Enum.Parse<T>(p.X is >= 500 and < 533 ? "Previous" : "None");

    private static void DugmeKaydet<T>(T dugme) => _dugmeler.Add(dugme!.ToString()!);

    /// <summary>Kancaya yapay bir fare olayı verir; olay yutulduysa true.</summary>
    private static bool Gonder(object kanca, uint ileti, int x)
    {
        IntPtr bellek = Marshal.AllocHGlobal(Marshal.SizeOf<FareOlayi>());
        try
        {
            Marshal.StructureToPtr(new FareOlayi { X = x, Y = 10 }, bellek, false);
            return (IntPtr)Cagir(kanca, "OnMouse", 0, (IntPtr)ileti, bellek)! == (IntPtr)1;
        }
        finally
        {
            Marshal.FreeHGlobal(bellek);
        }
    }

    private static bool Takili(object kanca) => (IntPtr)AlanOku(kanca, "_hook") != IntPtr.Zero;

    /// <summary>
    /// Kurucusu çalıştırılmamış uygulama nesnesi: yalnızca fare kancası ve imleç
    /// takvimin üstünde. Kancanın bırakış olayı uygulamadaki gibi
    /// UpdateMouseHook'a bağlanır; sağ tık menüye bağlanmaz (menü açılmaz).
    /// </summary>
    private static object Uygulama(object kanca)
    {
        object host = RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Core.AppHost"));
        AlanAta(host, "_mouseHook", kanca);
        AlanAta(host, "_hoverInside", true);
        kanca.GetType().GetEvent("BirakisGeldi")!.AddEventHandler(kanca, new Action(() => Cagir(host, "UpdateMouseHook")));
        return host;
    }

    /// <summary>Açılışta (Start) olaya verilen yöntem: add_ çağrısından önceki ldftn.</summary>
    private static string? Abone(Type kaynak, string olay)
    {
        List<Komut> acilis = Komutlar(Tip("Kalender.Core.AppHost").GetMethod("Start", Hepsi)!);
        int ekle = Bul(acilis, x => Cagri(x, kaynak, "add_" + olay));
        return ekle > 1 && acilis[ekle - 2].Kod == OpCodes.Ldftn ? acilis[ekle - 2].Uye?.Name : null;
    }

    // ------------------------------------------------------------------ 23. madde

    private static void Madde23()
    {
        Console.WriteLine("23) Menü açıkken ay okuna basılınca önce menü kapanıyor, ay sonra değişiyor");
        Type hostTipi = Tip("Kalender.Core.AppHost");
        object sonraki = Enum.Parse(Tip("Kalender.Calendar.NavButton"), "Next");
        var ay = new DateOnly(2030, 3, 1);

        // Uygulamanın kendisi kurulmadan: katman, veri ve Google yok, ay yalnız bellekte değişir.
        object host = RuntimeHelpers.GetUninitializedObject(hostTipi);
        AlanAta(host, "_viewMonth", ay);
        AlanAta(host, "_menuOpen", true);
        Cagir(host, "OnNavClicked", sonraki);
        Denetle("menü açıkken ay değişmiyor; düğme, menü kapanınca uygulanmak üzere bekliyor",
            (DateOnly)AlanOku(host, "_viewMonth") == ay && Equals(AlanOku(host, "_pendingNav"), sonraki));

        AlanAta(host, "_menuOpen", false);
        AlanAta(host, "_pendingNav", null);
        Cagir(host, "OnNavClicked", sonraki);
        Denetle("menü kapalıyken ok ayı hemen değiştiriyor", (DateOnly)AlanOku(host, "_viewMonth") == ay.AddMonths(1));

        List<Komut> ok = Komutlar(hostTipi.GetMethod("OnNavClicked", Hepsi)!);
        int acikMi = Bul(ok, x => AlanKomutu(x, OpCodes.Ldfld, "_menuOpen"));
        int kapat = Bul(ok, x => Cagri(x, Tip("Kalender.Interop.NativeMethods"), "EndMenu"), acikMi);
        Denetle("menü açıksa ok önce menüyü kapatıyor (EndMenu)", acikMi >= 0 && kapat > acikMi);

        // Menü kapanınca (finally'den sonra) bekleyen düğme silinip uygulanıyor.
        MethodInfo sagTik = hostTipi.GetMethod("OnCalendarRightClick", Hepsi)!;
        List<Komut> k = Komutlar(sagTik);
        int show = Bul(k, x => Cagri(x, Tip("Kalender.Interop.PopupMenu"), "Show"));
        ExceptionHandlingClause? sonunda = show >= 0 ? Finally(sagTik, k[show].Yer) : null;
        int sonrasi = sonunda is null ? -1 : Bul(k, x => x.Yer >= sonunda.HandlerOffset + sonunda.HandlerLength);
        int bekleyen = sonrasi < 0 ? -1
            : Bul(k, x => AlanKomutu(x, OpCodes.Ldfld, "_pendingNav") || AlanKomutu(x, OpCodes.Ldflda, "_pendingNav"), sonrasi);
        // Boş değer atanan Nullable alan: ldflda + initobj.
        int sil = -1;
        for (int i = bekleyen + 1; bekleyen >= 0 && i < k.Count - 1 && sil < 0; i++)
        {
            if (AlanKomutu(k[i], OpCodes.Stfld, "_pendingNav")
                || (AlanKomutu(k[i], OpCodes.Ldflda, "_pendingNav") && k[i + 1].Kod == OpCodes.Initobj))
            {
                sil = i;
            }
        }

        int uygula = sil < 0 ? -1 : Bul(k, x => Cagri(x, hostTipi, "OnNavClicked"), sil);
        Denetle("menü kapanınca bekleyen düğme silinip uygulanıyor",
            sonrasi >= 0 && bekleyen >= sonrasi && sil > bekleyen && uygula > sil, $"{bekleyen} / {sil} / {uygula}");
    }

    // ------------------------------------------------------------------ 24. madde

    private static void Madde24()
    {
        Console.WriteLine("24) Tuş basılıyken imleç takvimden çıkınca kanca bırakışı bekliyor (kanca takılmaz)");
        const uint LDown = 0x0201, LUp = 0x0202, RDown = 0x0204, RUp = 0x0205;
        _menuNoktalari.Clear();
        _dugmeler.Clear();
        object kanca = Kanca();
        object host = Uygulama(kanca);

        // Sağ tık: takvimde basılıyor, imleç takvimden çıkıyor (HoverWatcher bildirir), dışarıda bırakılıyor.
        bool basis = Gonder(kanca, RDown, 100);
        AlanAta(host, "_hoverInside", false);
        Cagir(host, "UpdateMouseHook");
        Denetle("sağ tuş basılıyken imleç takvimden çıkınca kanca takılı kalıyor",
            basis && Takili(kanca) && (bool)Oku(kanca, "BirakisBekleniyor")!);
        bool birakis = Gonder(kanca, RUp, 1500);
        Bosalt();
        Denetle("dışarıda bırakılan tuş kabuğa gitmiyor, menü basılan noktada açılıyor",
            birakis && _menuNoktalari.SequenceEqual(new[] { new DPoint(100, 10) }), string.Join(" ", _menuNoktalari));
        Denetle("bırakıştan sonra kanca sökülüyor", !Takili(kanca));

        // Sol tık: ay okuna basılıyor, imleç çıkıyor, dışarıda bırakılıyor.
        AlanAta(kanca, "_hook", new IntPtr(1));
        AlanAta(host, "_hoverInside", true);
        basis = Gonder(kanca, LDown, 520);
        AlanAta(host, "_hoverInside", false);
        Cagir(host, "UpdateMouseHook");
        bool bekledi = Takili(kanca);
        birakis = Gonder(kanca, LUp, 1500);
        Bosalt();
        Denetle("sol tuşta da: ok basılıyken çıkınca kanca bırakışı bekliyor, bırakış saklanıyor, sonra sökülüyor",
            basis && bekledi && birakis && !Takili(kanca) && _dugmeler.SequenceEqual(new[] { "Previous" }), string.Join(",", _dugmeler));

        AlanAta(kanca, "_hook", new IntPtr(1));
        Cagir(host, "UpdateMouseHook");
        Denetle("bekleyen bırakış yoksa imleç çıkınca kanca hemen sökülüyor", !Takili(kanca));

        AlanAta(kanca, "_hook", new IntPtr(1));
        AlanAta(host, "_hoverInside", true);
        _menuNoktalari.Clear();
        bool icerde = Gonder(kanca, RDown, 300) && Gonder(kanca, RUp, 302);
        Bosalt();
        Denetle("takvimin içinde sağ tık menüyü açıyor, kanca takılı kalıyor",
            icerde && _menuNoktalari.SequenceEqual(new[] { new DPoint(300, 10) }) && Takili(kanca));

        // Ay değişirken (ChangeMonth) kanca, bekleyen bırakışa bakılmadan sökülüyor.
        AlanAta(host, "_hoverInside", false);
        Gonder(kanca, RDown, 100);
        Cagir(host, "ChangeMonth", new Action(() => { }));
        Denetle("ay değişirken kanca bekleyen bırakış olsa da sökülüyor",
            !Takili(kanca) && !(bool)AlanOku(kanca, "_swallowingUp"));

        Denetle("açılışta bırakış olayı UpdateMouseHook'a bağlanıyor",
            Abone(kanca.GetType(), "BirakisGeldi") == "UpdateMouseHook");
    }

    // ------------------------------------------------------------------ 36. madde

    private static void Madde36()
    {
        Console.WriteLine("36) Windows'un sessizce söktüğü fare kancası yeniden takılıyor (kanca takılmaz)");
        const uint Hareket = 0x0200;
        object kanca = Kanca();
        long simdi = Environment.TickCount64;
        bool Sokulmus(long onceki) => (bool)Cagir(kanca, "Sokulmus", onceki)!;

        AlanAta(kanca, "_armedAt", simdi - 5000);
        AlanAta(kanca, "_lastCallAt", simdi - 3000);
        Denetle("imleç oynadığı hâlde takılı kanca hiç çağrılmadıysa sökülmüş sayılıyor", Sokulmus(simdi - 1000));

        Gonder(kanca, Hareket, 1500);
        Denetle("hareket kancadan geçtiyse sökülmüş sayılmıyor", !Sokulmus(simdi - 1000));

        AlanAta(kanca, "_lastCallAt", 0L);
        AlanAta(kanca, "_armedAt", simdi);
        Denetle("önceki okumadan sonra takılan kanca için karar verilmiyor", !Sokulmus(simdi - 1000));

        AlanAta(kanca, "_armedAt", simdi - 5000);
        AlanAta(kanca, "_hook", IntPtr.Zero);
        Denetle("takılı olmayan kanca sökülmüş sayılmıyor", !Sokulmus(simdi - 1000));

        // Uygulama: Windows'un söktüğü kanca sökülüp (durumu sıfırlanır) yeniden takılır.
        Type hostTipi = Tip("Kalender.Core.AppHost");
        object host = Uygulama(kanca);
        AlanAta(kanca, "_hook", new IntPtr(1));
        AlanAta(kanca, "_swallowingUp", true);
        Cagir(host, "KancayiDenetle", simdi - 1000);
        Denetle("ölü kanca sökülüyor, bekleyen bırakışı unutuluyor",
            !Takili(kanca) && !(bool)AlanOku(kanca, "_swallowingUp"));

        List<Komut> d = Komutlar(hostTipi.GetMethod("KancayiDenetle", Hepsi)!);
        int takiliMi = Bul(d, x => Cagri(x, kanca.GetType(), "get_IsArmed"));
        int sor = Bul(d, x => Cagri(x, kanca.GetType(), "Sokulmus"), takiliMi);
        int sok = Bul(d, x => Cagri(x, kanca.GetType(), "Disarm"), sor);
        int tak = Bul(d, x => Cagri(x, hostTipi, "UpdateMouseHook"), sok);
        Denetle("söküldükten sonra UpdateMouseHook ile yeniden takılıyor", sor >= 0 && sok > sor && tak > sok);
        Denetle("sökülü kanca (ör. ay çizilirken) burada takılmıyor", takiliMi >= 0 && takiliMi < sor);

        AlanAta(kanca, "_hook", new IntPtr(1));
        Gonder(kanca, Hareket, 300);
        Cagir(host, "KancayiDenetle", simdi - 1000);
        Denetle("çalışan kancaya dokunulmuyor", Takili(kanca));

        // Bırakışı beklenen basış artık beklenmiyorsa imleç dışarıdayken kanca kalmıyor.
        AlanAta(host, "_hoverInside", false);
        Cagir(host, "KancayiDenetle", simdi - 1000);
        Denetle("imleç takvimin dışındayken kanca her harekette yeniden değerlendiriliyor", !Takili(kanca));

        Denetle("açılışta imleç hareketi KancayiDenetle'ye bağlanıyor",
            Abone(Tip("Kalender.Desktop.HoverWatcher"), "ImlecOynadi") == "KancayiDenetle");

        // Yoklama, imleç yer değiştirince önceki okumanın anını bildiriyor.
        Type izleyiciTipi = Tip("Kalender.Desktop.HoverWatcher");
        object izleyici = RuntimeHelpers.GetUninitializedObject(izleyiciTipi);
        AlanAta(izleyici, "_layer", Yonetici());
        AlanAta(izleyici, "_lastPoint", new DPoint(int.MinValue, int.MinValue));
        AlanAta(izleyici, "_lastPointAt", 12345L);
        var gelen = new List<long>();
        izleyiciTipi.GetEvent("ImlecOynadi")!.AddEventHandler(izleyici, new Action<long>(gelen.Add));
        long once = Environment.TickCount64;
        Cagir(izleyici, "Tick");
        Denetle("yoklama imleç oynayınca önceki okumanın anını bildiriyor, yeni anı saklıyor",
            gelen.SequenceEqual(new[] { 12345L }) && (long)AlanOku(izleyici, "_lastPointAt") >= once,
            string.Join(",", gelen));
    }

    // ------------------------------------------------------------------ 18. madde

    /// <summary>
    /// Google'ın tatil akışının sahtesi: gövdeyi ya da "değişmedi" (304)
    /// yanıtını bu program verir, ağa çıkılmaz. İsteklerin koşul başlıkları
    /// (If-None-Match, If-Modified-Since) kaydedilir.
    /// </summary>
    private sealed class SahteTatilAkisi : HttpMessageHandler
    {
        public string Govde = string.Empty;
        public bool Degismedi;
        public readonly List<string> Kosullar = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken iptal)
        {
            Kosullar.Add(string.Join(";", istek.Headers
                .Where(b => b.Key is "If-None-Match" or "If-Modified-Since")
                .Select(b => b.Key + "=" + string.Join(",", b.Value))));

            if (Degismedi)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }

            var yanit = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Govde, Encoding.UTF8, "text/calendar"),
            };
            yanit.Headers.TryAddWithoutValidation("ETag", "\"yeni\"");
            return Task.FromResult(yanit);
        }
    }

    /// <summary>1.12'nin yazdığı tatil önbelleğinin biçimi; yeni dosyanın eski sürümce okunabildiğini sınamak için.</summary>
    private sealed class EskiTatilOnbellegi
    {
        public DateTimeOffset FetchedUtc { get; set; }

        public string? ETag { get; set; }

        public string? LastModified { get; set; }

        public List<EskiTatil> Days { get; set; } = new();
    }

    private sealed class EskiTatil
    {
        public DateOnly Date { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public bool DateUncertain { get; set; }
    }

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

    private static void Madde18()
    {
        Console.WriteLine("18) Ay düğmeleriyle uzak aylara gidince de akıştaki tatiller kırmızı");
        Type servisTipi = Tip("Kalender.Calendar.Holidays.HolidayService");

        // Açıklamalar Google'ın akışındaki gibi (ICS kaçışlarıyla).
        string akis = TatilAkisi(
            ("20210715", "20210716", "Demokrasi ve Millî Birlik Günü", "Resmi tatil"),
            ("20260526", "20260527", "Kurban Bayramı Arifesi", @"Resmi tatil\nBu\, yarım günlük bir tatildir."),
            ("20260527", "20260531", "Kurban Bayramı", "Resmi tatil"),
            ("20260510", "20260511", "Anneler Günü", @"Kutlama\nKutlama günlerini gizlemek için Google Takvim Ayarları"),
            ("20311029", "20311030", "Cumhuriyet Bayramı", @"Resmi tatil\nTarih\, geçicidir ve değişebilir."));

        // Süzme indirme ayına bakmıyor: bugünden yıllarca önceki ve sonraki günler de kalıyor.
        object kayitlar = Statik(Tip("Kalender.Calendar.Holidays.IcsParser"), "Parse", akis)!;
        Dictionary<DateOnly, object> gunler = ((IEnumerable)Statik(servisTipi, "Extract", kayitlar)!).Cast<object>()
            .ToDictionary(g => (DateOnly)Oku(g, "Date")!);
        Denetle("2021'deki tatil saklanıyor", gunler.ContainsKey(new DateOnly(2021, 7, 15)));
        Denetle("2031'deki tatil saklanıyor", gunler.ContainsKey(new DateOnly(2031, 10, 29)));
        Denetle("çok günlük bayramın her günü saklanıyor, bitiş günü dâhil değil",
            Enumerable.Range(27, 4).All(g => gunler.ContainsKey(new DateOnly(2026, 5, g)))
            && !gunler.ContainsKey(new DateOnly(2026, 5, 31)));
        Denetle("kutlama günü yine dışarıda", !gunler.ContainsKey(new DateOnly(2026, 5, 10)));
        Denetle("arife yarım gün, geçici tarih işaretli, toplam 7 gün",
            gunler.Count == 7
            && Oku(gunler[new DateOnly(2026, 5, 26)], "Kind")!.ToString() == "HalfDay"
            && (bool)Oku(gunler[new DateOnly(2031, 10, 29)], "DateUncertain")!,
            gunler.Count + " gün");

        // 1.12'nin önbelleği: yalnız 14 aylık pencere, FullFeed alanı yok, 5 gün önce alınmış.
        string onbellek = Path.Combine(
            (string)Tip("Kalender.Core.Paths").GetProperty("CacheFolder", Hepsi)!.GetValue(null)!, "tatiller.json");
        Directory.CreateDirectory(Path.GetDirectoryName(onbellek)!);
        File.WriteAllText(onbellek,
            $$"""{"FetchedUtc":"{{DateTimeOffset.UtcNow.AddDays(-5):O}}","ETag":"\"eski\"","Days":[{"Date":"2026-10-29","Name":"Cumhuriyet Bayramı","Kind":"Full","DateUncertain":false}]}""",
            new UTF8Encoding(false));

        object servis = Activator.CreateInstance(servisTipi, true)!;
        var sahte = new SahteTatilAkisi { Govde = akis };
        ((IDisposable)AlanOku(servis, "_http")).Dispose();
        AlanAta(servis, "_http", new HttpClient(sahte));
        object servis2 = Activator.CreateInstance(servisTipi, true)!;
        try
        {
            void Yenile() => ((Task)Cagir(servis, "RefreshAsync", false, CancellationToken.None)!).GetAwaiter().GetResult();
            bool Var(object s, DateOnly gun) => ((IDictionary)Oku(s, "Days")!).Contains(gun);

            Cagir(servis, "LoadCache");
            Denetle("eski sürümün önbelleği okunuyor, tatili hemen görünüyor",
                (int)Oku(servis, "Count")! == 1 && Var(servis, new DateOnly(2026, 10, 29)));

            Yenile();
            Denetle("eski önbellek 30 gün beklenmeden yeniden indiriliyor", sahte.Kosullar.Count == 1, sahte.Kosullar.Count + " istek");
            Denetle("eski önbellek için koşulsuz istek (304 eksik listeyi bırakmasın)",
                sahte.Kosullar.Count == 1 && sahte.Kosullar[0].Length == 0, string.Join(" | ", sahte.Kosullar));
            Denetle("akıştaki bütün tatiller bellekte",
                (int)Oku(servis, "Count")! == 7 && Var(servis, new DateOnly(2021, 7, 15)) && Var(servis, new DateOnly(2031, 10, 29)));

            string yazilan = File.ReadAllText(onbellek);
            Denetle("yeni önbellek bütün akışı tuttuğunu yazıyor", yazilan.Contains("\"FullFeed\":true", StringComparison.Ordinal));
            EskiTatilOnbellegi? eskiOkuma = JsonSerializer.Deserialize<EskiTatilOnbellegi>(yazilan);
            Denetle("yeni dosyayı eski sürüm de okuyabiliyor (tanımadığı alan atlanır)",
                eskiOkuma is { ETag: "\"yeni\"", Days.Count: 7 } && eskiOkuma.Days.Any(d => d.Date == new DateOnly(2021, 7, 15)));

            sahte.Kosullar.Clear();
            Yenile();
            Denetle("tam önbellek 30 gün dolmadan yeniden indirilmiyor", sahte.Kosullar.Count == 0, sahte.Kosullar.Count + " istek");

            object kayit = AlanOku(servis, "_cache");
            Yaz(kayit, "FetchedUtc", DateTimeOffset.UtcNow.AddDays(-31));
            sahte.Degismedi = true;
            Yenile();
            Denetle("süresi dolunca koşullu istek atılıyor",
                sahte.Kosullar.Count == 1 && sahte.Kosullar[0] == "If-None-Match=\"yeni\"", string.Join(" | ", sahte.Kosullar));
            Denetle("304'te bütün liste korunuyor, alınma tarihi ileri alınıyor",
                (int)Oku(servis, "Count")! == 7 && (DateTimeOffset)Oku(kayit, "FetchedUtc")! > DateTimeOffset.UtcNow.AddMinutes(-1));

            // Takvimin gördüğü: yeni önbellekle açılışta, ‹ › ile uzak aylara gidilince.
            Cagir(servis2, "LoadCache");
            Denetle("yeni önbellek açılışta okunuyor, tam olduğu biliniyor",
                (int)Oku(servis2, "Count")! == 7 && (bool)Oku(AlanOku(servis2, "_cache"), "FullFeed")!);
            object depo = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"), true)!;
            object veri = Activator.CreateInstance(Tip("Kalender.Calendar.CalendarDataService"), servis2, null, depo)!;
            bool Kirmizi(DateOnly gun)
            {
                Cagir(veri, "SetViewMonth", new DateOnly(gun.Year, gun.Month, 1));
                object? icerik = Cagir(Oku(veri, "Current")!, "For", gun);
                return icerik is not null && (bool)Oku(icerik, "IsHoliday")!;
            }

            Denetle("‹ ile Temmuz 2021'e gidilince 15 Temmuz kırmızı", Kirmizi(new DateOnly(2021, 7, 15)));
            Denetle("‹ ile Mayıs 2026'ya gidilince Kurban Bayramı kırmızı", Kirmizi(new DateOnly(2026, 5, 27)));
            Denetle("› ile Ekim 2031'e gidilince 29 Ekim kırmızı", Kirmizi(new DateOnly(2031, 10, 29)));
        }
        finally
        {
            ((IDisposable)servis).Dispose();
            ((IDisposable)servis2).Dispose();
        }
    }

    // ------------------------------------------------------------------ 34. madde

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

    private static void Madde34()
    {
        Console.WriteLine("34) Saat dilimi değişince gün ve gece yarısı yeni dilime göre");
        Type tip = Tip("Kalender.Time.DayChangeWatcher");

        TimeZoneInfo.ClearCachedData();
        DateTime utc = DateTime.UtcNow;
        DateOnly gercekGun = DateOnly.FromDateTime(DateTime.Now);
        TimeSpan gercekFark = TimeZoneInfo.Local.GetUtcOffset(utc);

        // Sahte dilim başka bir günde olsun: UTC−12 ile UTC+14 hep ayrı günlerde.
        TimeSpan fark = DateOnly.FromDateTime(utc.AddHours(-12)) != gercekGun ? TimeSpan.FromHours(-12) : TimeSpan.FromHours(14);
        object? izleyici = null;
        try
        {
            SahteDilimKoy(fark);
            Denetle("sınama düzeni: süreç başka bir dilimi önbelleğe almış (Windows'un ayarı değişmedi)",
                TimeZoneInfo.Local.Id == "Sahte Dilim" && DateTimeOffset.Now.Offset == fark, DateTimeOffset.Now.Offset.ToString());

            izleyici = Activator.CreateInstance(tip, true)!;
            DateOnly eskiGun = (DateOnly)Oku(izleyici, "Today")!;
            var sira = new List<string>();
            tip.GetEvent("DayChanged")!.AddEventHandler(izleyici, new Action<DateOnly>(g => sira.Add("gün " + g.ToString("yyyy-MM-dd"))));
            tip.GetEvent("TimeZoneChanged")!.AddEventHandler(izleyici, new Action(() => sira.Add("dilim")));

            // WM_TIMECHANGE gelince uygulama Recheck'i çağırıyor.
            Cagir(izleyici, "Recheck");
            DateOnly yeniGun = (DateOnly)Oku(izleyici, "Today")!;
            Denetle("saat değişiminde dilim Windows'tan yeniden okunuyor",
                TimeZoneInfo.Local.Id != "Sahte Dilim" && DateTimeOffset.Now.Offset == gercekFark, DateTimeOffset.Now.Offset.ToString());
            Denetle("bugün yeni dilime göre (eski dilimde başka gündü)", eskiGun != gercekGun && yeniGun == gercekGun,
                $"{eskiGun:yyyy-MM-dd} → {yeniGun:yyyy-MM-dd}");
            Denetle("önce gün değişimi, sonra bir kez dilim değişimi bildiriliyor",
                sira.SequenceEqual(new[] { "gün " + gercekGun.ToString("yyyy-MM-dd"), "dilim" }), string.Join(", ", sira));

            DateTime simdi = DateTime.Now;
            TimeSpan beklenen = simdi.Date.AddDays(1).AddSeconds(2) - simdi;
            if (beklenen > TimeSpan.FromHours(6))
            {
                beklenen = TimeSpan.FromHours(6);
            }

            TimeSpan aralik = ((DispatcherTimer)AlanOku(izleyici, "_timer")).Interval;
            Denetle("gece yarısı zamanlayıcısı yeni dilimin gece yarısına kuruluyor",
                (aralik - beklenen).Duration() < TimeSpan.FromSeconds(5), $"{aralik.TotalSeconds:0} / {beklenen.TotalSeconds:0} sn");

            sira.Clear();
            Cagir(izleyici, "Recheck");
            Denetle("dilim değişmeyince yeniden bildirilmiyor", sira.Count == 0, string.Join(", ", sira));
        }
        finally
        {
            TimeZoneInfo.ClearCachedData();
            (izleyici as IDisposable)?.Dispose();
        }

        // Dilim değişince Google etkinlikleri yeni dilimle baştan alınıyor
        // (eşitlemenin kendisi GoogleEsitleme sınamasında).
        Type host = Tip("Kalender.Core.AppHost");
        List<Komut> basla = Komutlar(host.GetMethod("Start", Hepsi)!);
        int abone = Bul(basla, x => Cagri(x, tip, "add_TimeZoneChanged"));
        Denetle("uygulama dilim değişimine abone oluyor",
            abone > 0 && basla.Take(abone).LastOrDefault(x => x.Kod == OpCodes.Ldftn)?.Uye is MethodBase m && m.Name == "OnTimeZoneChanged");

        List<Komut> isle = Komutlar(host.GetMethod("OnTimeZoneChanged", Hepsi)!);
        int birak = Bul(isle, x => Cagri(x, Tip("Kalender.Google.GoogleCalendarProvider"), "SaatDilimiDegisti"));
        int yenile = Bul(isle, x => Cagri(x, Tip("Kalender.Google.GooglePoller"), "RefreshAsync"));
        Denetle("önce eldeki eski dilimli veri bırakılıyor, sonra görünen ay zorlanmış yenilemeyle alınıyor",
            birak >= 0 && yenile > birak && isle[yenile - 1].Kod == OpCodes.Ldc_I4_1, $"{birak} / {yenile}");
    }

    // ------------------------------------------------------------------ 12. madde

    private const string AyriMasaustu = "KalenderKodIncelemesi";

    /// <summary>
    /// Gün kutusu gerçek bir pencere olduğu için bu bölüm görünmeyen ayrı bir
    /// Windows masaüstünde, bu programın ikinci bir kopyasında çalışır (Lite'taki
    /// CokluEkran sınamasının yolu); kullanıcının ekranına pencere açılmaz.
    /// Ölçeği farklı ekrana geçişte Windows'un gönderdiği WM_DPICHANGED elle
    /// gönderilir; tek ekranlı bilgisayarda da çalışır.
    /// </summary>
    private static void Madde12(string gecici)
    {
        Console.WriteLine("12) Gün kutusu ölçeği farklı ekranda da hücrenin altında açılıyor (görünmeyen ayrı masaüstünde)");
        IntPtr masa = CreateDesktop(AyriMasaustu, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000 /*GENERIC_ALL*/, IntPtr.Zero);
        if (masa == IntPtr.Zero)
        {
            Denetle("görünmeyen masaüstü açıldı", false, $"hata {Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            string cikti = Path.Combine(gecici, "gun-kutusu.txt");
            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = AyriMasaustu };
            string komut = $"\"{Environment.ProcessPath}\" --gun-kutusu \"{cikti}\"";
            if (!CreateProcess(null, komut, IntPtr.Zero, IntPtr.Zero, false, 0x08000000 /*CREATE_NO_WINDOW*/,
                    IntPtr.Zero, null, ref si, out PROCESS_INFORMATION pi))
            {
                Denetle("ikinci kopya başlatıldı", false, $"hata {Marshal.GetLastWin32Error()}");
                return;
            }

            bool bitti = WaitForSingleObject(pi.hProcess, 120_000) == 0;
            if (!bitti)
            {
                TerminateProcess(pi.hProcess, 5);
            }

            GetExitCodeProcess(pi.hProcess, out int kod);
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);

            string[] satirlar = File.Exists(cikti) ? File.ReadAllLines(cikti) : Array.Empty<string>();
            int kalan = 0;
            foreach (string satir in satirlar)
            {
                Console.WriteLine(satir);
                string s = satir.TrimStart();
                if (s.StartsWith("GEÇTİ:", StringComparison.Ordinal))
                {
                    _gecen++;
                }
                else if (s.StartsWith("KALDI:", StringComparison.Ordinal) || s.StartsWith("BEKLENMEYEN", StringComparison.Ordinal))
                {
                    _hata++;
                    kalan++;
                }
            }

            if (!bitti || satirlar.Length == 0 || (kod != 0 && kalan == 0))
            {
                Denetle("ikinci kopya bitti ve sonucunu yazdı", false, $"bitti: {bitti}, çıkış kodu {kod}");
            }
        }
        finally
        {
            CloseDesktop(masa);
        }
    }

    /// <summary>Görünmeyen masaüstündeki ikinci kopya: yalnızca gün kutusu bölümü.</summary>
    private static int GunKutusuKopyasi(string cikti)
    {
        using var yazici = new StreamWriter(cikti) { AutoFlush = true };
        Console.SetOut(yazici);
        try
        {
            // WPF ölçek değişimini yalnızca ekran başına ölçek bilen süreçte
            // işler (uygulamanın kendisi app.manifest ile öyle).
            SetProcessDpiAwarenessContext(new IntPtr(-4) /*PER_MONITOR_AWARE_V2*/);
            GunKutusu();
        }
        catch (Exception ex)
        {
            _hata++;
            Console.WriteLine("   BEKLENMEYEN HATA: " + ex);
        }

        return _hata == 0 ? 0 : 1;
    }

    private static void GunKutusu()
    {
        Type pencereTipi = Tip("Kalender.Ui.DayPopupWindow");
        var kutu = (System.Windows.Window)Activator.CreateInstance(pencereTipi)!;
        object icerik = GunIcerigi();

        var calisma = (DRectangle)Statik(Tip("Kalender.Interop.MonitorEnumerator"), "WorkAreaAt", new DPoint(0, 0))!;
        var hucre = new DRectangle(calisma.X + (calisma.Width / 2) - 40, calisma.Y + (calisma.Height / 2) - 30, 80, 60);
        double olcek = (double)Statik(pencereTipi, "EkranOlcegi", new DPoint(hucre.X + 40, hucre.Y + 30))!;
        uint ekranDpi = (uint)Math.Round(olcek * 96);
        Denetle("hücrenin bulunduğu ekranın ölçeği okunuyor", olcek > 0, $"%{olcek * 100:0}");

        IntPtr hwnd = IntPtr.Zero;
        DPoint Yer()
        {
            GetWindowRect(hwnd, out RECT r);
            return new DPoint(r.Left, r.Top);
        }

        // Reposition'ın hesabı: hücrenin altında ortalı, boyut hücrenin ekranının ölçeğiyle.
        DPoint Beklenen()
        {
            int genislik = (int)Math.Round(kutu.ActualWidth * olcek);
            return new DPoint(hucre.X + (hucre.Width / 2) - (genislik / 2), hucre.Bottom - (int)Math.Round(10 * olcek));
        }

        static bool Yakin(DPoint a, DPoint b) => Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1;

        Cagir(kutu, "ShowFor", icerik, hucre);
        hwnd = new System.Windows.Interop.WindowInteropHelper(kutu).Handle;
        Bekle(300);
        Denetle("kutu hücrenin altında, ortalı açılıyor", Yakin(Yer(), Beklenen()), $"{Yer()}, beklenen {Beklenen()}");

        // Windows'un ölçeği farklı ekrana geçişte gönderdiği ileti: yeni ölçek
        // ve "şuraya taşın" önerisi (burada çalışma alanının sol üst köşesi).
        uint yeniDpi = ekranDpi == 144 ? 96u : 144u;
        DpiIletisi(hwnd, yeniDpi, calisma.X, calisma.Y);
        Bekle(500);
        double pencereOlcegi = System.Windows.Media.VisualTreeHelper.GetDpi(kutu).DpiScaleX;
        Denetle("deneme geçerli: WPF ölçek değişimini işledi", Math.Abs(pencereOlcegi - (yeniDpi / 96.0)) < 0.01,
            $"pencere %{pencereOlcegi * 100:0}");
        Denetle("ölçek değişiminden sonra kutu yine hücrenin altında (WPF'in taşıması ezmiyor)",
            Yakin(Yer(), Beklenen()), $"{Yer()}, beklenen {Beklenen()}, öneri ({calisma.X},{calisma.Y})");

        Cagir(kutu, "HidePopup");
        DpiIletisi(hwnd, ekranDpi, calisma.X + 50, calisma.Y + 50);
        Bekle(300);
        Denetle("kapalı kutu ölçek değişiminde görünür olmuyor", !IsWindowVisible(hwnd));

        Cagir(kutu, "ShowFor", icerik, hucre);
        Bekle(300);
        Denetle("yeniden açılınca yine hücrenin altında", Yakin(Yer(), Beklenen()), $"{Yer()}, beklenen {Beklenen()}");
        kutu.Close();
    }

    /// <summary>Bir saatli kayıt içeren gün.</summary>
    private static object GunIcerigi()
    {
        Type girdiTipi = Tip("Kalender.Calendar.DayEntry");
        object girdi = Activator.CreateInstance(girdiTipi, true)!;
        Yaz(girdi, "Kind", Enum.Parse(Tip("Kalender.Calendar.EntryKind"), "Timed"));
        Yaz(girdi, "Title", "Deneme toplantısı");
        Yaz(girdi, "Start", new TimeOnly(10, 30));
        Yaz(girdi, "DotColor", DColor.SteelBlue);
        Array girdiler = Array.CreateInstance(girdiTipi, 1);
        girdiler.SetValue(girdi, 0);

        object icerik = Activator.CreateInstance(Tip("Kalender.Calendar.DayContent"), true)!;
        Yaz(icerik, "Date", new DateOnly(2026, 10, 12));
        Yaz(icerik, "Entries", girdiler);
        return icerik;
    }

    /// <summary>Pencereye WM_DPICHANGED gönderir; önerilen dikdörtgen verilen köşede, boyutu aynı.</summary>
    private static void DpiIletisi(IntPtr hwnd, uint dpi, int x, int y)
    {
        GetWindowRect(hwnd, out RECT r);
        var oneri = new RECT { Left = x, Top = y, Right = x + (r.Right - r.Left), Bottom = y + (r.Bottom - r.Top) };
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            Marshal.StructureToPtr(oneri, p, false);
            SendMessage(hwnd, 0x02E0 /*WM_DPICHANGED*/, (IntPtr)((dpi << 16) | dpi), p);
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    /// <summary>Dağıtıcıyı verilen süre boyunca çalıştırır.</summary>
    private static void Bekle(int ms)
    {
        var cerceve = new DispatcherFrame();
        var zaman = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Background,
            (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                cerceve.Continue = false;
            },
            Dispatcher.CurrentDispatcher);
        zaman.Start();
        Dispatcher.PushFrame(cerceve);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string ad, IntPtr aygit, IntPtr kip, int bayraklar, uint erisim, IntPtr guvenlik);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr masa);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string? uygulama, string komut, IntPtr sg, IntPtr ig, bool miras, uint bayraklar,
        IntPtr ortam, string? klasor, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr h, uint ms);

    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr h, uint kod);

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr h, out int kod);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr h, uint ileti, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr h, out RECT r);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr h);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr baglam);
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

/// <summary>
/// Ayarlar penceresinin uygulamaya açılan kapısının (IAppearanceHost) sahtesi:
/// pencere kurulmadan işleyicilerini sınamak için. Duvar kâğıdına hiçbir şey
/// yazmaz; ApplyAppearance çağrıları yalnızca sayılır.
/// </summary>
public class SahteAyarKapisi : DispatchProxy
{
    /// <summary>Ayar deposu (SettingsStore).</summary>
    public object? Ayarlar { get; set; }

    /// <summary>Güncelleme hizmeti (UpdateService); pencere yalnızca olayına abone olur.</summary>
    public object? Guncellemeler { get; set; }

    /// <summary>Bağlı ekranlar (MonitorTarget listesi).</summary>
    public object? Ekranlar { get; set; }

    /// <summary>ApplyAppearance kaç kez çağrıldı.</summary>
    public int Uygulama { get; private set; }

    /// <summary>Google'dan zorlanmış (tam) yenileme kaç kez istendi.</summary>
    public int ZorlanmisYenileme { get; private set; }

    protected override object? Invoke(MethodInfo? yontem, object?[]? a)
    {
        string ad = yontem!.Name;
        switch (ad)
        {
            case "get_Settings":
                return Ayarlar;

            case "get_Updates":
                return Guncellemeler;

            case "get_Monitors":
                return Ekranlar;

            case "ApplyAppearance":
                Uygulama++;
                return null;

            case "RefreshGoogleAsync":
                if (a?[0] is true)
                {
                    ZorlanmisYenileme++;
                }

                return Task.CompletedTask;

            default:
                if (ad.StartsWith("add_", StringComparison.Ordinal) || ad.StartsWith("remove_", StringComparison.Ordinal))
                {
                    return null;
                }

                throw new NotSupportedException(ad);
        }
    }
}
