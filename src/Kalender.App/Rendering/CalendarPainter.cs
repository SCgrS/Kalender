using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using Kalender.Calendar;
using Kalender.Settings;

namespace Kalender.Rendering;

/// <summary>
/// Takvim ızgarasını bir <see cref="Graphics"/> yüzeyine çizer.
/// Yüzeyin ne olduğunu bilmez; masaüstü katmanı da, ayarlardaki önizleme de
/// aynı çizimi kullanır.
/// </summary>
internal sealed class CalendarPainter : IDisposable
{
    // Ölçek 1.0 iken punto ve ölçüler (piksel).
    private const float MonthFontSize = 46f;
    private const float MonthLetterSpacing = 6f;

    // Gün adı harfleri, rakamlardan birkaç punto büyük duruyor; şartname böyle istiyor.
    private const float WeekdayFontSize = 43f;
    private const float DayFontSize = 40f;
    private const float TodayRadius = 34f;
    private const float DotDiameter = 8f;
    private const float DotGap = 5.5f;
    private const float DotsOffsetY = 40f;

    private readonly FontFamily _family;
    private readonly Dictionary<(float, FontStyle), Font> _fontCache = new();

    public CalendarPainter()
    {
        _family = ResolveFamily();
    }

    private static FontFamily ResolveFamily()
    {
        foreach (string name in new[] { "Segoe UI", "Segoe UI Variable Text", "Tahoma", "Arial" })
        {
            try
            {
                return new FontFamily(name);
            }
            catch (ArgumentException)
            {
                // Yazı tipi yoksa sıradakini dene.
            }
        }

        return FontFamily.GenericSansSerif;
    }

    private Font GetFont(float sizePx, FontStyle style)
    {
        var key = (sizePx, style);
        if (!_fontCache.TryGetValue(key, out var font))
        {
            font = new Font(_family, sizePx, style, GraphicsUnit.Pixel);
            _fontCache[key] = font;
        }

        return font;
    }

    /// <summary>Yazı tipinin em yüksekliğine göre üst çıkıntı (piksel).</summary>
    private float Ascent(Font font)
        => _family.GetCellAscent(font.Style) / (float)_family.GetEmHeight(font.Style) * font.Size;

    public void Paint(Graphics g, CalendarLayout layout, Theme theme, AppearanceSettings appearance,
        CalendarDataSnapshot data, DateOnly today)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = layout.Scale;

