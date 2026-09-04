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
    {
        Color background = ColorTools.Parse(ayarlar.SolidColor, Color.FromArgb(0x2E, 0x32, 0x38));

        bool photoMode = ayarlar.BackgroundMode == BackgroundMode.Photo;
        bool manual = photoMode || ayarlar.TextColorMode == TextColorMode.Manual;

        Color text;
        bool lightSurface;

        if (manual)
        {
            text = ColorTools.Parse(ayarlar.TextColor, Color.White);
            // Yazı açıksa zemin koyu kabul edilir; türetilen renkler buna göre ayarlanır.
            lightSurface = !ColorTools.IsLight(text);
        }
        else
        {
            lightSurface = ColorTools.IsLight(background);
            text = lightSurface ? Color.FromArgb(0x1A, 0x1D, 0x21) : Color.White;
        }

        Color muted = lightSurface
            ? ColorTools.Mix(text, Color.White, 0.55)
            : ColorTools.Mix(text, Color.FromArgb(0x30, 0x34, 0x3A), 0.55);

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

        return new Theme(background, text, muted, holiday, todayCircle, text);
    }
}
