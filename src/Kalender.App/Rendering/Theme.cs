using System.Drawing;
using Kalender.Settings;

namespace Kalender.Rendering;

/// <summary>
/// Takvimin çizimde kullandığı renk takımı. Kullanıcının seçtiği zemin ve
/// yazı rengi ayarlarından türetilir; böylece hem koyu hem açık zeminde
/// okunaklı kalır.
/// </summary>
internal sealed class Theme
{
    private Theme(Color background, Color text, Color muted, Color holiday, Color todayCircle, Color todayText)
    {
        Background = background;
        Text = text;
        Muted = muted;
        Holiday = holiday;
        TodayCircle = todayCircle;
        TodayText = todayText;
    }

    /// <summary>Zemin rengi. Fotoğraf kipinde yalnızca kenar boşlukları için kullanılır.</summary>
    public Color Background { get; }

    /// <summary>Ay adı, gün adları ve içinde bulunulan ayın günleri.</summary>
    public Color Text { get; }

    /// <summary>Önceki ve sonraki aya taşan günler.</summary>
    public Color Muted { get; }

    /// <summary>Pazar, (ayara bağlı olarak) cumartesi ve resmi tatiller.</summary>
    public Color Holiday { get; }

    /// <summary>Bugünün altındaki dolu daire.</summary>
    public Color TodayCircle { get; }

    /// <summary>Bugünün rakam rengi.</summary>
    public Color TodayText { get; }

    /// <summary>
    /// Ayarlardan tema üretir. Fotoğraf kipinde otomatik seçim yapılmaz;
    /// yazı rengini kullanıcı belirler (şartname gereği).
    /// </summary>
    public static Theme From(AppearanceSettings ayarlar)
        => ZemineGore(ayarlar, ayarlar.BackgroundMode == BackgroundMode.Photo, otomatikYazi: false);

