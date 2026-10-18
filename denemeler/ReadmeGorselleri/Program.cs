using System.Collections;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

// README ekran görüntülerini üretir: uygulamanın kendi çizim koduyla (yansımayla)
// Ekim 2026 takvimini, uydurma örnek etkinlik ve görevlerle çizer; gün kutusunu
// da kendi WPF penceresinin içeriğinden alıp hücrenin altına yerleştirir.
//
// Gerçek masaüstüne, duvar kağıdına, ayar dosyasına ya da Google hesabına
// dokunmaz: uygulamanın kök klasörü çıktı klasörünün altındaki "calisma"ya
// çevrilir (tatil önbelleği ve hazır fotoğraflar oraya yazılır).
// Kullanım: dotnet run -c Release -- <çıktı klasörü>
internal static class Program
{
    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;

    private static readonly DateOnly Bugun = new(2026, 10, 14);

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

    private static object? Oku(object o, string ad) =>
        o.GetType().GetProperty(ad, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o);

    private static void Yaz(object o, string ad, object? deger) =>
        o.GetType().GetProperty(ad, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(o, deger);

    // Google Takvim'in kendi renkleri.
    private static readonly Color Domates = ColorTranslator.FromHtml("#D50000");
    private static readonly Color Mandalina = ColorTranslator.FromHtml("#F4511E");
    private static readonly Color Muz = ColorTranslator.FromHtml("#F6BF26");
    private static readonly Color Ada = ColorTranslator.FromHtml("#33B679");
    private static readonly Color Fesleğen = ColorTranslator.FromHtml("#0B8043");
    private static readonly Color Tavus = ColorTranslator.FromHtml("#039BE5");
    private static readonly Color Lavanta = ColorTranslator.FromHtml("#7986CB");
    private static readonly Color Uzum = ColorTranslator.FromHtml("#8E24AA");
    private static readonly Color Flamingo = ColorTranslator.FromHtml("#E67C73");
    private static readonly Color Gorev = ColorTranslator.FromHtml("#4C8DFF");

    private sealed record Kayit(int Tur, string Baslik, TimeOnly? Saat, Color Renk, bool Bitti = false);

    // Tür: 1 tüm gün, 2 saatli, 3 görev (uygulamadaki EntryKind sırası).
    private static Kayit Saatli(int sa, int dk, string baslik, Color renk) => new(2, baslik, new TimeOnly(sa, dk), renk);

    private static Kayit TumGun(string baslik, Color renk) => new(1, baslik, null, renk);

    private static Kayit Isi(string baslik, bool bitti = false) => new(3, baslik, null, Gorev, bitti);

    private static Dictionary<DateOnly, List<Kayit>> OrnekVeri()
    {
        var v = new Dictionary<DateOnly, List<Kayit>>();
        void Ekle(int ay, int gun, params Kayit[] k)
        {
            var d = new DateOnly(2026, ay, gun);
            if (!v.TryGetValue(d, out var l))
            {
                v[d] = l = new List<Kayit>();
            }

            l.AddRange(k);
        }

        Ekle(9, 30, Saatli(10, 0, "Haftalık ofis toplantısı", Tavus));
        Ekle(10, 1, Saatli(9, 30, "Duruşma", Domates), Isi("Tebligat takibi", bitti: true));
        Ekle(10, 2, Isi("Masraf avansı yatırılacak", bitti: true));
        Ekle(10, 5, Saatli(14, 0, "Müvekkil görüşmesi", Ada));
        Ekle(10, 6, TumGun("Cevap dilekçesi son gün", Muz), Isi("Delil listesi hazırlanacak", bitti: true));
        Ekle(10, 7, Saatli(10, 0, "Haftalık ofis toplantısı", Tavus));
        Ekle(10, 8, Saatli(11, 0, "Keşif", Mandalina));
        Ekle(10, 12, Saatli(9, 30, "Duruşma", Domates), Saatli(16, 30, "Diş hekimi", Uzum));
        Ekle(10, 14,
            Saatli(9, 30, "Duruşma", Domates),
            Saatli(10, 0, "Haftalık ofis toplantısı", Tavus),
            Saatli(14, 30, "Bilirkişi incelemesi", Mandalina),
            Isi("Harç ödemesi"),
            Isi("Vekâletname örneği gönderilecek", bitti: true));
        Ekle(10, 15, Isi("İstinaf dilekçesi"));
        Ekle(10, 16, Saatli(13, 0, "Arabuluculuk görüşmesi", Ada));
        Ekle(10, 19, Saatli(9, 30, "Duruşma", Domates), Isi("Kira ödemesi"));
        Ekle(10, 20, TumGun("Hukuk semineri", Lavanta));
        Ekle(10, 21, TumGun("Hukuk semineri", Lavanta), Saatli(10, 0, "Haftalık ofis toplantısı", Tavus));
        Ekle(10, 22, TumGun("Hukuk semineri", Lavanta));
        Ekle(10, 24, TumGun("Doğum günü", Flamingo));
        Ekle(10, 26, Saatli(11, 30, "Duruşma", Domates));
        Ekle(10, 27, Isi("Bilirkişi raporuna itiraz"));
        Ekle(10, 28, Saatli(10, 0, "Haftalık ofis toplantısı", Tavus));
        Ekle(11, 2, TumGun("Kasım planlaması", Fesleğen));
        Ekle(11, 4, Saatli(10, 0, "Haftalık ofis toplantısı", Tavus));
        return v;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string cikti = Path.GetFullPath(args.Length > 0 ? args[0] : "cikti");
        Directory.CreateDirectory(cikti);

        // Uygulamanın kök klasörü çıktının altına: gerçek ayar ve önbelleğe dokunulmaz.
        Tip("Kalender.Core.Paths").GetProperty("RootFolder")!.SetValue(null, Path.Combine(cikti, "calisma"));

        object anlik = Anlik();

        // En üstteki görüntüler: masaüstü simgeleri ve Windows 11 görev çubuğuyla dört zemin.
        // 3840×2160 ve %150 ölçek: 2560×1440, %100 bir ekranın (kullanıcınınki) bir buçuk kat keskin hâli.
        // Ust: takvim sağ üstte (simgelerin hizasında); sağında yine bir simge sütunu boş.
        var masaustleri = new (string Ad, string? Foto, string? Renk, bool Ortada, bool AcikSerit, bool Ust)[]
        {
            ("masaustu-1.jpg", null, "#0B0E13", false, false, false),
            ("masaustu-2.jpg", "sisli-dag-sirtlari", null, false, false, true),
            ("masaustu-3.jpg", "sisli-cam-ormani", null, true, false, false),
            ("masaustu-4.jpg", null, "#D9CFC1", true, true, false),
        };
        foreach (var m in masaustleri)
        {
            (Bitmap r, Rectangle blok) = Ciz(new Size(3840, 2160), anlik,
                m.Foto is null ? null : HazirFotograf(m.Foto), m.Renk, kutu: false, ust: m.Ust);
            Masaustu.Ciz(r, 1.5, blok, m.Ortada, m.AcikSerit);
            Kaydet(r, Path.Combine(cikti, m.Ad));
            Console.WriteLine(m.Ad);
        }

        // Gün kutusu: koyu düz zemin (Kuzgun), takvimin çevresi de görünsün, iki kat piksel.
        (Bitmap resim, Rectangle alan) = Ciz(new Size(5120, 2880), anlik, null, "#0B0E13", kutu: true, ust: false);
        // Takvim ekranın sağ altında: sağa ve alta ekran kenarına kadar, sola ve yukarı biraz masaüstü.
        int sag = Math.Min(resim.Width, alan.Right + 600);
        int alt = Math.Min(resim.Height, alan.Bottom + 300);
        alan = Rectangle.FromLTRB(Math.Max(0, alan.Left - 600), Math.Max(0, alan.Top - 300), sag, alt);
        using (Bitmap kirpik = resim.Clone(alan, PixelFormat.Format24bppRgb))
        {
            Kaydet(kirpik, Path.Combine(cikti, "gun-kutusu.png"));
        }

        Console.WriteLine("Bitti: " + cikti);
        return 0;
    }

    private static string HazirFotograf(string id)
    {
        Type t = Tip("Kalender.Settings.HazirFotograflar");
        var hepsi = (IEnumerable)t.GetProperty("All")!.GetValue(null)!;
        object secilen = hepsi.Cast<object>().First(p => (string)Oku(p, "Id")! == id);
        return (string)t.GetMethod("Prepare")!.Invoke(null, new[] { secilen })!;
    }

    /// <summary>Tatiller (Google'ın Türkiye takviminden) ve örnek kayıtlarla anlık görüntü.</summary>
    private static object Anlik()
    {
        object tatiller = Activator.CreateInstance(Tip("Kalender.Calendar.Holidays.HolidayService"))!;
        tatiller.GetType().GetMethod("LoadCache")!.Invoke(tatiller, null);
        var gorev = (Task)tatiller.GetType().GetMethod("RefreshAsync")!
            .Invoke(tatiller, new object[] { true, CancellationToken.None })!;
        gorev.GetAwaiter().GetResult();

        // Güne düşen tatiller (bir günde birden çok olabilir; tam günler önce).
        var tatilAdi = new Dictionary<DateOnly, IList>();
        foreach (object cift in (IEnumerable)Oku(tatiller, "Days")!)
        {
            tatilAdi[(DateOnly)Oku(cift, "Key")!] = (IList)Oku(cift, "Value")!;
        }

        Console.WriteLine($"Tatil günü: {tatilAdi.Count}");

        Type girdiT = Tip("Kalender.Calendar.DayEntry");
        Type icerikT = Tip("Kalender.Calendar.DayContent");
        Type turT = Tip("Kalender.Calendar.EntryKind");

        Dictionary<DateOnly, List<Kayit>> veri = OrnekVeri();
        var gunler = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(DateOnly), icerikT))!;

