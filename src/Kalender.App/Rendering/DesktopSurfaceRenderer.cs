using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Kalender.Calendar;
using Kalender.Settings;

namespace Kalender.Rendering;

/// <summary>Bir monitör için üretilen görüntü ve ona ait geometri.</summary>
internal sealed class DesktopSurface : IDisposable
{
    public required Bitmap Image { get; init; }

    /// <summary>Takvim çizildiyse geometrisi; gizliyse null.</summary>
    public CalendarLayout? Layout { get; init; }

    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Zemin ve takvimi tek bir bitmap üzerinde birleştirir. Bitmap sadece
/// içerik değiştiğinde üretilir; ekranın yeniden boyanması bundan sonra
/// yalnızca bir kopyalama işlemidir, bu yüzden boştaki işlemci kullanımı sıfırdır.
/// </summary>
internal sealed class DesktopSurfaceRenderer : IDisposable
{
    private readonly BackgroundPainter _background = new();
    private readonly CalendarPainter _calendar = new();

    /// <summary>Takvimin ekran kenarına en fazla yaklaşabileceği pay (ölçek 1.0 iken piksel).</summary>
    private const float EdgeMargin = 12f;

    public DesktopSurface Render(Size pixelSize, double dpiScale, AppSettings settings,
        MonitorPlacement placement, CalendarDataSnapshot data, DateOnly today, bool drawCalendar)
    {
        var bitmap = new Bitmap(Math.Max(pixelSize.Width, 1), Math.Max(pixelSize.Height, 1),
            PixelFormat.Format32bppPArgb);

        var theme = Theme.From(settings.Appearance);
        CalendarLayout? layout = null;

        using (Graphics g = Graphics.FromImage(bitmap))
        {
            var area = new Rectangle(Point.Empty, bitmap.Size);

            string? photo = placement.PhotoPath ?? settings.Appearance.PhotoPath;
            PhotoFit fit = placement.PhotoPath is not null ? placement.PhotoFit : settings.Appearance.PhotoFit;
            _background.Paint(g, area, settings.Appearance, theme, photo, fit);

            if (drawCalendar)
            {
                float scale = (float)(dpiScale * (placement.ScalePercent / 100.0));
                PointF origin = ResolveOrigin(pixelSize, scale, placement);
                layout = CalendarLayout.Create(today, scale, origin);

                PaintBackdrop(g, layout, settings.Appearance, scale);
                _calendar.Paint(g, layout, theme, settings.Appearance, data, today);
            }
        }

        return new DesktopSurface { Image = bitmap, Layout = layout };
    }

    /// <summary>
    /// Kayıtlı oranlardan takvimin sol üst köşesini hesaplar ve bloğun
    /// ekran dışına taşmamasını güvence altına alır.
    /// </summary>
    public static PointF ResolveOrigin(Size pixelSize, float scale, MonitorPlacement placement)
    {
        SizeF block = CalendarLayout.MeasureSize(scale);
        float margin = EdgeMargin * scale;

        float maxX = Math.Max(margin, pixelSize.Width - block.Width - margin);
        float maxY = Math.Max(margin, pixelSize.Height - block.Height - margin);

        float x = (float)(placement.XRatio * pixelSize.Width);
        float y = (float)(placement.YRatio * pixelSize.Height);

        return new PointF(Math.Clamp(x, margin, maxX), Math.Clamp(y, margin, maxY));
    }

    /// <summary>Fotoğraf kipinde takvimin arkasına isteğe bağlı yumuşak karartma.</summary>
    private static void PaintBackdrop(Graphics g, CalendarLayout layout, AppearanceSettings appearance, float scale)
    {
        if (appearance.BackgroundMode != BackgroundMode.Photo || appearance.BackdropDim <= 0)
        {
            return;
        }

        int alpha = (int)Math.Round(Math.Clamp(appearance.BackdropDim, 0, 100) / 100.0 * 200);
        float pad = 28f * scale;
        var rect = RectangleF.Inflate(layout.Bounds, pad, pad);

        // Kenarları belli olmayan yumuşak bir gölge için merkezden dışa doğru soluklaşan bir fırça.
        using var path = new GraphicsPath();
        path.AddEllipse(rect);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(alpha, 0, 0, 0),
            SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) },
        };

        g.FillEllipse(brush, rect);
    }

    public void Dispose()
    {
        _background.Dispose();
        _calendar.Dispose();
    }
}
