using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using Kalender.Core;
using Kalender.Settings;

namespace Kalender.Rendering;

/// <summary>
/// Zemini (düz renk ya da fotoğraf) çizer. Fotoğraflar dosya kilitlenmesin
/// diye belleğe okunur ve yolu + değişme zamanı ile önbelleklenir.
/// </summary>
internal sealed class BackgroundPainter : IDisposable
{
    private string? _cachedKey;
    private Image? _cachedImage;

    public void Paint(Graphics g, Rectangle area, AppearanceSettings appearance, Theme theme,
        string? photoPath, PhotoFit fit)
    {
        using (var brush = new SolidBrush(theme.Background))
        {
            g.FillRectangle(brush, area);
        }

        if (appearance.BackgroundMode != BackgroundMode.Photo || string.IsNullOrWhiteSpace(photoPath))
        {
            return;
        }

        Image? image = Load(photoPath);
        if (image is null)
        {
            return;
        }

        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        switch (fit)
        {
            case PhotoFit.Fill:
                DrawCovering(g, area, image);
                break;
            case PhotoFit.Fit:
                DrawContained(g, area, image);
                break;
            case PhotoFit.Center:
                g.DrawImageUnscaled(image,
                    area.X + ((area.Width - image.Width) / 2),
                    area.Y + ((area.Height - image.Height) / 2));
                break;
            case PhotoFit.Tile:
                DrawTiled(g, area, image);
                break;
        }
    }

    private static void DrawCovering(Graphics g, Rectangle area, Image image)
    {
        double scale = Math.Max((double)area.Width / image.Width, (double)area.Height / image.Height);
        int w = (int)Math.Ceiling(image.Width * scale);
        int h = (int)Math.Ceiling(image.Height * scale);
        g.DrawImage(image, area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
    }

    private static void DrawContained(Graphics g, Rectangle area, Image image)
    {
        double scale = Math.Min((double)area.Width / image.Width, (double)area.Height / image.Height);
        int w = (int)Math.Round(image.Width * scale);
        int h = (int)Math.Round(image.Height * scale);
        g.DrawImage(image, area.X + ((area.Width - w) / 2), area.Y + ((area.Height - h) / 2), w, h);
    }

    private static void DrawTiled(Graphics g, Rectangle area, Image image)
    {
        using var brush = new TextureBrush(image, WrapMode.Tile);
        brush.TranslateTransform(area.X, area.Y);
        g.FillRectangle(brush, area);
    }

    private Image? Load(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            string key = $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            if (key == _cachedKey && _cachedImage is not null)
            {
                return _cachedImage;
            }

            // Dosyayı belleğe alıp öyle açıyoruz; Image.FromFile dosyayı kilitler
            // ve kullanıcı fotoğrafı silemez ya da değiştiremez hâle gelir.
            byte[] bytes = File.ReadAllBytes(path);
            var stream = new MemoryStream(bytes, writable: false);
            var image = Image.FromStream(stream);

            _cachedImage?.Dispose();
            _cachedImage = image;
            _cachedKey = key;
            return image;
        }
        catch (Exception ex)
        {
            Log.Warn($"Zemin fotoğrafı açılamadı: {path}", ex);
            return null;
        }
    }

    public void Dispose()
    {
        _cachedImage?.Dispose();
        _cachedImage = null;
        _cachedKey = null;
    }
}
