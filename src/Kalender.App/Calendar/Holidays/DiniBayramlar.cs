using System.Text;
using System.Text.RegularExpressions;

namespace Kalender.Calendar.Holidays;

/// <summary>
/// Ramazan ve Kurban bayramları: Diyanet'in tarihleri ve Google akışındaki
/// bayram kayıtlarının tanınıp adlarının düzeltilmesi.
///
/// Google'ın Türkiye tatil akışı dinî bayramları her yıl Diyanet'le aynı
/// vermiyor: 2027–2029'da Ramazan Bayramı bir gün geç (2027'de Google 10–12
/// Mart, Diyanet 9–11 Mart). Tablonun kapsadığı yıllarda akıştaki bayram ve
/// arife kayıtları atılıp yerlerine buradan üretilen günler konuyor
/// (<see cref="HolidayService"/>); tablonun dışındaki yıllarda akış
/// kullanılıyor, yalnızca İngilizce ya da yanlış yazılmış adları düzeltiliyor.
/// Sabit tarihli tatiller her zaman akıştan geliyor.
///
/// Tarihler Kalender Lite'taki TurkishHolidays tablosuyla aynı: Diyanet
/// İşleri Başkanlığının yıllık "Dinî Günler Listesi"
/// (vakithesaplama.diyanet.gov.tr), 2026–2035. Biri değişirse öbürü de
/// değişmeli.
/// </summary>
internal static partial class DiniBayramlar
{
    private const int RamazanUzunlugu = 3;
    private const int KurbanUzunlugu = 4;

    /// <summary>
    /// Akıştaki bir bayram kaydının tablodaki bayrama en çok bu kadar gün uzak
    /// olabileceği. Google'ın kayması bir gün; bayrama eklenen idari izin
    /// günleri de birkaç gün ötede.
    /// </summary>
    private const int YakinlikPayi = 7;

    /// <summary>
    /// Ramazan Bayramı'nın ilk günleri. 2033'te iki tane var: hicri yıl
    /// miladi yıldan 11 gün kadar kısa.
    /// </summary>
    private static readonly DateOnly[] RamazanIlkGunleri =
    {
        new(2026, 3, 20),
        new(2027, 3, 9),
        new(2028, 2, 26),
        new(2029, 2, 14),
        new(2030, 2, 4),
        new(2031, 1, 24),
        new(2032, 1, 14),
        new(2033, 1, 2),
        new(2033, 12, 23),
        new(2034, 12, 12),
        new(2035, 12, 1),
    };

    /// <summary>Kurban Bayramı'nın ilk günleri.</summary>
    private static readonly DateOnly[] KurbanIlkGunleri =
    {
        new(2026, 5, 27),
        new(2027, 5, 16),
        new(2028, 5, 5),
        new(2029, 4, 24),
        new(2030, 4, 13),
        new(2031, 4, 2),
        new(2032, 3, 22),
        new(2033, 3, 11),
        new(2034, 3, 1),
        new(2035, 2, 18),
    };

    private enum Bayram
    {
        Ramazan,
        Kurban,
    }

    /// <summary>
    /// Tablodan üretilen bütün günler, akıştaki adlarla aynı biçimde: arife
    /// yarım gün ("Ramazan Bayramı Arifesi (yarım gün)"), Ramazan Bayramı 3,
    /// Kurban Bayramı 4 gün ("Kurban Bayramı", "Kurban Bayramı 2. gün"…).
    /// Tarih Diyanet'ten, kesin; "(kesin değil)" eki konmaz.
    /// </summary>
    public static IReadOnlyList<HolidayInfo> Gunler { get; } = GunleriUret();

    /// <summary>
    /// Akıştaki kayıt, tablodaki bir Ramazan ya da Kurban Bayramı'nın (arifesi
    /// ve bayrama eklenen idari izin günleri dâhil) kaydı mı? Ad da tarih de
    /// tutmalı: sabit tatillerin adında da "Bayramı" geçiyor (19 Mayıs, 23
    /// Nisan, Cumhuriyet Bayramı Arifesi), ama Ramazan ya da Kurban geçmiyor;
    /// onlar hiçbir zaman atılmaz.
    /// </summary>
    public static bool TablodakiBayramMi(string ad, DateOnly gun)
        => DiniAd().IsMatch(Sadelestir(ad)) && TabloyaYakin(gun);

