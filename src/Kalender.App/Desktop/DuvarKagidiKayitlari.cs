using System.Collections;
using System.Drawing;
using System.IO;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Windows'un duvar kâğıdı listesindeki bir kaydın durumu; kaydın dikdörtgenini
/// soran çağrının (GetMonitorRECT) sonucuna göre.
/// </summary>
internal enum KayitDurumu
{
    /// <summary>Bağlı bir ekranın kaydı: dikdörtgeni var.</summary>
    Canli,

    /// <summary>
    /// Windows kaydı tanıyor ama ekran bağlı değil (S_FALSE ya da boş
    /// dikdörtgen). Dizüstünden çıkarılmış haricî ekran gibi olağan bir durum;
    /// liste bozuk sayılmaz.
    /// </summary>
    BagliDegil,

    /// <summary>
    /// Dikdörtgen sorusu hata veriyor: Explorer'ın listesinde kalmış, hiçbir
    /// ekrana karşılık gelmeyen kayıt. Ona yazılan görüntü hatasız kabul ediliyor
    /// ama hiçbir ekrana çıkmıyor (2026-10-17, ev bilgisayarında görüldü).
    /// Yalnız Explorer'ın kendi cevabı sayılır: soru Explorer'a ulaşamadıysa
    /// liste bilinmiyor (bkz. <see cref="DuvarKagidiKayitlari.UlasilamadiMi"/>).
    /// </summary>
    Hayalet,
}

/// <summary>Windows'un duvar kâğıdı listesinin bir kaydı.</summary>
/// <param name="Sira">Listedeki yeri.</param>
/// <param name="Kimlik">Aygıt yolu; Windows boş dize de verebiliyor.</param>
/// <param name="Durum">Kaydın durumu.</param>
/// <param name="Sinir">Windows'un verdiği dikdörtgen; hayalet kayıtta boş.</param>
/// <param name="Dosya">Kaydın gösterdiği dosya; okunamadıysa null.</param>
/// <param name="RectHr">Dikdörtgen sorusunun sonucu (HRESULT).</param>
internal readonly record struct DuvarKagidiKaydi(
    uint Sira, string Kimlik, KayitDurumu Durum, Rectangle Sinir, string? Dosya, int RectHr);

/// <summary>Bir ekranın duvar kâğıdına hangi yoldan yazılacağı.</summary>
internal enum HedefTuru
{
    /// <summary>Ekranın kaydına aygıt yoluyla.</summary>
    Kimlik,

    /// <summary>Aygıt yolu verilmeden, bütün ekranlara (SetWallpaper(NULL)); yalnız tek ekranda.</summary>
    Hepsi,

    /// <summary>
    /// Ekranın kaydı canlı ama kimliği boş: ona yalnız aygıt yolu verilmeden
    /// ulaşılabiliyor (çok ekranda; bkz. <see cref="WallpaperCanvas.Apply"/>).
    /// </summary>
    Kimliksiz,

    /// <summary>Ekranın kaydı bulunamadı; yazılmaz.</summary>
    Yok,
}

/// <summary>Bir ekranın yazım yolu ve (aygıt yoluyla yazılıyorsa) kaydın kimliği.</summary>
internal readonly record struct DuvarKagidiHedefi(HedefTuru Tur, string? Kimlik)
{
    public static DuvarKagidiHedefi Hepsi => new(HedefTuru.Hepsi, null);

    public static DuvarKagidiHedefi Kimliksiz => new(HedefTuru.Kimliksiz, null);

    public static DuvarKagidiHedefi Yok => new(HedefTuru.Yok, null);

    public static DuvarKagidiHedefi KimlikIle(string kimlik) => new(HedefTuru.Kimlik, kimlik);
}

/// <summary>
/// Okunan kayıtlar ve Windows'un bütün masaüstü için verdiği yol
/// (GetWallpaper(NULL)). Canlı kayıt hiç yokken aygıt yolu verilmeden yazılan
/// görüntünün ekranda durup durmadığı bu yoldan anlaşılır.
/// </summary>
internal sealed class DuvarKagidiTablosu : IReadOnlyList<DuvarKagidiKaydi>
{
    private readonly IReadOnlyList<DuvarKagidiKaydi> _kayitlar;