        (DateOnly ilk, DateOnly son) = (new DateOnly(2026, 9, 28), new DateOnly(2026, 11, 8));
        for (DateOnly d = ilk; d <= son; d = d.AddDays(1))
        {
            bool tatil = tatilAdi.TryGetValue(d, out var t);
            veri.TryGetValue(d, out var kayitlar);
            if (!tatil && kayitlar is null)
            {
                continue;
            }

            var liste = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(girdiT))!;
            foreach (object bilgi in t ?? new List<object>())
            {
                object g = Activator.CreateInstance(girdiT)!;
                Yaz(g, "Kind", Enum.ToObject(turT, 0));
                Yaz(g, "Title", Oku(bilgi, "Name"));
                Yaz(g, "YarimGun", (int)Oku(bilgi, "Kind")! != 0);
                liste.Add(g);
            }

            foreach (Kayit k in (kayitlar ?? new()).OrderBy(k => k.Tur == 3 ? 3 : k.Tur)
                         .ThenBy(k => k.Saat ?? TimeOnly.MinValue))
            {
                object g = Activator.CreateInstance(girdiT)!;
                Yaz(g, "Kind", Enum.ToObject(turT, k.Tur));
                Yaz(g, "Title", k.Baslik);
                Yaz(g, "Start", k.Saat);
                Yaz(g, "DotColor", k.Renk);
                Yaz(g, "Completed", k.Bitti);
                liste.Add(g);
            }