    /// <summary>
    /// Akıştaki dinî bayram adını Türkçeye çevirir: "Kurban Bayrami Day 2" →
    /// "Kurban Bayramı 2. gün", "Sacrifice Feast Holiday" → "Kurban Bayramı".
    /// "(kesin değil)" eki korunur. Dinî bayram olmayan ad olduğu gibi kalır.
    /// </summary>
    public static string AdiDuzelt(string ad)
    {
        string sade = Sadelestir(ad);
        Bayram? bayram = KurbanAdi().IsMatch(sade) ? Bayram.Kurban
            : RamazanAdi().IsMatch(sade) ? Bayram.Ramazan
            : null;

        if (bayram is null)
        {
            return ad;
        }

        // Türkçe ad, yer yer İngilizce ya da "ı" yerine "i": yalnız o yerler düzeltilir.
        if (TurkceAd().IsMatch(sade))
        {
            string duzgun = BayramiYazimi().Replace(ad, "$1ayramı");
            return IngilizceGun().Replace(duzgun, "$1. gün");
        }

        // Bütünüyle İngilizce ad ("Eid al Adha Public Sector Holiday"): baştan kurulur.
        var yeni = new StringBuilder(bayram == Bayram.Kurban ? "Kurban Bayramı" : "Ramazan Bayramı");
        if (Arife().IsMatch(sade))
        {
            yeni.Append(" Arifesi");
        }

        Match gun = IngilizceGun().Match(sade);
        if (gun.Success)
        {
            yeni.Append(' ').Append(gun.Groups[1].Value).Append(". gün");
        }

        if (YarimGun().IsMatch(sade))
        {
            yeni.Append(" (yarım gün)");
        }

        if (KamuIzni().IsMatch(sade))
        {
            yeni.Append(" (idari izin)");
        }

        if (KesinDegil().IsMatch(ad))
        {
            yeni.Append(" (kesin değil)");
        }

        return yeni.ToString();
    }

    private static bool TabloyaYakin(DateOnly gun)
    {
        return Yakin(RamazanIlkGunleri, RamazanUzunlugu) || Yakin(KurbanIlkGunleri, KurbanUzunlugu);

        bool Yakin(DateOnly[] ilkGunler, int uzunluk)
            => ilkGunler.Any(ilk => gun >= ilk.AddDays(-1 - YakinlikPayi)
                && gun <= ilk.AddDays(uzunluk - 1 + YakinlikPayi));
    }

    private static List<HolidayInfo> GunleriUret()
    {
        var gunler = new List<HolidayInfo>();
        Ekle(RamazanIlkGunleri, RamazanUzunlugu, "Ramazan Bayramı", numarali: false);
        Ekle(KurbanIlkGunleri, KurbanUzunlugu, "Kurban Bayramı", numarali: true);
        return gunler.OrderBy(g => g.Date).ToList();

        // Google'ın akışında Ramazan Bayramı'nın her günü yalnız adıyla, Kurban
        // Bayramı'nın 2.–4. günleri numarasıyla geliyor; aynısı yapılıyor.
        void Ekle(DateOnly[] ilkGunler, int uzunluk, string ad, bool numarali)
        {
            foreach (DateOnly ilk in ilkGunler)
            {
                gunler.Add(new HolidayInfo(ilk.AddDays(-1), ad + " Arifesi (yarım gün)", HolidayKind.HalfDay, false));
                for (int i = 0; i < uzunluk; i++)
                {
                    string gunAdi = numarali && i > 0 ? $"{ad} {i + 1}. gün" : ad;
                    gunler.Add(new HolidayInfo(ilk.AddDays(i), gunAdi, HolidayKind.Full, false));
                }
            }
        }
    }

    /// <summary>Küçük harfe çevirip Türkçe harfleri sadeleştirir; adları tanımak için.</summary>
    private static string Sadelestir(string ad)
    {
        var sb = new StringBuilder(ad.Length);
        foreach (char c in ad.ToLowerInvariant())
        {
            sb.Append(c switch
            {
                'ı' or 'î' => 'i',
                'ş' => 's',
                'ğ' => 'g',
                'ü' or 'û' => 'u',
                'ö' => 'o',
                'ç' => 'c',
                'â' => 'a',
                _ => c,
            });
        }

        return sb.ToString();
    }

    // Aşağıdakiler sadeleştirilmiş adda arar (küçük harf, Türkçe harfsiz);
    // BayramiYazimi, IngilizceGun ve KesinDegil özgün adda da çalışır.

    [GeneratedRegex(@"\b(ramazan|ramadan|fitr|kurban|sacrifice|adha|eid)\b|\bseker\s+bayram", RegexOptions.CultureInvariant)]
    private static partial Regex DiniAd();

    [GeneratedRegex(@"\b(kurban|sacrifice|adha)\b", RegexOptions.CultureInvariant)]
    private static partial Regex KurbanAdi();

    [GeneratedRegex(@"\b(ramazan|ramadan|fitr)\b|\bseker\s+bayram", RegexOptions.CultureInvariant)]
    private static partial Regex RamazanAdi();

    [GeneratedRegex(@"\b(ramazan|kurban)\b|\bseker\s+bayram", RegexOptions.CultureInvariant)]
    private static partial Regex TurkceAd();

    [GeneratedRegex(@"\b(eve|arife\w*)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Arife();

    [GeneratedRegex(@"half[\s-]*day|yarim\s+gun", RegexOptions.CultureInvariant)]
    private static partial Regex YarimGun();

    [GeneratedRegex(@"public\s+sector", RegexOptions.CultureInvariant)]
    private static partial Regex KamuIzni();

    [GeneratedRegex(@"\b([Bb])ayrami\b", RegexOptions.CultureInvariant)]
    private static partial Regex BayramiYazimi();

    [GeneratedRegex(@"\bday\s+(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IngilizceGun();

    [GeneratedRegex(@"\(kesin değil\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KesinDegil();
}