    public DuvarKagidiTablosu(IReadOnlyList<DuvarKagidiKaydi> kayitlar, string? ortak)
    {
        _kayitlar = kayitlar;
        Ortak = ortak;
    }

    /// <summary>Aygıt yolu verilmeden okunan dosya; okunamadıysa null.</summary>
    public string? Ortak { get; }

    public int Count => _kayitlar.Count;

    public DuvarKagidiKaydi this[int index] => _kayitlar[index];

    public IEnumerator<DuvarKagidiKaydi> GetEnumerator() => _kayitlar.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Windows'un duvar kâğıdı listesini okur ve bir ekranın görüntüsünün hangi
/// kayda yazılacağını seçer.
///
/// Neden gerekiyor: Explorer'ın listesi bozulabiliyor. 2026-10-17'de ev
/// bilgisayarında tek ekranın listesinde iki kayıt vardı: ekranda görünen kaydın
/// kimliği boş dizeydi, ekranın gerçek aygıt yolunu taşıyan kayıt ise hiçbir
/// ekrana karşılık gelmiyordu (dikdörtgen sorusu E_FAIL). 1.14 kimliği tutan
/// kaydı seçiyordu; görüntü hatasız yazılıyor ama ekrana hiç çıkmıyordu, ekranda
/// iki gün önceki takvim kaldı. Ekranda ne durduğu da aynı kayıttan okunduğu
/// için nöbet denetimi bunu göremiyordu. Bu yüzden kayıtlar dikdörtgenlerine
/// göre ayrılıyor ve görüntü yalnız canlı kayıtlara yazılıyor; tek ekranda
/// liste tutarsızsa aygıt yolu verilmeden (ölçüldü: bozuk listede de ekrana
/// ulaşıyor).
/// </summary>
internal static class DuvarKagidiKayitlari
{
    /// <summary>COM'un aktarım katmanının (FACILITY_RPC) hata kodları: 0x8001xxxx.</summary>
    private const int RpcAilesi = unchecked((int)0x80010000);

    /// <summary>RPC_S_SERVER_UNAVAILABLE: Explorer'a ulaşılamıyor.</summary>
    private const int SunucuYok = unchecked((int)0x800706BA);

    /// <summary>RPC_S_CALL_FAILED: çağrı yolda kesildi.</summary>
    private const int CagriKesildi = unchecked((int)0x800706BE);

    /// <summary>RPC_S_CALL_FAILED_DNE: çağrı Explorer'a hiç ulaşmadı.</summary>
    private const int CagriUlasmadi = unchecked((int)0x800706BF);

    /// <summary>
    /// Windows'un duvar kâğıdı arayüzünü açar. Kalender'in bütün okuma ve
    /// yazmaları buradan geçer; sınamalar bunu sahtesiyle değiştirir, gerçek
    /// duvar kâğıdına dokunulmaz.
    /// </summary>
    internal static Func<IDesktopWallpaper> Ac { get; set; } = () => (IDesktopWallpaper)new DesktopWallpaperClass();

