using System.Drawing;

namespace Kalender.Calendar;

/// <summary>
/// Takvimin bir ekranda hangi ölçekte çizileceğini hesaplar.
///
/// Kural: takvim ekranın belli bir oranını kaplar, sabit bir piksel ölçüsünü
/// değil. Böylece çözünürlük değiştiğinde takvim de birlikte küçülür ya da
/// büyür; küçük ekranda kocaman, büyük ekranda minicik kalmaz.
///
/// Windows'un DPI ölçeği bilerek hesaba katılmıyor: "ekranın şu kadarını
/// kaplasın" kuralı zaten DPI'dan bağımsızdır. DPI'yı bir kez daha çarpmak
/// yüksek DPI'lı ekranlarda takvimi gereksiz büyütürdü.
/// </summary>
internal static class CalendarScale
{
    /// <summary>Takvimin tasarlandığı referans ekran.</summary>
    private const double ReferenceWidth = 2560;
    private const double ReferenceHeight = 1440;

    /// <summary>Çok küçük ya da çok büyük ekranlarda ölçeğin savrulmaması için sınırlar.</summary>
    private const double MinScale = 0.35;
    private const double MaxScale = 3.0;

    /// <summary>Ekranın kendi ölçeği; kullanıcının boyut yüzdesi hariç.</summary>
    public static double ForScreen(Size pixelSize)
    {
        double byWidth = Math.Max(pixelSize.Width, 1) / ReferenceWidth;
        double byHeight = Math.Max(pixelSize.Height, 1) / ReferenceHeight;

        // Küçük olan alınıyor ki blok dar ya da basık ekranlarda taşmasın.
        return Math.Clamp(Math.Min(byWidth, byHeight), MinScale, MaxScale);
    }

    /// <summary>Kullanıcının boyut yüzdesiyle birleştirilmiş toplam ölçek.</summary>
    public static float For(Size pixelSize, int userPercent)
        => (float)(ForScreen(pixelSize) * (Math.Clamp(userPercent, 50, 200) / 100.0));
}
