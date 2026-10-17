namespace Kalender.Desktop;

/// <summary>Nöbetin ekrana bakışının sonucu (bkz. <see cref="WallpaperCanvas.EkraniDenetle"/>).</summary>
internal enum EkranDenetimi
{
    /// <summary>Her ekranda Kalender'in son görüntüsü duruyor.</summary>
    Yerinde,

    /// <summary>
    /// Emin olunamadı (liste ya da bir kaydın dosyası okunamadı, yazım yolu
    /// değişimi ilk kez görüldü); yeniden yazılmaz.
    /// </summary>
    Bilinmiyor,

    /// <summary>
    /// Windows'un listesine göre bir ekranın yazım yolu değişti (liste bozuldu
    /// ya da düzeldi) ve görüntü yeni yoldan bakınca ekranda durmuyor; yeniden
    /// yazılmalı. Çoğu zaman bir yazım bunu dengeye getirir.
    /// </summary>
    YolDegisti,

    /// <summary>
    /// Yazım yolu aynı ama ekranda Kalender'in görüntüsü yok (kullanıcı başka
    /// resim seçti ya da yazılan görüntü ekrana ulaşmadı); yeniden yazılmalı.
    /// </summary>
    Gorunmuyor,
}

/// <summary>
/// Nöbetin duvar kâğıdı denetiminin geri çekilmesi.
///
/// Yazılan görüntü ekrana ulaşmıyorsa (Explorer'ın listesi, aygıt yolu
/// verilmeden yazılan görüntüyü de ekrana çıkarmayacak kadar bozuk) her
/// denetim "görüntü yok" der ve yeniden yazım ister: yarım dakikada bir bütün
/// ekranlar yeniden çizilir, ekran başına ~11 MB'lık dosya yazılır, masaüstü
/// yenilenir; bu sonsuza dek sürerdi. Art arda <see cref="IlkEsik"/>
/// uyuşmazlıktan sonra denetim <see cref="IlkBekleme"/>, <see cref="UzunEsik"/>
/// uyuşmazlıktan sonra <see cref="UzunBekleme"/> aralıkla yapılır. Yazım yolu
/// değişimi de sayılır: çoğu zaman bir yazım onu dengeye getirir, ama Windows'un
/// listesi iki hâl arasında gidip geliyorsa (ekran kapalıyken monitör veri
/// yolundan düşüp kalkıyor) her gidiş ve dönüş yeniden yazım isterdi; önceden
/// bunun bir sınırı yoktu. Ekranda Kalender'in görüntüsü görülünce, ekran
/// yapılandırması değişince, uyanınca ve Explorer yeniden başlayınca baştan başlar.
/// </summary>
internal sealed class DuvarKagidiNobeti
{
    /// <summary>Bu kadar art arda uyuşmazlıktan sonra denetim seyrekleşir ve kullanıcıya haber verilir.</summary>
    internal const int IlkEsik = 3;

    /// <summary>Bu kadar art arda uyuşmazlıktan sonra denetim en seyrek aralığa iner.</summary>
    internal const int UzunEsik = 5;

    internal static readonly TimeSpan IlkBekleme = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan UzunBekleme = TimeSpan.FromMinutes(15);

    /// <summary>Bir sonraki denetimin en erken zamanı (UTC).</summary>
    private DateTime _sonraki = DateTime.MinValue;

    /// <summary>
    /// Art arda kaç denetim yeniden yazım istedi (ekranda Kalender'in görüntüsü
    /// yoktu ya da yazım yolu değişti), arada görüntü yerinde görülmeden.
    /// </summary>
    public int ArdArda { get; private set; }

    /// <summary>Duvar kâğıdı şimdi denetlenebilir mi (geri çekilme süresi doldu mu)?</summary>
    public bool DenetimZamani(DateTime simdi) => simdi >= _sonraki;

    /// <summary>Bir denetimin sonucunu işler.</summary>
    /// <returns>
    /// Uyuşmazlık sayısı şimdi <see cref="IlkEsik"/>'e ulaştıysa true (yeniden
    /// yazım ekranı düzeltemedi); her bozukluk döneminde bir kez.
    /// </returns>
    public bool Isle(EkranDenetimi sonuc, DateTime simdi)
    {
        switch (sonuc)
        {
            case EkranDenetimi.Yerinde:
                Sifirla();
                return false;

            case EkranDenetimi.Gorunmuyor:
            case EkranDenetimi.YolDegisti:
                ArdArda++;
                _sonraki = ArdArda >= UzunEsik ? simdi + UzunBekleme
                    : ArdArda >= IlkEsik ? simdi + IlkBekleme
                    : DateTime.MinValue;
                return ArdArda == IlkEsik;

            default:
                return false;
        }
    }

    /// <summary>Geri çekilmeyi bırakır: bir sonraki nöbet turunda denetlenir.</summary>
    public void Sifirla()
    {
        ArdArda = 0;
        _sonraki = DateTime.MinValue;
    }
}