    /// <summary>
    /// Bütün kayıtları okur: her kayıtta kimlik, dikdörtgen ve gösterdiği
    /// dosya; ayrıca aygıt yolu verilmeden okunan dosya. Bir kaydın
    /// dikdörtgeni ya da dosyası okunamazsa öbürleri yine okunur. Yalnız okur.
    /// </summary>
    /// <returns>
    /// Kayıtlar (<see cref="DuvarKagidiTablosu"/>); kayıt sayısı ya da bir
    /// kaydın kimliği okunamazsa, bir kaydın dikdörtgen sorusu Explorer'a
    /// ulaşamazsa null (liste bilinmiyor).
    /// </returns>
    internal static IReadOnlyList<DuvarKagidiKaydi>? Oku(IDesktopWallpaper wp)
    {
        uint sayi;
        try
        {
            sayi = wp.GetMonitorDevicePathCount();
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kâğıdı ekran listesi okunamadı: " + ex.Message);
            return null;
        }

        var kayitlar = new List<DuvarKagidiKaydi>();
        for (uint i = 0; i < sayi; i++)
        {
            // Kimliği okunamayan kayıt hayalet sayılmaz: hangi ekrana ait olduğu
            // bilinmiyor, kullanıcının bir ekranı da olabilir. Hayalet sayılsaydı
            // yedekten atılır, gösterdiği dosya temizlikte korunmazdı; tek bir
            // anlık okuma hatası o ekransız, kalıcı bir yedek yazdırırdı. 1.14'te
            // de bu hata yedeği durduruyordu, sonraki yazım yeniden deniyordu.
            // Liste o tur bilinmiyor sayılır: yedek alınmaz, dosya silinmez,
            // nöbet yeniden yazdırmaz. Dikdörtgen sorusu Explorer'a ulaşamazsa
            // da öyle (bkz. KaydiOku).
            if (KaydiOku(wp, i) is not DuvarKagidiKaydi kayit)
            {
                return null;
            }

            kayitlar.Add(kayit);
        }

        string? ortak;
        try
        {
            ortak = wp.GetWallpaper(null) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Debug("Ortak duvar kâğıdı okunamadı: " + ex.Message);
            ortak = null;
        }

        return new DuvarKagidiTablosu(kayitlar, ortak);
    }

    /// <returns>
    /// Kayıt; kimliği okunamazsa ya da dikdörtgen sorusu Explorer'a
    /// ulaşamazsa (<see cref="UlasilamadiMi"/>) null.
    /// </returns>
    private static DuvarKagidiKaydi? KaydiOku(IDesktopWallpaper wp, uint sira)
    {
        string kimlik;
        try
        {
            kimlik = wp.GetMonitorDevicePathAt(sira) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Debug($"Duvar kâğıdı kaydının kimliği okunamadı ({sira}): {ex.Message}");
            return null;
        }

        // Kimliği boş kayıtla da sorulur: boş dizeyle hem dikdörtgen hem dosya
        // okunabiliyor (ölçüldü).
        // Explorer'ın cevabı dönüş değeri olarak geliyor (PreserveSig): hayalet
        // kaydın E_FAIL'i de. İstisna ise Explorer'ın cevabı değil, çağrının ona
        // ulaşamadığıdır; RPC kodları da öyle (Explorer çağrının ortasında
        // kapandı ya da yeniden başlıyor). Önceden ikisi de hayalet sayılıyordu:
        // kayıt o tur yedekten atılıyor (ilk yedekse kalıcı olarak), gösterdiği
        // dosya temizlikte korunmuyor, liste tutarsız diye günlüğe hata
        // yazılıyordu. Kimliği okunamayan kayıttaki gibi liste bilinmiyor sayılır.
        int hr;
        NativeMethods.RECT r = default;
        try
        {
            hr = wp.GetMonitorRECT(kimlik, out r);
        }
        catch (Exception ex)
        {
            Log.Debug($"Duvar kâğıdı kaydının dikdörtgeni sorulamadı ({sira}): {ex.Message}");
            return null;
        }

        if (UlasilamadiMi(hr))
        {
            Log.Debug($"Duvar kâğıdı kaydının dikdörtgeni sorulamadı ({sira}): Explorer'a ulaşılamadı (0x{hr:X8}).");
            return null;
        }

        Rectangle sinir = hr >= 0 ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom) : Rectangle.Empty;

        string? dosya;
        try
        {
            dosya = wp.GetWallpaper(kimlik) ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Debug($"Duvar kâğıdı kaydının dosyası okunamadı ({sira}): {ex.Message}");
            dosya = null;
        }

        return new DuvarKagidiKaydi(sira, kimlik, Siniflandir(hr, sinir), sinir, dosya, hr);
    }

