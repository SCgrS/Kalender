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

    /// <param name="today">Bugünün tarihi; yalnızca daire çizilecek günü belirler.</param>
    /// <param name="viewMonth">Çizilecek ay. Normalde bugünün ayı, menüden gezinirken başka bir ay.</param>
    public DesktopSurface Render(Size pixelSize, AppSettings settings,
        MonitorPlacement placement, CalendarDataSnapshot data, DateOnly today, DateOnly viewMonth,
        bool drawCalendar)
    {
        var bitmap = new Bitmap(Math.Max(pixelSize.Width, 1), Math.Max(pixelSize.Height, 1),
            PixelFormat.Format32bppPArgb);

        CalendarLayout? layout = null;

        using (Graphics g = Graphics.FromImage(bitmap))
        {
            var area = new Rectangle(Point.Empty, bitmap.Size);

            string? photo = FotografYolu(settings, placement);
            Image? fotograf = _background.Fotograf(settings.Appearance, photo);
            Theme theme = EkranTemasi(settings.Appearance, photo, fotograf);

            // Ekrana özel fotoğraf da genel "Yerleşim" seçimiyle çizilir; ekran
            // başına ayrı bir seçim yok. Önceden ekranın kaydındaki değer
            // kullanılıyordu; onu değiştiren bir denetim hiç olmadığı için
            // ekrana özel fotoğraf hep Kapla ile çiziliyordu.
            _background.Paint(g, area, theme, fotograf, settings.Appearance.PhotoFit);

            if (drawCalendar)
            {
                // Ölçek ekran ölçüsünden türetiliyor: çözünürlük değişince
                // takvim de birlikte küçülüp büyüsün.
                float scale = CalendarScale.For(pixelSize, placement.ScalePercent);
                PointF origin = ResolveOrigin(pixelSize, scale, placement);
                layout = CalendarLayout.Create(viewMonth, scale, origin);

                if (fotograf is not null)
                {
                    PaintBackdrop(g, layout.Bounds, settings.Appearance, scale);
                }

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

        using Graphics g = Graphics.FromImage(bitmap);
        string? photo = FotografYolu(settings, placement);
        Image? fotograf = _background.Fotograf(settings.Appearance, photo);
        Theme theme = EkranTemasi(settings.Appearance, photo, fotograf);
        _background.Paint(g, new Rectangle(Point.Empty, bitmap.Size), theme, fotograf, settings.Appearance.PhotoFit);

        return bitmap;
    }

    /// <summary>
    /// Yalnızca takvim bloğunu saydam bir yüzeye çizer. Sürükleyerek
    /// yerleştirmede ekranda gezdirilen görüntü budur.
    /// </summary>
    /// <param name="placement">Takvimin yerleştirildiği ekranın ayarı; renkler o ekranın zeminine göre seçilir.</param>
    public Bitmap RenderCalendarBlock(float scale, AppSettings settings, MonitorPlacement placement,
        CalendarDataSnapshot data, DateOnly today)
    {
        SizeF size = CalendarLayout.MeasureSize(scale);
        var bitmap = new Bitmap(
            Math.Max((int)Math.Ceiling(size.Width), 1),
            Math.Max((int)Math.Ceiling(size.Height), 1),
            PixelFormat.Format32bppPArgb);

        string? photo = FotografYolu(settings, placement);
        Theme theme = EkranTemasi(settings.Appearance, photo, _background.Fotograf(settings.Appearance, photo));
        var layout = CalendarLayout.Create(today, scale, PointF.Empty);

        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        _calendar.Paint(g, layout, theme, settings.Appearance, data, today);

        return bitmap;
    }

    /// <summary>
    /// Fotoğraf zemininde takvimin arkasındaki karartmayı saydam bir yüzeye
    /// çizer; yerleştirme ekranı onu takvim bloğuyla birlikte gezdirir. Boyutu
    /// ve rengi masaüstündekinin aynısı. Bu ekranda karartma yoksa (düz renk
    /// zemin, açılamayan fotoğraf ya da %0) null.
    /// </summary>
    /// <param name="kayma">Yüzeyin sol üst köşesinin takvim bloğunun sol üst köşesine göre yeri.</param>
    public Bitmap? KarartmaYuzeyi(float scale, AppSettings settings, MonitorPlacement placement, out PointF kayma)
    {
        kayma = PointF.Empty;
        AppearanceSettings appearance = settings.Appearance;

        // Masaüstündeki koşulun aynısı: karartma yalnız fotoğraf çizilen ekranda.
        if (appearance.BackdropDim <= 0 || _background.Fotograf(appearance, FotografYolu(settings, placement)) is null)
        {
            return null;
        }

        var blok = new RectangleF(PointF.Empty, CalendarLayout.MeasureSize(scale));
        RectangleF kare = KarartmaKaresi(blok, scale);
        kayma = kare.Location;

        var bitmap = new Bitmap(
            Math.Max((int)Math.Ceiling(kare.Width), 1),
            Math.Max((int)Math.Ceiling(kare.Height), 1),
            PixelFormat.Format32bppPArgb);

        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.Transparent);
        blok.Offset(-kare.X, -kare.Y);
        PaintBackdrop(g, blok, appearance, scale);

        return bitmap;
    }

    /// <summary>
    /// Bütün ekranlar çizildikten sonra çağrılır: bu turda hiçbir ekranın
    /// çizmediği fotoğraflar bellekten atılır.
    /// </summary>
    public void KullanilmayanFotograflariAt() => _background.KullanilmayanlariAt();

    /// <summary>Ekranın kendi fotoğrafı, yoksa genel fotoğraf.</summary>
    private static string? FotografYolu(AppSettings settings, MonitorPlacement placement)
        => placement.PhotoPath ?? settings.Appearance.PhotoPath;

    /// <summary>
    /// Ekranın gerçek zeminine göre tema: fotoğraf çiziliyorsa fotoğraf
    /// kipinin, çizilmiyorsa düz zeminin renkleri. Fotoğraf istendiği hâlde
    /// açılamadıysa zemin düz renkte kalır; yazı rengi de ona göre hesaplanır.
    /// </summary>
    private static Theme EkranTemasi(AppearanceSettings appearance, string? photo, Image? fotograf)
    {
        bool istendi = appearance.BackgroundMode == BackgroundMode.Photo && !string.IsNullOrWhiteSpace(photo);
        return Theme.ZemineGore(appearance, fotograf is not null, otomatikYazi: istendi && fotograf is null);
    }

    /// <summary>
    /// Kayıtlı oranlardan takvimin sol üst köşesini hesaplar ve bloğun
    /// ekran dışına taşmamasını güvence altına alır.
    /// </summary>
    public static PointF ResolveOrigin(Size pixelSize, float scale, MonitorPlacement placement)
    {
        float x = (float)(placement.XRatio * pixelSize.Width);
        float y = (float)(placement.YRatio * pixelSize.Height);

        return KenarPayiIcinde(new PointF(x, y), CalendarLayout.MeasureSize(scale), pixelSize, scale);
    }

    /// <summary>
    /// Takvimin ekran kenarından en az ne kadar içeride durduğu (piksel); "Takvimi
    /// taşı" ekranında kenara yapışma da bu çizgiye.
    /// </summary>
    public static float KenarPayi(float scale) => EdgeMargin * scale;

    /// <summary>
    /// Takvim bloğunun sol üst köşesini ekranın içine, kenarlardan
    /// <see cref="EdgeMargin"/> × ölçek içeride kalacak biçimde sıkıştırır.
    /// Masaüstündeki çizim ve "Takvimi taşı" ekranı aynı sınırı kullanır; önceden
    /// taşıma ekranı takvimi kenara dek götürüyor, masaüstünde takvim kenardan
    /// içeride çıkıyordu.
    /// </summary>
    public static PointF KenarPayiIcinde(PointF nokta, SizeF blok, Size ekran, float scale)
    {
        float margin = KenarPayi(scale);

        float maxX = Math.Max(margin, ekran.Width - blok.Width - margin);
        float maxY = Math.Max(margin, ekran.Height - blok.Height - margin);

        return new PointF(Math.Clamp(nokta.X, margin, maxX), Math.Clamp(nokta.Y, margin, maxY));
    }

    /// <summary>Karartma karesinin takvim bloğundan dışarı taşma payı (ölçek 1.0 iken piksel).</summary>
    private const float BackdropPad = 56f;

    /// <summary>En hafif karartmada köşe yarıçapının kare kenarına oranı.</summary>
    private const float BackdropMaxCorner = 0.28f;

    /// <summary>
    /// En hafif karartmada tam koyu kalan iç bölgenin kareye oranı; dışındaki
    /// halka saydama doğru solar.
    /// </summary>
    private const float BackdropMinFocus = 0.80f;

    /// <summary>
    /// Fotoğraf kipinde takvimin arkasına isteğe bağlı karartma.
    ///
    /// Şekil, takvim bloğunu ortalayan bir kare. Kaydırıcı büyüdükçe üç şey
    /// aynı oranda birlikte değişir: koyuluk artar, köşeler düzleşir, kenardaki
    /// yumuşak geçiş daralır. Düşük değerlerde yuvarlak ve yumuşak bir gölgedir;
    /// %100'de keskin kenarlı, köşesiz ve tam opak bir kare olur.
    /// </summary>
    /// <param name="block">Takvim bloğunun bu yüzeydeki yeri.</param>
    private static void PaintBackdrop(Graphics g, RectangleF block, AppearanceSettings appearance, float scale)
    {
        if (appearance.BackgroundMode != BackgroundMode.Photo || appearance.BackdropDim <= 0)
        {
            return;
        }

        // 0 (en hafif) ile 1 (tam kare) arası; koyuluk, köşe ve kenar buna doğrusal bağlı.
        float t = Math.Clamp(appearance.BackdropDim, 0, 100) / 100f;
        Color dark = Color.FromArgb((int)Math.Round(t * 255), 0, 0, 0);

        RectangleF rect = KarartmaKaresi(block, scale);
        float side = rect.Width;

        // Masaüstünde fotoğraf çizimi bu kipi zaten açmış oluyor; taşıma ekranının
        // ayrı yüzeyinde de aynı olsun ki karartma piksel piksel aynı çıksın.
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float softness = 1f - t;
        if (softness <= 0f)
        {
            using var solid = new SolidBrush(dark);
            g.FillRectangle(solid, rect);
            return;
        }

        // Odak 1'e yaklaştıkça koyu bölge kenara kadar uzanır, geçiş keskinleşir.
        float focus = 1f - ((1f - BackdropMinFocus) * softness);

        using GraphicsPath path = RoundedRectangle(rect, side * BackdropMaxCorner * softness);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(rect.X + (side / 2f), rect.Y + (side / 2f)),
            CenterColor = dark,
            SurroundColors = new[] { Color.FromArgb(0, 0, 0, 0) },
            FocusScales = new PointF(focus, focus),
        };

        g.FillPath(brush, path);
    }

    /// <summary>
    /// Karartma karesi: takvim bloğunu ortalar, bloktan her yana
    /// <see cref="BackdropPad"/> × ölçek taşar. Blok neredeyse kare (7 sütun ×
    /// 8 satır); uzun kenara göre tam kare yapılır.
    /// </summary>
    private static RectangleF KarartmaKaresi(RectangleF block, float scale)
    {
        float side = Math.Max(block.Width, block.Height) + (2f * BackdropPad * scale);
        return new RectangleF(
            block.X + ((block.Width - side) / 2f),
            block.Y + ((block.Height - side) / 2f),
            side,
            side);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        float d = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();

        // Çok küçük yayları GDI+ çizemiyor; o kadar küçük köşe zaten düz görünür.
        if (d < 1f)
        {
            path.AddRectangle(rect);
            return path;
        }

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