        DrawMonthTitle(g, layout, theme, scale);
        DrawWeekdayHeader(g, layout, theme, appearance, scale);
        DrawDays(g, layout, theme, appearance, data, today, scale);
    }

    private void DrawMonthTitle(Graphics g, CalendarLayout layout, Theme theme, float scale)
    {
        Font font = GetFont(MonthFontSize * scale, FontStyle.Bold);
        Font weekdayFont = GetFont(WeekdayFontSize * scale, FontStyle.Bold);
        string text = TurkishNames.MonthNameUpper(layout.MonthStart.Month);
        float spacing = MonthLetterSpacing * scale;

        using var brush = new SolidBrush(theme.Text);
        using var format = TypographicFormat(StringAlignment.Near);

        float y = layout.TitleBounds.Y + (layout.TitleBounds.Height / 2f) + (0.35f * font.Size) - Ascent(font);

        // Ay adı, hücrenin sol kenarına değil, ilk sütundaki gün harfinin
        // (Pazartesi'nin P'si) sol kenarına hizalanır; ikisi alt alta simetrik dursun.
        RectangleF firstCell = layout.WeekdayCells[0];
        float harfGenisligi = g.MeasureString(
            TurkishNames.WeekdayInitials[0], weekdayFont, PointF.Empty, format).Width;
        float x = firstCell.X + ((firstCell.Width - harfGenisligi) / 2f);

        // Harf aralığı GDI+ tarafından desteklenmediği için harfleri tek tek çiziyoruz.
        foreach (char ch in text)
        {
            string s = ch.ToString();
            g.DrawString(s, font, brush, new PointF(x, y), format);
            x += g.MeasureString(s, font, PointF.Empty, format).Width + spacing;
        }
    }

    private void DrawWeekdayHeader(Graphics g, CalendarLayout layout, Theme theme,
        AppearanceSettings appearance, float scale)
    {
        Font font = GetFont(WeekdayFontSize * scale, FontStyle.Bold);
        using var normal = new SolidBrush(theme.Text);
        using var red = new SolidBrush(theme.Holiday);
        using var format = TypographicFormat(StringAlignment.Center);

        for (int c = 0; c < CalendarLayout.Columns; c++)
        {
            RectangleF cell = layout.WeekdayCells[c];
            bool isSunday = c == 6;
            bool isSaturday = c == 5;
            Brush brush = isSunday || (isSaturday && appearance.SaturdayIsRed) ? red : normal;

            float y = cell.Y + (cell.Height / 2f) + (0.35f * font.Size) - Ascent(font);
            g.DrawString(TurkishNames.WeekdayInitials[c], font, brush,
                new PointF(cell.X + (cell.Width / 2f), y), format);
        }
    }

    private void DrawDays(Graphics g, CalendarLayout layout, Theme theme, AppearanceSettings appearance,
        CalendarDataSnapshot data, DateOnly today, float scale)
    {
        Font font = GetFont(DayFontSize * scale, FontStyle.Bold);
        Font plusFont = GetFont(DayFontSize * 0.42f * scale, FontStyle.Bold);
        using var format = TypographicFormat(StringAlignment.Center);
        using var todayBrush = new SolidBrush(theme.TodayCircle);

        float ascent = Ascent(font);
        float radius = TodayRadius * scale;

        foreach (DayCell cell in layout.Cells)
        {
            DayContent? content = data.For(cell.Date);
            bool isToday = cell.Date == today;

            if (isToday)
            {
                g.FillEllipse(todayBrush,
                    cell.Center.X - radius,
                    cell.Center.Y - radius,
                    radius * 2f,
                    radius * 2f);
            }

            Color color = ResolveDayColor(cell, content, theme, appearance, isToday);
            using (var brush = new SolidBrush(color))
            {
                float y = cell.Center.Y + (0.35f * font.Size) - ascent;
                g.DrawString(
                    cell.Date.Day.ToString(CultureInfo.InvariantCulture),
                    font, brush, new PointF(cell.Center.X, y), format);
            }

            if (content is not null && content.Entries.Count > 0)
            {
                DrawDots(g, cell, content, plusFont, theme, format, scale);
            }
        }
    }

    private static Color ResolveDayColor(DayCell cell, DayContent? content, Theme theme,
        AppearanceSettings appearance, bool isToday)
    {
        if (!cell.InCurrentMonth)
        {
            return theme.Muted;
        }

        bool red = cell.IsSunday
            || (cell.IsSaturday && appearance.SaturdayIsRed)
            || (content?.IsHoliday ?? false);

        if (red)
        {
            return theme.Holiday;
        }

        return isToday ? theme.TodayText : theme.Text;
    }

    private void DrawDots(Graphics g, DayCell cell, DayContent content, Font plusFont,
        Theme theme, StringFormat format, float scale)
    {
        // Tatiller nokta üretmez; kırmızı rakam zaten bunu anlatıyor.
        var colors = content.Entries
            .Where(e => e.Kind != EntryKind.Holiday)
            .Select(e => e.DotColor)
            .ToList();

        if (colors.Count == 0)
        {
            return;
        }

        float diameter = DotDiameter * scale;
        float gap = DotGap * scale;
        int shown = colors.Count <= 4 ? colors.Count : 3;
        int overflow = colors.Count - shown;

        string? plusText = overflow > 0 ? $"+{overflow}" : null;
        float plusWidth = 0f;
        if (plusText is not null)
        {
            plusWidth = g.MeasureString(plusText, plusFont, PointF.Empty, format).Width + gap;
        }

        float totalWidth = (shown * diameter) + ((shown - 1) * gap) + plusWidth;
        float x = cell.Center.X - (totalWidth / 2f);
        float y = cell.Center.Y + (DotsOffsetY * scale) - (diameter / 2f);

        for (int i = 0; i < shown; i++)
        {
            using var brush = new SolidBrush(colors[i]);
            g.FillEllipse(brush, x, y, diameter, diameter);
            x += diameter + gap;
        }

        if (plusText is not null)
        {
            using var brush = new SolidBrush(theme.Muted);
            float ty = cell.Center.Y + (DotsOffsetY * scale) + (0.35f * plusFont.Size) - Ascent(plusFont);
            g.DrawString(plusText, plusFont, brush, new PointF(x + (plusWidth - gap) / 2f, ty), format);
        }
    }

    private static StringFormat TypographicFormat(StringAlignment alignment)
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = alignment;
        format.LineAlignment = StringAlignment.Near;
        format.Trimming = StringTrimming.None;
        format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.NoClip
            | StringFormatFlags.MeasureTrailingSpaces;
        return format;
    }

    public void Dispose()
    {
        foreach (Font font in _fontCache.Values)
        {
            font.Dispose();
        }

        _fontCache.Clear();

        // _family bilerek elden çıkarılmıyor: paylaşılan bir sistem nesnesi
        // olabilir (GenericSansSerif) ve uygulama ömrü boyunca zaten gerekli.
    }
}