    /// <summary>
    /// Sonuç kodu Explorer'ın cevabı değil de çağrının ona ulaşamadığını mı
    /// söylüyor: COM'un aktarım hataları (FACILITY_RPC, ör. RPC_E_DISCONNECTED
    /// 0x80010108) ve RPC_S_SERVER_UNAVAILABLE, RPC_S_CALL_FAILED,
    /// RPC_S_CALL_FAILED_DNE. Duvar kâğıdı arayüzü Explorer'ın içinde çalışıyor;
    /// bu kodlar kaydın hiçbir ekrana karşılık gelmediğini söylemez.
    /// </summary>
    internal static bool UlasilamadiMi(int hr)
        => (hr & unchecked((int)0xFFFF0000)) == RpcAilesi || hr == SunucuYok || hr == CagriKesildi || hr == CagriUlasmadi;

    /// <summary>
    /// Dikdörtgen sorusunun sonucuna göre kaydın durumu: hata hayalet; S_OK ve
    /// boş olmayan dikdörtgen canlı; öbürleri (S_FALSE, boş dikdörtgen) bağlı
    /// değil. Hayalet yalnız hata dönen kayıttır: bağlı olmayan ekran olağan
    /// bir durumdur, aygıt yolu verilmeden yazmaya gerekçe olmaz.
    /// </summary>
    internal static KayitDurumu Siniflandir(int hr, Rectangle sinir)
        => hr < 0 ? KayitDurumu.Hayalet
            : hr == 0 && sinir.Width > 0 && sinir.Height > 0 ? KayitDurumu.Canli
            : KayitDurumu.BagliDegil;

    /// <summary>
    /// Liste tutarlı mı: hayalet kayıt yok, canlı kayıtların kimliği dolu ve
    /// hiçbir kimlik iki kayıtta geçmiyor.
    /// </summary>
    internal static bool Saglikli(IReadOnlyList<DuvarKagidiKaydi> k)
        => k.All(x => x.Durum != KayitDurumu.Hayalet)
            && k.All(x => x.Durum != KayitDurumu.Canli || !string.IsNullOrWhiteSpace(x.Kimlik))
            && k.GroupBy(x => x.Kimlik, StringComparer.OrdinalIgnoreCase).All(g => g.Count() == 1);