            object icerik = Activator.CreateInstance(icerikT)!;
            Yaz(icerik, "Date", d);
            if (tatil)
            {
                Yaz(icerik, "Tatiller", t);
            }

            Yaz(icerik, "Entries", liste);
            gunler[d] = icerik;
        }

        return Activator.CreateInstance(Tip("Kalender.Calendar.CalendarDataSnapshot"), gunler)!;
    }

    private static (Bitmap Resim, Rectangle Alan) Ciz(Size boyut, object anlik, string? foto, string? renk, bool kutu, bool ust)
    {
        object store = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"))!;
        object ayarlar = Oku(store, "Current")!;
        object gorunum = Oku(ayarlar, "Appearance")!;
        if (foto is not null)
        {
            Yaz(gorunum, "BackgroundMode", Enum.ToObject(Tip("Kalender.Settings.BackgroundMode"), 1));
            Yaz(gorunum, "PhotoPath", foto);
        }

        if (renk is not null)
        {
            Yaz(gorunum, "SolidColor", renk);
            Yaz(gorunum, "PresetId", null);
        }

        object yer = Activator.CreateInstance(Tip("Kalender.Settings.MonitorPlacement"))!;
        if (ust)
        {
            // Yatayda varsayılan yer kalır; dikeyde en üste (çizici kenar payında durdurur).
            Yaz(yer, "YRatio", 0.0);
        }

        object cizici = Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"))!;
        object yuzey = cizici.GetType().GetMethod("Render")!.Invoke(cizici,
            new[] { boyut, ayarlar, yer, anlik, Bugun, Bugun, true })!;

        var resim = (Bitmap)Oku(yuzey, "Image")!;
        object duzen = Oku(yuzey, "Layout")!;
        var blok = (RectangleF)Oku(duzen, "Bounds")!;
        var alan = Rectangle.Round(blok);

        if (kutu)
        {
            object hucre = ((IEnumerable)Oku(duzen, "Cells")!).Cast<object>().First(c => (DateOnly)Oku(c, "Date")! == Bugun);
            var hb = Rectangle.Round((RectangleF)Oku(hucre, "Bounds")!);
            object icerik = anlik.GetType().GetMethod("For")!.Invoke(anlik, new object[] { Bugun })!;
            double olcek = boyut.Width / 2560.0;
            Color soluk = (Color)Oku(Tip("Kalender.Rendering.Theme").GetMethod("From")!.Invoke(null, new[] { gorunum })!, "Muted")!;
            using Bitmap k = GunKutusu(icerik, olcek, soluk);

            // Uygulamadaki gibi: hücrenin altında ortalı, gölge payı kadar yukarıda.
            int pay = (int)Math.Round(10 * olcek);
            int x = hb.X + (hb.Width / 2) - (k.Width / 2);
            int y = hb.Bottom - pay;
            using (Graphics g = Graphics.FromImage(resim))
            {
                g.DrawImage(k, x, y, k.Width, k.Height);
            }

            alan = Rectangle.Union(alan, new Rectangle(x, y, k.Width, k.Height));
        }

        return (resim, alan);
    }

    private static void Kaydet(Bitmap b, string yol)
    {
        using var duz = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(duz))
        {
            g.DrawImage(b, 0, 0, b.Width, b.Height);
        }

        if (yol.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
            using var p = new EncoderParameters(1);
            p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
            duz.Save(yol, jpeg, p);
        }
        else
        {
            duz.Save(yol, ImageFormat.Png);
        }
    }

    /// <summary>Uygulamanın gün kutusu penceresinin içeriğini ekrana açmadan resme çevirir.</summary>
    private static Bitmap GunKutusu(object icerik, double olcek, Color soluk)
    {
        var pencere = (System.Windows.Window)Activator.CreateInstance(Tip("Kalender.Ui.DayPopupWindow"))!;
        Yaz(pencere, "MutedColor", soluk);
        Yaz(pencere, "BackgroundOpacity", 95);
        pencere.GetType().GetMethod("BuildLines", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(pencere, new[] { icerik });

        var kutu = (System.Windows.FrameworkElement)pencere.Content;
        pencere.Content = null;
        System.Windows.Documents.TextElement.SetFontFamily(kutu, pencere.FontFamily);
        System.Windows.Media.TextOptions.SetTextFormattingMode(kutu, System.Windows.Media.TextFormattingMode.Display);
        kutu.UseLayoutRounding = true;

        kutu.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        kutu.Arrange(new System.Windows.Rect(kutu.DesiredSize));
        kutu.UpdateLayout();

        // DesiredSize gölge payını (Margin) da içerir.
        int w = (int)Math.Ceiling(kutu.DesiredSize.Width * olcek);
        int h = (int)Math.Ceiling(kutu.DesiredSize.Height * olcek);

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96 * olcek, 96 * olcek,
            System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(kutu);

        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
