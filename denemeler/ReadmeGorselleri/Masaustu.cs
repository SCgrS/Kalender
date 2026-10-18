using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

/// <summary>
/// README görüntüsünün masaüstü olduğu anlaşılsın diye takvim görüntüsünün üstüne
/// masaüstü simgeleri ve Windows 11 görev çubuğu çizer. Simgeler Windows'un kendi
/// dosyalarından (imageres.dll, explorer.exe, tarayıcılar) okunur; yalnız görüntü içindir.
/// </summary>
internal static class Masaustu
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIconW(string dosya, int sira, uint bayrak, out IntPtr buyuk, out IntPtr kucuk, uint boyut);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr h);

    private const string Imageres = @"C:\Windows\System32\imageres.dll";

    private static Bitmap? Simge(string dosya, int sira, int boyut)
    {
        if (!File.Exists(dosya))
        {
            return null;
        }

        // Bazı programların dosyasında büyük boy yok (Firefox): daha küçüğünü dene.
        IntPtr h = IntPtr.Zero;
        IntPtr k = IntPtr.Zero;
        foreach (int b in new[] { boyut, 128, 64, 48 })
        {
            if (SHDefExtractIconW(dosya, sira, 0, out h, out k, (uint)b) == 0 && h != IntPtr.Zero)
            {
                break;
            }
        }

        if (h == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            using var ic = Icon.FromHandle(h);
            return ic.ToBitmap();
        }
        finally
        {
            DestroyIcon(h);
            if (k != IntPtr.Zero)
            {
                DestroyIcon(k);
            }
        }
    }

    public static void Ciz(Bitmap resim, double s, Rectangle takvim, bool ortada, bool acik)
    {
        using Graphics g = Graphics.FromImage(resim);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        int seritYuksekligi = (int)(48 * s);
        Simgeler(g, resim.Size, s, takvim);
        if (ortada)
        {
            Ortada(g, resim.Size, s, acik);
        }
        else
        {
            Serit(g, resim.Size, s, acik, seritYuksekligi);
        }
    }

    // Windows 11 masaüstü ızgarası (orta boy simge, %100'de): hücre 76×100, simge 48.
    private static void Simgeler(Graphics g, Size ekran, double s, Rectangle takvim)
    {
        float hw = (float)(76 * s);
        float hh = (float)(100 * s);
        float pay = (float)(4 * s);
        int simgeBoyu = (int)(48 * s);

        using Bitmap? klasor = Simge(Imageres, 3, 256);
        using Bitmap? cop = Simge(Imageres, 50, 256);

        var sol = new (Bitmap? Resim, string Ad)[]
        {
            (cop, "Geri Dönüşüm Kutusu"),
            (klasor, "Dosyalar"),
            (klasor, "Taslaklar"),
            (klasor, "Arşiv"),
        };

        for (int i = 0; i < sol.Length; i++)
        {
            Tek(g, sol[i].Resim, sol[i].Ad, pay, pay + (i * hh), hw, simgeBoyu, s);
        }

        // Takvimin sağında bir simge sütunu boş kalır (Kalender'in varsayılan yeri): oraya bir klasör.
        int sutun = (int)Math.Floor((ekran.Width - pay) / hw) - 1;
        float x = pay + (sutun * hw);
        if (x >= takvim.Right)
        {
            int satir = (int)Math.Floor((takvim.Bottom - pay - hh) / hh);
            Tek(g, klasor, "Kısayollar", x, pay + (satir * hh), hw, simgeBoyu, s);
        }
    }

    private static void Tek(Graphics g, Bitmap? resim, string ad, float x, float y, float hw, int boy, double s)
    {
        if (resim is not null)
        {
            g.DrawImage(resim, x + ((hw - boy) / 2f), y + (float)(4 * s), boy, boy);
        }

        using var yazi = new Font("Segoe UI", (float)(9 * s), GraphicsUnit.Point);
        using var bicim = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord };
        var kutu = new RectangleF(x + (float)(2 * s), y + boy + (float)(8 * s), hw - (float)(4 * s), (float)(34 * s));
        using var golge = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        var gk = kutu;
        gk.Offset((float)(1 * s), (float)(1 * s));
        g.DrawString(ad, yazi, golge, gk, bicim);
        g.DrawString(ad, yazi, Brushes.White, kutu, bicim);
    }

    private static List<Bitmap?> Uygulamalar(int boy, bool kullanici) => kullanici
        ? new()
        {
            Simge(@"C:\Program Files\Google\Chrome\Application\chrome.exe", 0, boy),
            Simge(SpotifyYolu(), 0, boy),
        }
        : new()
        {
            Simge(@"C:\Windows\explorer.exe", 0, boy),
            Simge(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", 0, boy),
            Simge(@"C:\Program Files\Google\Chrome\Application\chrome.exe", 0, boy),
        };

    /// <summary>
    /// Spotify'ın exe'si: Microsoft Store paketi (WindowsApps klasörü listelenemediği için yolu
    /// Get-AppxPackage'dan) ya da sitesinden kurulan sürüm. Bulunamazsa simge çizilmez.
    /// </summary>
    private static string SpotifyYolu()
    {
        try
        {
            var bilgi = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                "-NoProfile -Command \"(Get-AppxPackage SpotifyAB.SpotifyMusic).InstallLocation\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var surec = System.Diagnostics.Process.Start(bilgi)!;
            string klasor = surec.StandardOutput.ReadToEnd().Trim();
            surec.WaitForExit();
            if (klasor.Length > 0 && File.Exists(Path.Combine(klasor, "Spotify.exe")))
            {
                return Path.Combine(klasor, "Spotify.exe");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe");
    }

    private static Color Zemin(bool acik) => acik ? Color.FromArgb(238, 243, 243, 243) : Color.FromArgb(238, 32, 32, 32);

    private static Color Yazi(bool acik) => acik ? Color.FromArgb(20, 20, 20) : Color.White;

    /// <summary>
    /// Tam şerit, kullanıcının kendi görev çubuğu gibi: simgeler solda, arama düğmesi yok,
    /// sabitli uygulamalar Chrome ve Spotify (ikisi de açık), saat sağda.
    /// </summary>
    private static void Serit(Graphics g, Size ekran, double s, bool acik, int yukseklik)
    {
        var alan = new RectangleF(0, ekran.Height - yukseklik, ekran.Width, yukseklik);
        using (var b = new SolidBrush(Zemin(acik)))
        {
            g.FillRectangle(b, alan);
        }

        using (var p = new Pen(acik ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255), (float)s))
        {
            g.DrawLine(p, 0, alan.Top, ekran.Width, alan.Top);
        }

        float dugme = (float)(44 * s);
        Dugmeler(g, (float)(4 * s), alan.Top + ((yukseklik - dugme) / 2f), dugme, s, acik, kullanici: true);
        Tepsi(g, ekran.Width - (float)(12 * s), alan.Top + (yukseklik / 2f), s, acik, pil: false);
    }

    /// <summary>Ortada: yalnız simgeleri ve saati saran, alttan biraz yukarıda yuvarlak köşeli bir şerit.</summary>
    private static void Ortada(Graphics g, Size ekran, double s, bool acik)
    {
        float dugme = (float)(44 * s);
        float ic = (float)(8 * s);
        float tepsiGenisligi = (float)(205 * s);
        float genislik = (5 * dugme) + tepsiGenisligi + (3 * ic);
        float yukseklik = (float)(52 * s);
        var alan = new RectangleF((ekran.Width - genislik) / 2f, ekran.Height - yukseklik - (float)(10 * s), genislik, yukseklik);

        using (var yol = Yuvarlak(alan, (float)(12 * s)))
        {
            using var b = new SolidBrush(Zemin(acik));
            g.FillPath(b, yol);
            using var p = new Pen(acik ? Color.FromArgb(35, 0, 0, 0) : Color.FromArgb(45, 255, 255, 255), (float)s);
            g.DrawPath(p, yol);
        }

        Dugmeler(g, alan.Left + ic, alan.Top + ((yukseklik - dugme) / 2f), dugme, s, acik, kullanici: false);
        Tepsi(g, alan.Right - ic - (float)(4 * s), alan.Top + (yukseklik / 2f), s, acik, pil: true);
    }

    /// <summary>
    /// Başlat, (kullanıcının düzeninde değilse) Ara ve uygulamalar. Açık uygulamanın altında
    /// çizgi: kullanıcının düzeninde Chrome ve Spotify, öbüründe Dosya Gezgini.
    /// </summary>
    private static void Dugmeler(Graphics g, float x, float y, float dugme, double s, bool acik, bool kullanici)
    {
        float simge = (float)(24 * s);
        float ust = y + ((dugme - simge) / 2f);

        // Başlat: dört mavi kare.
        float kare = simge * 0.47f;
        float bosluk = simge - (2 * kare);
        float bx = x + ((dugme - simge) / 2f);
        using (var mavi = new LinearGradientBrush(new RectangleF(bx, ust, simge, simge),
                   Color.FromArgb(0x00, 0xB4, 0xFF), Color.FromArgb(0x00, 0x67, 0xD6), 45f))
        {
            g.FillRectangle(mavi, bx, ust, kare, kare);
            g.FillRectangle(mavi, bx + kare + bosluk, ust, kare, kare);
            g.FillRectangle(mavi, bx, ust + kare + bosluk, kare, kare);
            g.FillRectangle(mavi, bx + kare + bosluk, ust + kare + bosluk, kare, kare);
        }

        x += dugme;

        // Ara: büyüteç.
        if (!kullanici)
        {
        using (var p = new Pen(Yazi(acik), (float)(2 * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float r = simge * 0.62f;
            float cx = x + ((dugme - simge) / 2f);
            g.DrawEllipse(p, cx + (simge * 0.06f), ust + (simge * 0.06f), r, r);
            g.DrawLine(p, cx + (simge * 0.06f) + (r * 0.85f), ust + (simge * 0.06f) + (r * 0.85f), cx + (simge * 0.92f), ust + (simge * 0.92f));
        }

        x += dugme;
        }

        List<Bitmap?> uyg = Uygulamalar(256, kullanici);
        int acikSon = kullanici ? 1 : 0;
        for (int i = 0; i < uyg.Count; i++)
        {
            if (uyg[i] is { } b)
            {
                g.DrawImage(b, x + ((dugme - simge) / 2f), ust, simge, simge);
                b.Dispose();
            }

            if (i <= acikSon)
            {
                using var vurgu = new SolidBrush(acik ? Color.FromArgb(120, 0, 0, 0) : Color.FromArgb(150, 255, 255, 255));
                float w = (float)(16 * s);
                using var yol = Yuvarlak(new RectangleF(x + ((dugme - w) / 2f), y + dugme - (float)(3 * s), w, (float)(3 * s)), (float)(1.5 * s));
                g.FillPath(vurgu, yol);
            }

            x += dugme;
        }
    }

    /// <summary>Sağ uçtaki ^, ağ, ses, pil ve iki satırlık saat; sağ kenar verilen x.</summary>
    private static void Tepsi(Graphics g, float sag, float orta, double s, bool acik, bool pil)
    {
        using var firca = new SolidBrush(Yazi(acik));
        using var saat = new Font("Segoe UI", (float)(9 * s), GraphicsUnit.Point);
        using var sagaYasli = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        string metin = "14:30\n14.10.2026";
        SizeF olcu = g.MeasureString(metin, saat);
        g.DrawString(metin, saat, firca, new RectangleF(sag - olcu.Width, orta - (olcu.Height / 2f), olcu.Width, olcu.Height), sagaYasli);

        string ikonYazisi = "Segoe Fluent Icons";
        using var ikon = new Font(ikonYazisi, (float)(10.5 * s), GraphicsUnit.Point);
        if (!string.Equals(ikon.Name, ikonYazisi, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var ortaliBicim = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        float x = sag - olcu.Width - (float)(14 * s);
        string[] glifler = pil
            ? new[] { "\uE83F", "\uE767", "\uE701", "\uE70E" }
            : new[] { "\uE767", "\uE701", "\uE70E" };
        foreach (string glif in glifler)
        {
            float w = (float)(26 * s);
            g.DrawString(glif, ikon, firca, new RectangleF(x - w, orta - w / 2f, w, w), ortaliBicim);
            x -= w + (float)(4 * s);
        }
    }

    private static GraphicsPath Yuvarlak(RectangleF r, float yaricap)
    {
        float d = yaricap * 2;
        var yol = new GraphicsPath();
        yol.AddArc(r.Left, r.Top, d, d, 180, 90);
        yol.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        yol.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        yol.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        yol.CloseFigure();
        return yol;
    }
}