    /// <summary>
    /// Ekranın görüntüsü hangi yoldan yazılmalı? Hayalet ya da bağlı olmayan
    /// kayıt hiçbir zaman seçilmez.
    ///
    /// Tek ekranda: liste tutarlıysa ve tam bir canlı kayıt varsa o kayıt.
    /// Ekranın kimliğiyle tutması aranmaz: kimliği okunamamış (\\.\DISPLAY1
    /// adını taşıyan) ekranın kaydı da odur; önceki sürümlerdeki dikdörtgen
    /// eşleşmesinin karşılığı. Öbür bütün durumlarda (liste tutarsız, canlı
    /// kayıt yok, birden çok canlı) aygıt yolu verilmeden: tek ekranda bütün
    /// ekranlar o ekrandır. Liste tutarsızken kimliği dolu tek canlı kayıt da
    /// seçilmez: 2026-10-17'de aynı bozuk listede canlı ve hayalet kayıt birkaç
    /// saat içinde yer değiştirdi (önce boş kimlikli kayıt canlıydı, sonra
    /// aygıt yollu kayıt); dikdörtgen sorusu hangi kaydın ekranda göründüğünü
    /// kanıtlamıyor. Aygıt yolu verilmeden yazılan görüntü bütün kayıtlara
    /// gider, hangisi ekrandaysa ona da ulaşır (bozuk listede ölçüldü); liste
    /// iki hâl arasında gidip gelse de yazım yolu değişmez.
    ///
    /// Çok ekranda sırayla: kimliği ekranınkine eşit tekil canlı kayıt; yoksa
    /// kimliği dolu, tekil ve dikdörtgeni ekranınkiyle bire bir aynı tek canlı
    /// kayıt; yoksa kimliği boş ve dikdörtgeni aynı tek canlı kayıt (kimliksiz).
    /// Hiçbiri yoksa yazılmaz: aygıt yolu verilmeden yazmak bütün ekranlara
    /// aynı görüntüyü basardı.
    /// </summary>
    /// <param name="bagliEkranSayisi">Bağlı ekran sayısı (yalnız bu turda yazılanlar değil).</param>
    internal static DuvarKagidiHedefi Coz(IReadOnlyList<DuvarKagidiKaydi> k, MonitorTarget ekran, int bagliEkranSayisi)
    {
        bool Tekil(string kimlik) => k.Count(x => KimlikEsit(x.Kimlik, kimlik)) == 1;
        List<DuvarKagidiKaydi> canlilar = k.Where(x => x.Durum == KayitDurumu.Canli).ToList();

        if (bagliEkranSayisi <= 1)
        {
            // Tutarlı listede canlı kaydın kimliği dolu ve tekildir (bkz. Saglikli).
            return canlilar.Count == 1 && Saglikli(k)
                ? DuvarKagidiHedefi.KimlikIle(canlilar[0].Kimlik)
                : DuvarKagidiHedefi.Hepsi;
        }

        foreach (DuvarKagidiKaydi x in canlilar)
        {
            if (!string.IsNullOrWhiteSpace(x.Kimlik) && KimlikEsit(x.Kimlik, ekran.StableId) && Tekil(x.Kimlik))
            {
                return DuvarKagidiHedefi.KimlikIle(x.Kimlik);
            }
        }

        List<DuvarKagidiKaydi> sinirlilar = canlilar
            .Where(x => !string.IsNullOrWhiteSpace(x.Kimlik) && Tekil(x.Kimlik) && x.Sinir == ekran.Bounds)
            .ToList();
        if (sinirlilar.Count == 1)
        {
            return DuvarKagidiHedefi.KimlikIle(sinirlilar[0].Kimlik);
        }

        return canlilar.Count(x => string.IsNullOrWhiteSpace(x.Kimlik) && x.Sinir == ekran.Bounds) == 1
            ? DuvarKagidiHedefi.Kimliksiz
            : DuvarKagidiHedefi.Yok;
    }

