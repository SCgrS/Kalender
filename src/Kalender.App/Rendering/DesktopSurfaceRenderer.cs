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
    /// Yalnızca zemini üretir; takvim çizilmez. Takvimi sürükleyerek
    /// yerleştirme ekranı arka plan olarak bunu kullanır.
    /// </summary>
    public Bitmap RenderBackgroundOnly(Size pixelSize, AppSettings settings, MonitorPlacement placement)
    {
        var bitmap = new Bitmap(Math.Max(pixelSize.Width, 1), Math.Max(pixelSize.Height, 1),
            PixelFormat.Format32bppPArgb);

        var theme = Theme.From(settings.Appearance);

        using Graphics g = Graphics.FromImage(bitmap);
        string? photo = placement.PhotoPath ?? settings.Appearance.PhotoPath;
        PhotoFit fit = placement.PhotoPath is not null ? placement.PhotoFit : settings.Appearance.PhotoFit;
        _background.Paint(g, new Rectangle(Point.Empty, bitmap.Size), settings.Appearance, theme, photo, fit);

        return bitmap;
    }

    /// <summary>
    /// Yalnızca takvim bloğunu saydam bir yüzeye çizer. Sürükleyerek
    /// yerleştirmede ekranda gezdirilen görüntü budur.
    /// </summary>
    public Bitmap RenderCalendarBlock(float scale, AppSettings settings,
        CalendarDataSnapshot data, DateOnly today)
    {
        SizeF size = CalendarLayout.MeasureSize(scale);
        var bitmap = new Bitmap(
            Math.Max((int)Math.Ceiling(size.Width), 1),
            Math.Max((int)Math.Ceiling(size.Height), 1),
            PixelFormat.Format32bppPArgb);

        var theme = Theme.From(settings.Appearance);
        var layout = CalendarLayout.Create(today, scale, PointF.Empty);

        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        _calendar.Paint(g, layout, theme, settings.Appearance, data, today);

        return bitmap;
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

    /// <summary>
    /// Fotoğraf kipinde takvimin arkasına isteğe bağlı yumuşak karartma.
    ///
    /// Şekil daire değil, köşeleri yuvarlatılmış bir dikdörtgen: takvim de
    /// dikdörtgen olduğu için gölge onu daha doğal sarıyor ve ekranın ortasında
    /// kocaman siyah bir daire oluşmuyor. Kenarlar yumuşak geçişli.
    /// </summary>
    private static void PaintBackdrop(Graphics g, CalendarLayout layout, AppearanceSettings appearance, float scale)
    {
        if (appearance.BackgroundMode != BackgroundMode.Photo || appearance.BackdropDim <= 0)
        {
            return;
        }

        // En koyu ayarda bile tam siyah olmasın; altındaki fotoğraf sezilsin.
        int alpha = (int)Math.Round(Math.Clamp(appearance.BackdropDim, 0, 100) / 100.0 * 190);

        float pad = 90f * scale;
        var rect = RectangleF.Inflate(layout.Bounds, pad, pad);
        float radius = Math.Min(rect.Width, rect.Height) * 0.28f;

        using GraphicsPath path = RoundedRectangle(rect, radius);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f)),
            CenterColor = Color.FromArgb(alpha, 0, 0, 0),
            SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) },

            // Koyu bölge takvimi kapsayacak kadar geniş dursun; solma dışarıda başlasın.
            FocusScales = new PointF(0.80f, 0.80f),
        };

        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    public void Dispose()
    {
        _background.Dispose();
        _calendar.Dispose();
    }
}