    /// <summary>
    /// Bir ekranın gerçek zeminine göre tema üretir. Fotoğraf yalnız bazı
    /// ekranlara verildiyse öbürleri düz renkle çizilir; onlarda tema da düz
    /// zemindeki gibi kurulur. Önceden genel ayar fotoğraf kipinde olduğu için
    /// fotoğrafsız ekranda da yazı fotoğrafın rengiyle (çoğu zaman beyaz)
    /// çiziliyor, açık zeminde okunmuyordu.
    /// </summary>
    /// <param name="fotograf">Bu ekranın zeminine fotoğraf çiziliyor mu.</param>
    /// <param name="otomatikYazi">
    /// Yazı rengi ayara bakılmadan zeminden hesaplansın mı. Fotoğraf
    /// açılamadığında kullanılır: elle seçilen renk fotoğraf içindi, onun
    /// yerine kalan düz zeminde görünmeyebilir.
    /// </param>
    public static Theme ZemineGore(AppearanceSettings ayarlar, bool fotograf, bool otomatikYazi)
    {
        Color background = ColorTools.Parse(ayarlar.SolidColor, Color.FromArgb(0x2E, 0x32, 0x38));

        bool photoMode = fotograf;
        bool manual = photoMode || (!otomatikYazi && ayarlar.TextColorMode == TextColorMode.Manual);

        Color text;
        bool lightSurface;

        if (photoMode)
        {
            text = ColorTools.Parse(ayarlar.TextColor, Color.White);
            // Fotoğrafın açıklığı bilinmiyor: yazı açıksa zemin koyu kabul
            // edilir; türetilen renkler buna göre ayarlanır.
            lightSurface = !ColorTools.IsLight(text);
        }
        else
        {
            // Düz zeminde açıklık, yazı rengi elle seçilmiş olsa da zeminin
            // kendisinden çıkarılır. Önceden elle seçilen yazıdan çıkarılıyordu:
            // koyu zeminde orta tonlu bir yazı (yeşil, mavi, turuncu) koyu yazı
            // sayılıp zemin açık kabul ediliyor, bugünün dairesi zeminden koyu
            // çiziliyor ve siyah zeminde hiç görünmüyordu.
            lightSurface = ColorTools.IsLight(background);
            text = manual
                ? ColorTools.Parse(ayarlar.TextColor, Color.White)
                : lightSurface ? Color.FromArgb(0x1A, 0x1D, 0x21) : Color.White;
        }

        // Düz zeminde soluk renk zeminin kendisine doğru karıştırılır. Açık
        // zeminde beyaza doğru karıştırmak Kum Taşı gibi koyuca bej bir zeminde
        // soluk günleri neredeyse görünmez yapıyordu (karşıtlık ~1,2:1); koyu
        // zeminde sabit koyu griye doğru karıştırmak da orta tonlu bir zeminde
        // soluk günleri zeminle aynı tona getiriyordu. Zemin o griden koyuysa
        // (hazır koyu renklerin hepsi) eskisi gibi griye doğru karıştırılır:
        // siyah gibi çok koyu zeminde soluk günler eskisinden koyu çıkmasın.
        // Fotoğrafta zemin rengi fotoğrafı anlatmadığı için eskisi gibi beyaz
        // ya da koyu gri kullanılır.
        Color muted = lightSurface
            ? ColorTools.Mix(text, photoMode ? Color.White : background, 0.55)
            : ColorTools.Mix(text, photoMode ? KoyuGri : AcikOlani(background, KoyuGri), 0.55);

        Color holiday = lightSurface
            ? Color.FromArgb(0xC0, 0x2B, 0x2B)
            : Color.FromArgb(0xE4, 0x50, 0x50);

        Color todayCircle = lightSurface
            ? ColorTools.WithAlpha(ColorTools.Darken(background, 0.18), 235)
            : ColorTools.WithAlpha(ColorTools.Lighten(background, 0.16), 235);

        // Fotoğraf kipinde daire zeminden türetilemez; yazının zıddını kullanırız.
        if (photoMode)
        {
            todayCircle = lightSurface
                ? Color.FromArgb(64, 0, 0, 0)
                : Color.FromArgb(90, 255, 255, 255);
        }

        // Kullanıcı ayarlardan tek tek renk verdiyse hesaplananın yerine geçer.
        // Boş bırakılan her renk otomatik kalır; varsayılan hâl budur.
        CalendarTextColors secim = ayarlar.Colors;
        text = Override(secim.Weekday, text);
        muted = Override(secim.Muted, muted);
        holiday = Override(secim.Holiday, holiday);

        // Dairenin saydamlığı hesabın bir parçası; kullanıcı renk verdiğinde de
        // aynı yumuşaklıkta kalsın diye alfası korunuyor.
        Color? dairesi = ParseOrNull(secim.TodayCircle);
        if (dairesi is not null)
        {
            todayCircle = ColorTools.WithAlpha(dairesi.Value, todayCircle.A);
        }

        return new Theme(background, text, muted, holiday, todayCircle, text);
    }

    /// <summary>Koyu zeminde soluk günlerin yazı renginden karıştırıldığı gri.</summary>
    private static readonly Color KoyuGri = Color.FromArgb(0x30, 0x34, 0x3A);

    /// <summary>İki renkten açık olanı (bağıl parlaklığa göre).</summary>
    private static Color AcikOlani(Color a, Color b)
        => ColorTools.RelativeLuminance(a) >= ColorTools.RelativeLuminance(b) ? a : b;

    /// <summary>Ayarda renk varsa onu, yoksa hesaplananı verir.</summary>
    private static Color Override(string? hex, Color hesaplanan)
        => ParseOrNull(hex) ?? hesaplanan;

    private static Color? ParseOrNull(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }

        // Bozuk bir değer varsayılana düşmeli, siyaha değil.
        Color a = ColorTools.Parse(hex, Color.Black);
        Color b = ColorTools.Parse(hex, Color.White);
        return a == b ? a : null;
    }
}