    /// <summary>
    /// Verilen yoldan yazılan ekranda şu an duran dosya. Yalnız canlı kayıtlara
    /// bakılır: hayalet kaydın gösterdiği dosya ekranda görünmüyor.
    /// </summary>
    /// <returns>
    /// Aygıt yoluyla: o kaydın dosyası. Aygıt yolu verilmeden: canlı kayıtların
    /// hepsi aynı dosyayı gösteriyorsa o (canlı kayıt yoksa aygıt yolu verilmeden
    /// okunan dosya). Kimliksiz: ekranın dikdörtgenindeki boş kimlikli canlı
    /// kaydın dosyası. Bilinmiyorsa null.
    /// </returns>
    internal static string? Gorunen(IReadOnlyList<DuvarKagidiKaydi> k, DuvarKagidiHedefi h, MonitorTarget ekran)
    {
        switch (h.Tur)
        {
            case HedefTuru.Kimlik:
                foreach (DuvarKagidiKaydi x in k)
                {
                    if (x.Durum == KayitDurumu.Canli && KimlikEsit(x.Kimlik, h.Kimlik))
                    {
                        return x.Dosya;
                    }
                }

                return null;

            case HedefTuru.Hepsi:
                List<string?> dosyalar = k.Where(x => x.Durum == KayitDurumu.Canli).Select(x => x.Dosya).ToList();
                if (dosyalar.Count == 0)
                {
                    return (k as DuvarKagidiTablosu)?.Ortak;
                }

                string? ilk = dosyalar[0];
                return ilk is not null && dosyalar.All(d => string.Equals(d, ilk, StringComparison.OrdinalIgnoreCase))
                    ? ilk
                    : null;

            case HedefTuru.Kimliksiz:
                List<DuvarKagidiKaydi> bos = KimliksizCanlilar(k, ekran);
                return bos.Count == 1 ? bos[0].Dosya : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// Verilen yoldan yazılan ekranda ne durduğu okunamadı mı? Öyleyse
    /// <see cref="Gorunen"/>'in null'u "ekranda başka bir şey var" değil
    /// "bilinmiyor" demektir: ilgili canlı kaydın dosyası okunamadı (Explorer
    /// meşgul ya da yeniden başlıyor), aygıt yolu verilmeden yazılan ekranda
    /// canlı kayıtlardan birinin dosyası, canlı kayıt yoksa aygıt yolu
    /// verilmeden okunan dosya okunamadı. Nöbet böyle bir ekran için yeniden
    /// yazım istemez (emin olamadığı yere yazmaz; 1.14'te okuma hatası da
    /// "yerinde" sayılıyordu).
    /// </summary>
    internal static bool GorunenBilinmiyor(IReadOnlyList<DuvarKagidiKaydi> k, DuvarKagidiHedefi h, MonitorTarget ekran)
    {
        switch (h.Tur)
        {
            case HedefTuru.Kimlik:
                return k.Any(x => x.Durum == KayitDurumu.Canli && KimlikEsit(x.Kimlik, h.Kimlik) && x.Dosya is null);

            case HedefTuru.Hepsi:
                return k.Any(x => x.Durum == KayitDurumu.Canli)
                    ? k.Any(x => x.Durum == KayitDurumu.Canli && x.Dosya is null)
                    : (k as DuvarKagidiTablosu)?.Ortak is null;

            case HedefTuru.Kimliksiz:
                List<DuvarKagidiKaydi> bos = KimliksizCanlilar(k, ekran);
                return bos.Count == 1 && bos[0].Dosya is null;

            default:
                return false;
        }
    }

    /// <summary>Ekranın dikdörtgenindeki, kimliği boş canlı kayıtlar.</summary>
    private static List<DuvarKagidiKaydi> KimliksizCanlilar(IReadOnlyList<DuvarKagidiKaydi> k, MonitorTarget ekran)
        => k.Where(x => x.Durum == KayitDurumu.Canli && string.IsNullOrWhiteSpace(x.Kimlik) && x.Sinir == ekran.Bounds)
            .ToList();

    /// <summary>Kayıtların günlük için tek satırlık özeti (dosyaların yalnız adı).</summary>
    internal static string Ozet(IReadOnlyList<DuvarKagidiKaydi> k)
    {
        IEnumerable<string> satirlar = k.Select(x =>
            $"[{x.Sira}] {(x.Kimlik.Length == 0 ? "(boş kimlik)" : x.Kimlik)} {x.Durum} hr=0x{x.RectHr:X8} "
            + (x.Durum == KayitDurumu.Hayalet ? "-" : $"{x.Sinir.X},{x.Sinir.Y} {x.Sinir.Width}x{x.Sinir.Height}")
            + " -> " + DosyaAdi(x.Dosya));

        string ozet = k.Count == 0 ? "(kayıt yok)" : string.Join("; ", satirlar);
        return k is DuvarKagidiTablosu t ? ozet + "; aygıtsız -> " + DosyaAdi(t.Ortak) : ozet;
    }

    /// <summary>Windows'un listesinin şimdiki hâlinin günlük özeti; okunamazsa bunu söyler.</summary>
    internal static string SimdikiOzet()
    {
        try
        {
            IReadOnlyList<DuvarKagidiKaydi>? kayitlar = Oku(Ac());
            return kayitlar is null ? "(liste okunamadı)" : Ozet(kayitlar);
        }
        catch (Exception ex)
        {
            return "(liste okunamadı: " + ex.Message + ")";
        }
    }

    private static string DosyaAdi(string? yol)
        => yol is null ? "(okunamadı)" : yol.Length == 0 ? "(boş)" : Path.GetFileName(yol);

    private static bool KimlikEsit(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
