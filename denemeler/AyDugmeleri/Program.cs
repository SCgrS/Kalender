using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Threading;

// Kalender / Kalender Lite 2026-09-30 revizyonunun sınaması (yansımayla): ay
// düğmelerinin (önceki ay oku, bu aya dönüş noktası, sonraki ay oku) yeri,
// görünüşü ve tıklama alanları; fare kancasının sol ve sağ tık davranışı
// (yapay olaylarla; kanca takılmaz, ay basınca değişir); 52 piksellik ay adı;
// her ekran için sağ alt varsayılan yer (sağında bir simge sütunu, görev
// çubuğunun biraz üstü); hazır fotoğraflar; zemin renklerinin sırası;
// Kalender'de yakın zamanda gösterilen ayların Google verisinin bellekte tutulması.
//
// Gerçek masaüstüne, duvar kağıdına, ayar dosyasına ya da Google hesabına
// dokunmaz. Hazır fotoğraf klasörünü yalnız denemeden önce yoksa oluşturur ve
// sonunda siler; kullanıcı bir hazır fotoğraf seçmişse o bölümü atlar.
// Çıkış kodu 0 = hepsi geçti. Kullanım: dotnet run -c Release [-- <depo kökü>]
internal static class Program
{
    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static readonly List<string> _dugmeOlaylari = new();
    private static int _hata;
    private static int _gecen;

    [StructLayout(LayoutKind.Sequential)]
    private struct Olay
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

    private static object? Oku(object o, string ad) => o.GetType().GetProperty(ad)!.GetValue(o);

    private static void Yaz(object o, string ad, object? deger) => o.GetType().GetProperty(ad)!.SetValue(o, deger);

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

    private static bool Yakin(double a, double b, double pay = 0.51) => Math.Abs(a - b) <= pay;

    /// <summary>Çalışan dosyanın yukarısında src\Kalender.App'i içeren ilk klasör.</summary>
    private static string DepoKoku()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Kalender.App")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("Depo kökü bulunamadı; yolu bağımsız değişken olarak verin.");
    }

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string depo = args.Length > 0 ? args[0] : DepoKoku();

        try
        {
            Geometri();
            AyAdiVeDugmeler();
            FareKancasi();
            VarsayilanYer();
            HazirFotograflar(depo);
            Renkler();
            GoogleGecmisi();
        }
        catch (Exception ex)
        {
            _hata++;
            Console.WriteLine("BEKLENMEYEN HATA: " + ex);
        }

        Console.WriteLine();
        Console.WriteLine(_hata == 0 ? $"SONUÇ: hepsi geçti ({_gecen} denetim)" : $"SONUÇ: {_hata} denetim kaldı, {_gecen} geçti");
        return _hata == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------ geometri

    private static void Geometri()
    {
        Console.WriteLine("Ay düğmelerinin geometrisi");
        MethodInfo olustur = Tip("Kalender.Calendar.CalendarLayout").GetMethod("Create")!;

        foreach (float s in new[] { 1f, 0.75f, 1.5f, 0.35f })
        {
            object d = olustur.Invoke(null, new object[] { new DateOnly(2026, 9, 1), s, new PointF(100, 200) })!;
            var koken = (PointF)Oku(d, "Origin")!;
            var once = (PointF)Oku(d, "PrevNavCenter")!;
            var nokta = (PointF)Oku(d, "TodayNavCenter")!;
            var sonra = (PointF)Oku(d, "NextNavCenter")!;
            var alan = (RectangleF)Oku(d, "NavBounds")!;
            var baslik = (RectangleF)Oku(d, "TitleBounds")!;
            var blok = (RectangleF)Oku(d, "Bounds")!;
            var gunAdlari = (RectangleF[])Oku(d, "WeekdayCells")!;
            MethodInfo isabet = d.GetType().GetMethod("HitTestNav")!;
            MethodInfo gun = d.GetType().GetMethod("HitTest")!;
            string I(float x, float y) => isabet.Invoke(d, new object[] { new PointF(x, y) })!.ToString()!;

            float cumaCmt = (gunAdlari[4].Right + gunAdlari[5].Left) / 2f;
            float cmtPaz = (gunAdlari[5].Right + gunAdlari[6].Left) / 2f;
            float cumaOrta = gunAdlari[4].X + (gunAdlari[4].Width / 2f);
            float cmtOrta = gunAdlari[5].X + (gunAdlari[5].Width / 2f);
            float pazOrta = gunAdlari[6].X + (gunAdlari[6].Width / 2f);

            Denetle($"ölçek {s}: önceki ay oku Cuma ile Cumartesi'nin ortasında",
                Yakin(once.X, cumaCmt) && Yakin(once.X, (cumaOrta + cmtOrta) / 2f), $"{once.X - koken.X:0.#} px");
            Denetle($"ölçek {s}: sonraki ay oku Cumartesi ile Pazar'ın ortasında",
                Yakin(sonra.X, cmtPaz) && Yakin(sonra.X, (cmtOrta + pazOrta) / 2f), $"{sonra.X - koken.X:0.#} px");
            Denetle($"ölçek {s}: nokta iki okun ortasında, Cumartesi'nin üstünde",
                Yakin(nokta.X, (once.X + sonra.X) / 2f) && Yakin(nokta.X, cmtOrta), $"{nokta.X - koken.X:0.#} px");
            Denetle($"ölçek {s}: oklar ve nokta ay adı satırının ortasında, aynı hizada",
                Yakin(once.Y, baslik.Y + (baslik.Height / 2f)) && once.Y == sonra.Y && once.Y == nokta.Y);
            Denetle($"ölçek {s}: tıklama alanı başlık satırının ve bloğun içinde",
                alan.Top >= baslik.Top && alan.Bottom <= baslik.Bottom && blok.Contains(alan));

            float y = once.Y;
            float solSinir = (once.X + nokta.X) / 2f;
            float sagSinir = (nokta.X + sonra.X) / 2f;
            Denetle($"ölçek {s}: isabetler",
                I(once.X, y) == "Previous" && I(nokta.X, y) == "Today" && I(sonra.X, y) == "Next"
                && I(solSinir - 1, y) == "Previous" && I(solSinir + 1, y) == "Today"
                && I(sagSinir - 1, y) == "Today" && I(sagSinir + 1, y) == "Next"
                && I(alan.Left + 1, y) == "Previous" && I(alan.Left - 1, y) == "None"
                && I(alan.Right - 1, y) == "Next" && I(alan.Right + 1, y) == "None"
                && I(koken.X + (60 * s), y) == "None"
                && I(once.X, baslik.Bottom + 1) == "None"
                && gun.Invoke(d, new object[] { once }) is null);
        }
    }

    /// <summary>
    /// Ay adı 52 px ve düğmelere değmiyor (en uzunları AĞUSTOS, HAZİRAN);
    /// düğmeler zeminsiz; nokta bu ayda soluk, başka ayda yazı renginde.
    /// </summary>
    private static void AyAdiVeDugmeler()
    {
        Console.WriteLine("Ay adı 52 px, düğmeler zeminsiz, nokta bu ayda soluk");
        object store = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"))!;
        object ayarlar = Oku(store, "Current")!;
        object gorunum = Oku(ayarlar, "Appearance")!;
        object cizici = Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"))!;
        MethodInfo blok = cizici.GetType().GetMethod("RenderCalendarBlock")!;
        object yerlesim = Activator.CreateInstance(Tip("Kalender.Settings.MonitorPlacement"))!;
        object bos = Tip("Kalender.Calendar.CalendarDataSnapshot").GetProperty("Empty")!.GetValue(null)!;

        // Başlık satırı y 0-88; önceki ay okunun ucu x ≈ 496'da.
        int enSag = 0;
        string enUzun = "";
        for (int ay = 1; ay <= 12; ay++)
        {
            using var resim = (Bitmap)blok.Invoke(cizici, new object[] { 1f, ayarlar, yerlesim, bos, new DateOnly(2026, ay, 15) })!;
            int sag = 0;
            for (int y = 0; y < 88; y++)
            {
                for (int x = 0; x < 480; x++)
                {
                    if (resim.GetPixel(x, y).A > 40)
                    {
                        sag = Math.Max(sag, x);
                    }
                }
            }

            if (sag > enSag)
            {
                enSag = sag;
                enUzun = ay.ToString();
            }
        }

        Denetle("en uzun ay adı oklardan en az 100 px önce bitiyor", enSag < 396, $"en sağ yazı pikseli {enSag} (ay {enUzun}), ok ~496'da");

        // Düğmeleri doğrudan çizicinin kendisiyle, istenen ay için çiz.
        Type duzenTipi = Tip("Kalender.Calendar.CalendarLayout");
        object tema = Tip("Kalender.Rendering.Theme").GetMethod("From")!.Invoke(null, new[] { gorunum })!;
        object ressam = Activator.CreateInstance(Tip("Kalender.Rendering.CalendarPainter"))!;
        MethodInfo boya = ressam.GetType().GetMethod("Paint")!;
        var bugun = new DateOnly(2026, 9, 30);

        Bitmap Ciz(DateOnly ay, out object duzen)
        {
            duzen = duzenTipi.GetMethod("Create")!.Invoke(null, new object[] { ay, 1f, PointF.Empty })!;
            var resim = new Bitmap(700, 704, PixelFormat.Format32bppArgb);
            using Graphics g = Graphics.FromImage(resim);
            g.Clear(Color.Transparent);
            boya.Invoke(ressam, new[] { g, duzen, tema, gorunum, bos, bugun });
            return resim;
        }

        static bool Ayni(Color a, Color b) => Math.Abs(a.R - b.R) <= 3 && Math.Abs(a.G - b.G) <= 3 && Math.Abs(a.B - b.B) <= 3 && a.A > 240;

        var yazi = (Color)Oku(tema, "Text")!;
        var soluk = (Color)Oku(tema, "Muted")!;

        using (Bitmap eylulResmi = Ciz(new DateOnly(2026, 9, 1), out object eylulDuzeni))
        using (Bitmap ekimResmi = Ciz(new DateOnly(2026, 10, 1), out object ekimDuzeni))
        {
            var nokta = (PointF)Oku(eylulDuzeni, "TodayNavCenter")!;
            var once = (PointF)Oku(eylulDuzeni, "PrevNavCenter")!;
            var sonra = (PointF)Oku(eylulDuzeni, "NextNavCenter")!;
            Color eylulNokta = eylulResmi.GetPixel((int)nokta.X, (int)nokta.Y);
            Color ekimNokta = ekimResmi.GetPixel((int)nokta.X, (int)nokta.Y);

            Denetle("nokta bu ayda soluk renkte", Ayni(eylulNokta, soluk), $"{eylulNokta} / soluk {soluk}");
            Denetle("nokta başka ayda yazı renginde", Ayni(ekimNokta, yazi), $"{ekimNokta} / yazı {yazi}");

            // Okların uçları dolu; ok ile nokta arası ve kapsülün eski yeri boş (zemin yok).
            Color solUc = eylulResmi.GetPixel((int)Math.Round(once.X - 3.5), (int)once.Y);
            Color sagUc = eylulResmi.GetPixel((int)Math.Round(sonra.X + 3.5), (int)sonra.Y);
            bool araBos = eylulResmi.GetPixel((int)((once.X + nokta.X) / 2), (int)once.Y).A == 0
                && eylulResmi.GetPixel((int)((nokta.X + sonra.X) / 2), (int)once.Y).A == 0
                && eylulResmi.GetPixel((int)once.X - 20, (int)once.Y).A == 0
                && eylulResmi.GetPixel((int)sonra.X + 20, (int)once.Y).A == 0;
            Denetle("oklar yazı renginde, aralarında zemin yok", Ayni(solUc, yazi) && Ayni(sagUc, yazi) && araBos,
                $"sol uç {solUc}, sağ uç {sagUc}");

            // Oklar küçük: boyanan yükseklikleri 16-22 px.
            int ust = int.MaxValue, alt = 0;
            for (int y = 0; y < 88; y++)
            {
                for (int x = (int)once.X - 8; x <= (int)once.X + 8; x++)
                {
                    if (eylulResmi.GetPixel(x, y).A > 128)
                    {
                        ust = Math.Min(ust, y);
                        alt = Math.Max(alt, y);
                    }
                }
            }

            Denetle("ok yüksekliği 16-22 px (önceki kapsüldeki oklardan küçük)", alt - ust + 1 is >= 16 and <= 22, $"{alt - ust + 1} px");
        }

        // Ay adının yüksekliği: 52 px Segoe UI Bold büyük harfin boyanan yüksekliği ~37 px.
        using var eylul = (Bitmap)blok.Invoke(cizici, new object[] { 1f, ayarlar, yerlesim, bos, new DateOnly(2026, 9, 15) })!;

        // "EYLÜL"deki Ü noktaları büyük harfin üstüne çıkıyor; E'nin yüksekliğine bakılır.
        int eUst = int.MaxValue, eAlt = 0;
        for (int y = 0; y < 88; y++)
        {
            for (int x = 20; x < 70; x++)
            {
                if (eylul.GetPixel(x, y).A > 128)
                {
                    eUst = Math.Min(eUst, y);
                    eAlt = Math.Max(eAlt, y);
                }
            }
        }

        int yukseklik = eAlt - eUst + 1;
        Denetle("ay adı 52 px (E harfi ~37 px yüksek)", yukseklik >= 35 && yukseklik <= 39, $"E yüksekliği {yukseklik} px");
        Denetle("ay adı satırın ortasında", Yakin((eUst + eAlt) / 2.0, 44, 2), $"E ortası {(eUst + eAlt) / 2.0}");
    }

    // ---------------------------------------------------------- fare kancası

    private static void FareKancasi()
    {
        Console.WriteLine("Fare kancası (yapay olaylarla; kanca takılmaz)");
        Type t = Tip("Kalender.Desktop.CalendarMouseHook");

        // Takvim x<1000; önceki ay düğmesi 500-532, nokta 533-565, sonraki 566-599.
        // Simgenin altında kalan düğme için karar işlevi zaten "yok" döner (AppHost).
        Type dugmeTipi = Tip("Kalender.Calendar.NavButton");
        Func<Point, bool> takvimde = p => p.X < 1000;
        Delegate dugme = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(Point), dugmeTipi),
            typeof(Program).GetMethod(nameof(DugmeBul), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(dugmeTipi));
        object kanca = Activator.CreateInstance(t, Dispatcher.CurrentDispatcher, takvimde, dugme)!;

        _dugmeOlaylari.Clear();
        var aylar = _dugmeOlaylari;
        var menuler = new List<Point>();
        t.GetEvent("NavClicked")!.AddEventHandler(kanca, Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(dugmeTipi),
            typeof(Program).GetMethod(nameof(DugmeKaydet), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(dugmeTipi)));
        t.GetEvent("RightClicked")!.AddEventHandler(kanca, new Action<Point>(menuler.Add));
        MethodInfo fare = t.GetMethod("OnMouse", BindingFlags.NonPublic | BindingFlags.Instance)!;

        IntPtr Gonder(uint ileti, int x)
        {
            IntPtr bellek = Marshal.AllocHGlobal(Marshal.SizeOf<Olay>());
            try
            {
                Marshal.StructureToPtr(new Olay { X = x, Y = 10 }, bellek, false);
                return (IntPtr)fare.Invoke(kanca, new object[] { 0, (IntPtr)ileti, bellek })!;
            }
            finally
            {
                Marshal.FreeHGlobal(bellek);
            }
        }

        void Bosalt() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

        const uint LDown = 0x0201, LUp = 0x0202, RDown = 0x0204, RUp = 0x0205;

        bool yut1 = Gonder(LDown, 520) == (IntPtr)1;
        Bosalt();
        bool basincaDegisti = aylar.SequenceEqual(new[] { "Previous" });
        bool yut2 = Gonder(LUp, 520) == (IntPtr)1;
        Bosalt();
        Denetle("önceki ay: tuşa basınca (bırakmadan) ay değişiyor, basış ve bırakış saklanıyor",
            yut1 && basincaDegisti && yut2 && aylar.SequenceEqual(new[] { "Previous" }), string.Join(",", aylar));

        yut1 = Gonder(LDown, 550) == (IntPtr)1;
        yut2 = Gonder(LUp, 700) == (IntPtr)1;
        Bosalt();
        Denetle("nokta: bu aya dönüş; bırakırken kayılsa da bırakış saklanıyor",
            yut1 && yut2 && aylar.SequenceEqual(new[] { "Previous", "Today" }), string.Join(",", aylar));

        yut1 = Gonder(LDown, 580) == (IntPtr)1;
        yut2 = Gonder(LUp, 580) == (IntPtr)1;
        Bosalt();
        Denetle("sonraki ay", yut1 && yut2 && aylar.SequenceEqual(new[] { "Previous", "Today", "Next" }), string.Join(",", aylar));

        yut1 = Gonder(LDown, 520) == (IntPtr)1;
        bool ikinci = Gonder(LUp, 520) == (IntPtr)1 && Gonder(LDown, 520) == (IntPtr)1 && Gonder(LUp, 520) == (IntPtr)1;
        Bosalt();
        Denetle("hızlı iki tık iki ay geri götürüyor", yut1 && ikinci && aylar.Count(a => a == "Previous") == 3);

        bool gecti1 = Gonder(LDown, 700) != (IntPtr)1, gecti2 = Gonder(LUp, 700) != (IntPtr)1;
        Bosalt();
        Denetle("takvimde ama düğme dışında (ya da simgede) sol tık masaüstüne geçiyor", gecti1 && gecti2 && aylar.Count == 5);

        gecti1 = Gonder(LDown, 700) != (IntPtr)1;
        gecti2 = Gonder(LUp, 520) != (IntPtr)1;
        Bosalt();
        Denetle("düğme dışında basılıp düğmede bırakılan tık da masaüstüne geçiyor", gecti1 && gecti2 && aylar.Count == 5);

        bool yutR1 = Gonder(RDown, 100) == (IntPtr)1, yutR2 = Gonder(RUp, 100) == (IntPtr)1;
        Bosalt();
        Denetle("takvimde sağ tık eskisi gibi menü açıyor", yutR1 && yutR2 && menuler.Count == 1 && aylar.Count == 5);

        gecti1 = Gonder(RDown, 1500) != (IntPtr)1;
        gecti2 = Gonder(RUp, 1500) != (IntPtr)1;
        Bosalt();
        Denetle("takvim dışında (ya da simgede) sağ tık masaüstüne geçiyor", gecti1 && gecti2 && menuler.Count == 1);

        yutR1 = Gonder(RDown, 520) == (IntPtr)1;
        yutR2 = Gonder(RUp, 520) == (IntPtr)1;
        Bosalt();
        Denetle("ay düğmesine sağ tık menü açıyor, ay değiştirmiyor", yutR1 && yutR2 && menuler.Count == 2 && aylar.Count == 5);
    }

    private static T DugmeBul<T>(Point p) where T : struct, Enum
        => Enum.Parse<T>(p.X switch
        {
            >= 500 and < 533 => "Previous",
            >= 533 and < 566 => "Today",
            >= 566 and < 600 => "Next",
            _ => "None",
        });

    private static void DugmeKaydet<T>(T dugme) => _dugmeOlaylari.Add(dugme!.ToString()!);

    // -------------------------------------------------------- varsayılan yer

    private static object Ekran(int x, int y, int w, int h, Rectangle? calisma, uint dpi, bool ana)
    {
        object m = Activator.CreateInstance(Tip("Kalender.Interop.MonitorTarget"))!;
        Yaz(m, "Handle", IntPtr.Zero);
        Yaz(m, "DeviceName", @"\\.\DISPLAY9");
        Yaz(m, "StableId", "deneme-" + w + "x" + h + "@" + x);
        Yaz(m, "Bounds", new Rectangle(x, y, w, h));
        Yaz(m, "WorkArea", calisma ?? new Rectangle(x, y, w, h));
        Yaz(m, "Dpi", dpi);
        Yaz(m, "IsPrimary", ana);
        return m;
    }

    private static void VarsayilanYer()
    {
        Console.WriteLine("Varsayılan yer (sağında bir simge sütunu, görev çubuğunun biraz üstü)");
        MethodInfo varsayilan = Tip("Kalender.Settings.PlacementDefaults").GetMethod("For")!;
        MethodInfo sutun = Tip("Kalender.Desktop.MasaustuSimgeIzgarasi").GetMethod("SutunGenisligi")!;
        MethodInfo koken = Tip("Kalender.Rendering.DesktopSurfaceRenderer").GetMethod("ResolveOrigin")!;
        MethodInfo olcek = Tip("Kalender.Calendar.CalendarScale").GetMethod("For")!;
        MethodInfo boyut = Tip("Kalender.Calendar.CalendarLayout").GetMethod("MeasureSize")!;
        int izgara = (int)Tip("Kalender.Desktop.MasaustuSimgeIzgarasi").GetProperty("Baslangic")!.GetValue(null)!;

        var durumlar = new (string Ad, object Ekran, int? BeklenenX, int? BeklenenY)[]
        {
            ("2560×1440 %100, görev çubuğu 48 (bu bilgisayar)", Ekran(0, 0, 2560, 1440, new Rectangle(0, 0, 2560, 1392), 96, true), 1775, 650),
            ("1920×1080 %100, görev çubuğu 48", Ekran(0, 0, 1920, 1080, new Rectangle(0, 0, 1920, 1032), 96, true), 1275, 476),
            ("ikinci ekran 1920×1080 sağda, görev çubuğu yok", Ekran(2560, 0, 1920, 1080, null, 96, false), null, null),
            ("3840×2160 %150, görev çubuğu 72", Ekran(0, 0, 3840, 2160, new Rectangle(0, 0, 3840, 2088), 144, true), null, null),
            ("1366×768 %100, görev çubuğu 48", Ekran(0, 0, 1366, 768, new Rectangle(0, 0, 1366, 720), 96, true), null, null),
            ("2560×1440, görev çubuğu sağda (80 px)", Ekran(0, 0, 2560, 1440, new Rectangle(0, 0, 2480, 1440), 96, true), null, null),
            ("1920×1200 %125 (16:10)", Ekran(0, 0, 1920, 1200, new Rectangle(0, 0, 1920, 1140), 120, true), null, null),
        };

        foreach ((string ad, object ekran, int? bx, int? by) in durumlar)
        {
            var sinir = (Rectangle)Oku(ekran, "Bounds")!;
            var calisma = (Rectangle)Oku(ekran, "WorkArea")!;
            uint dpi = (uint)Oku(ekran, "Dpi")!;
            object yer = varsayilan.Invoke(null, new[] { ekran })!;
            float s = (float)olcek.Invoke(null, new object[] { sinir.Size, (int)Oku(yer, "ScalePercent")! })!;
            var blok = (SizeF)boyut.Invoke(null, new object[] { s })!;
            var sol = (PointF)koken.Invoke(null, new object[] { sinir.Size, s, yer })!;

            float sag = sinir.X + sol.X + blok.Width;
            float alt = sinir.Y + sol.Y + blok.Height;
            int kolon = (int)sutun.Invoke(null, new object[] { dpi })!;
            float sagBosluk = calisma.Right - sag;
            float altBosluk = calisma.Bottom - alt;

            // Sağdaki tam sütun: ızgara sanal masaüstünün solundan başlıyor.
            int tamSutun = (calisma.Right - izgara) / kolon;
            float sonSutunBasi = izgara + ((tamSutun - 1) * kolon);

            bool icinde = sinir.X + sol.X >= calisma.Left && sinir.Y + sol.Y >= calisma.Top && sag <= calisma.Right && alt <= calisma.Bottom;
            bool klasorSigar = sag <= sonSutunBasi + 0.5f && sagBosluk >= kolon;
            bool altBoslukDogru = Yakin(altBosluk, 38 * s, 1);
            bool beklenen = (bx is null || Yakin(sol.X, bx.Value, 1)) && (by is null || Yakin(sol.Y, by.Value, 1));

            Denetle(ad, icinde && klasorSigar && altBoslukDogru && beklenen,
                $"blok ({sol.X:0.#}, {sol.Y:0.#}) {blok.Width:0}×{blok.Height:0}; sağda {sagBosluk:0.#} px (sütun {kolon}), görev çubuğuna {altBosluk:0.#} px");
        }
    }

    // ------------------------------------------------------ hazır fotoğraflar

    private static void HazirFotograflar(string depo)
    {
        Console.WriteLine("Hazır fotoğraflar");
        Type t = Tip("Kalender.Settings.HazirFotograflar");
        var hepsi = ((System.Collections.IEnumerable)t.GetProperty("All")!.GetValue(null)!).Cast<object>().ToList();
        string klasor = (string)t.GetProperty("Folder")!.GetValue(null)!;
        bool klasorVardi = Directory.Exists(klasor);

        Denetle("dört fotoğraf var", hepsi.Count == 4);

        MethodInfo kucuk = t.GetMethod("OpenThumbnail")!;
        MethodInfo hazirla = t.GetMethod("Prepare")!;
        MethodInfo bul = t.GetMethod("Find")!;
        MethodInfo yolu = t.GetMethod("PathOf")!;

        foreach (object f in hepsi)
        {
            string id = (string)Oku(f, "Id")!;
            string kaynak = Path.Combine(depo, @"src\Kalender.App\Assets\DuvarKagitlari", id + ".jpg");
            using (var s = (Stream?)kucuk.Invoke(null, new[] { f }))
            {
                Denetle($"{id}: küçük resim gömülü", s is not null && s.Length > 1000);
            }

            if (klasorVardi)
            {
                continue;
            }

            string? yol = (string?)hazirla.Invoke(null, new[] { f });
            bool ayni = yol is not null && File.Exists(yol)
                && SHA256.HashData(File.ReadAllBytes(yol)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(kaynak)));
            Denetle($"{id}: çıkarılan dosya depodakiyle aynı", ayni, yol ?? "çıkarılamadı");

            var resim = Image.FromFile(yol!);
            Denetle($"{id}: 3840 × 2160", resim.Width == 3840 && resim.Height == 2160);
            resim.Dispose();

            Denetle($"{id}: yol tanınıyor (büyük harfle de)",
                Equals(bul.Invoke(null, new object?[] { yol }), f)
                && Equals(bul.Invoke(null, new object?[] { yol!.ToUpperInvariant() }), f)
                && bul.Invoke(null, new object?[] { @"C:\baska\" + id + ".jpg" }) is null
                && bul.Invoke(null, new object?[] { null }) is null);
        }

        if (klasorVardi)
        {
            Console.WriteLine("   bilgi: klasör zaten vardı (" + klasor + "); çıkarma denemesi atlandı, dokunulmadı.");
            return;
        }

        // Silinen hazır fotoğraf açılışta geri geliyor.
        object ilk = hepsi[0];
        string ilkYol = (string)yolu.Invoke(null, new[] { ilk })!;
        File.Delete(ilkYol);
        object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"))!;
        Yaz(Oku(ayarlar, "Appearance")!, "PhotoPath", ilkYol);
        t.GetMethod("RestoreMissing")!.Invoke(null, new[] { ayarlar });
        Denetle("silinen hazır fotoğraf yeniden çıkarılıyor", File.Exists(ilkYol));

        Directory.Delete(klasor, recursive: true);
        Denetle("deneme klasörü temizlendi", !Directory.Exists(klasor));
    }

    // --------------------------------------------------------------- renkler

    private static void Renkler()
    {
        Console.WriteLine("Hazır zemin renkleri");
        var hepsi = ((System.Collections.IEnumerable)Tip("Kalender.Settings.ThemePresets").GetProperty("All")!.GetValue(null)!)
            .Cast<object>().ToList();
        string[] adlar = hepsi.Select(p => (string)Oku(p, "Name")!).ToArray();
        string[] beklenen = { "Siyah", "Kuzgun", "Mürekkep", "Gece Mavisi", "Kömür", "Kum Taşı", "Kum", "Kâğıt" };
        Denetle("sıra: siyah en solda, Kum Taşı Kömür ile Kum arasında", adlar.SequenceEqual(beklenen), string.Join(", ", adlar));

        MethodInfo parlaklik = Tip("Kalender.Rendering.ColorTools").GetMethod("RelativeLuminance")!;
        MethodInfo coz = Tip("Kalender.Rendering.ColorTools").GetMethod("Parse")!;
        double[] l = hepsi.Select(p => (double)parlaklik.Invoke(null, new[] { coz.Invoke(null, new object[] { (string)Oku(p, "Color")!, Color.Gray }) })!).ToArray();
        Denetle("koyudan açığa sıralı", l.Zip(l.Skip(1)).All(z => z.First < z.Second), string.Join(" < ", l.Select(v => v.ToString("0.000"))));

        string[] cikan = { "cam-yesili", "tarcin", "sis-grisi" };
        Denetle("yeşil, kahve ve gri çıktı", hepsi.All(p => !cikan.Contains((string)Oku(p, "Id")!)));

        object yer = Activator.CreateInstance(Tip("Kalender.Settings.MonitorPlacement"))!;
        Denetle("yedek varsayılan oranlar yeni yere göre", Yakin((double)Oku(yer, "XRatio")!, 0.6934, 0.0001) && Yakin((double)Oku(yer, "YRatio")!, 0.4514, 0.0001));
    }

    // ---------------------------------------------------- Google ay önbelleği

    /// <summary>
    /// Kalender: daha önce gösterilen ayların Google verisi bellekte tutuluyor.
    /// Sağlayıcı ağsız, kimliksiz kurulur (kurucusu çalıştırılmaz); veri sahte.
    /// Diskteki Google önbelleğine dokunan ClearCache çağrılmaz.
    /// </summary>
    private static void GoogleGecmisi()
    {
        Console.WriteLine("Google: yakın zamanda gösterilen aylar bellekte");
        Type? t = Uyg.GetType("Kalender.Google.GoogleCalendarProvider");
        if (t is null)
        {
            Console.WriteLine("   bilgi: Kalender Lite'ta Google yok; atlandı.");
            return;
        }

        Type paketTipi = Tip("Kalender.Google.ProviderBundle");
        Type takvimTipi = Tip("Kalender.Google.ProviderCalendar");
        object saglayici = RuntimeHelpers.GetUninitializedObject(t);
        BindingFlags ic = BindingFlags.NonPublic | BindingFlags.Instance;
        t.GetField("_gecmis", ic)!.SetValue(saglayici, Activator.CreateInstance(typeof(List<>).MakeGenericType(paketTipi)));
        t.GetField("_veriKilidi", ic)!.SetValue(saglayici, new object());
        PropertyInfo veri = t.GetProperty("Data")!;
        var gecmis = (IList)t.GetField("_gecmis", ic)!.GetValue(saglayici)!;
        MethodInfo getir = t.GetMethod("GecmistenGetir")!;
        MethodInfo gecmise = t.GetMethod("Gecmise", ic)!;

        object Paket(int ay)
        {
            object p = Activator.CreateInstance(paketTipi)!;
            (DateOnly ilk, DateOnly son) = ((DateOnly, DateOnly))Tip("Kalender.Calendar.CalendarLayout").GetMethod("GridRange")!
                .Invoke(null, new object[] { new DateOnly(2026, ay, 1) })!;
            Yaz(p, "RangeFrom", ilk);
            Yaz(p, "RangeTo", son);
            ((IList)Oku(p, "Calendars")!).Add(Activator.CreateInstance(takvimTipi));
            return p;
        }

        bool Getir(object p) => (bool)getir.Invoke(saglayici, new object[] { (DateOnly)Oku(p, "RangeFrom")!, (DateOnly)Oku(p, "RangeTo")! })!;

        object eylul = Paket(9), ekim = Paket(10), kasim = Paket(11);
        veri.SetValue(saglayici, eylul);

        Denetle("görülmemiş ay bellekte yok; veri değişmiyor", !Getir(ekim) && ReferenceEquals(veri.GetValue(saglayici), eylul));

        gecmise.Invoke(saglayici, new[] { ekim });
        Denetle("görülmüş ay bellekten geliyor, önceki ay belleğe geçiyor",
            Getir(ekim) && ReferenceEquals(veri.GetValue(saglayici), ekim) && gecmis.Contains(eylul) && !gecmis.Contains(ekim));

        Denetle("bu aya dönüş de bellekten", Getir(eylul) && ReferenceEquals(veri.GetValue(saglayici), eylul) && gecmis.Contains(ekim));

        Denetle("gösterilen ay yeniden istenince değişiklik yok", Getir(eylul) && ReferenceEquals(veri.GetValue(saglayici), eylul));

        for (int ay = 1; ay <= 6; ay++)
        {
            gecmise.Invoke(saglayici, new[] { Paket(ay) });
        }

        Denetle("bellekte en çok 3 ay tutuluyor, en eskisi düşüyor", gecmis.Count == 3 && !Getir(ekim) && Getir(kasim) is false);

        object bos = Activator.CreateInstance(paketTipi)!;
        int once = gecmis.Count;
        gecmise.Invoke(saglayici, new[] { bos });
        Denetle("boş veri belleğe alınmıyor", gecmis.Count == once);
    }
}
