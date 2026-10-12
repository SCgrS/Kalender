using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Kalender.Core;
using Kalender.Settings;

namespace Kalender.Rendering;

/// <summary>
/// Zemini (düz renk ya da fotoğraf) çizer. Fotoğraflar dosya kilitlenmesin
/// diye belleğe okunur ve yolu + değişme zamanı ile ön bellekte tutulur. Her
/// ekranın kendi fotoğrafı olabildiği için ön bellekte birden çok fotoğraf durur;
/// masaüstü katmanı bütün ekranları çizdikten sonra kullanılmayanları atar.
/// </summary>
internal sealed class BackgroundPainter : IDisposable
{
    /// <summary>EXIF yön etiketi (Orientation).</summary>
    private const int YonEtiketi = 0x0112;

    /// <summary>
    /// Ön bellekte en çok bu kadar fotoğraf durur. Kullanılmayanları atan
    /// çağrının gelmediği bir çizicide de (yerleştirme ekranı) bellek sınırlı kalsın.
    /// </summary>
    private const int EnCokFotograf = 8;

    /// <summary>
    /// Yol + değişme zamanı + boyut -> çözülmüş fotoğraf. Önceden tek
    /// fotoğraf tutuluyordu: iki ekranda farklı fotoğraf varken her çizim
    /// turunda ikisi de diskten yeniden okunup çözülüyordu.
    /// </summary>
    private readonly Dictionary<string, Image> _onbellek = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Son <see cref="KullanilmayanlariAt"/> çağrısından beri çizilen fotoğrafların anahtarları.</summary>
    private readonly HashSet<string> _kullanilanlar = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Açılamadığı günlüğe yazılmış fotoğraflar; aynı hata her çizimde yinelenmesin.</summary>
    private readonly HashSet<string> _bildirilenler = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bu ekranın zemin fotoğrafı: fotoğraf kipinde değilse, yol boşsa ya da
    /// dosya açılamıyorsa null. Çizici temayı buna göre seçer; fotoğraf
    /// açılamayınca zemin düz renkte kalır, yazı da düz zemine göre çizilir.
    /// </summary>
    public Image? Fotograf(AppearanceSettings appearance, string? photoPath)
    {
        if (appearance.BackgroundMode != BackgroundMode.Photo || string.IsNullOrWhiteSpace(photoPath))
        {
            return null;
        }

        return Load(photoPath);
    }

    /// <summary>Zemini düz renkle doldurur; fotoğraf varsa üstüne yerleştirir.</summary>
    /// <param name="image"><see cref="Fotograf"/>'ın döndürdüğü fotoğraf ya da null.</param>
    public void Paint(Graphics g, Rectangle area, Theme theme, Image? image, PhotoFit fit)
    {
        using (var brush = new SolidBrush(theme.Background))
        {
            g.FillRectangle(brush, area);
        }

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
                // Boyut açıkça veriliyor: DrawImageUnscaled resmi fotoğrafın
                // DPI'ına göre büyütüp küçültüyordu (72 DPI'lık fotoğraf 96
                // DPI'lık yüzeyde 1,33 kat), konum ise piksel boyutuyla
                // hesaplandığı için resim ortalanmıyordu. Döşe'nin doku fırçası
                // DPI'a bakmıyor, piksel boyutuyla çiziyor.
                g.DrawImage(image, new Rectangle(
                    area.X + ((area.Width - image.Width) / 2),
                    area.Y + ((area.Height - image.Height) / 2),
                    image.Width,
                    image.Height));
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
                Bildir(path, $"Zemin fotoğrafı bulunamadı, zemin düz renkle çiziliyor: {path}", null);
                return null;
            }

            string key = $"{info.FullName}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
            if (_onbellek.TryGetValue(key, out Image? cached))
            {
                _kullanilanlar.Add(key);
                return cached;
            }

            // Dosyayı belleğe alıp öyle açıyoruz; Image.FromFile dosyayı kilitler
            // ve kullanıcı fotoğrafı silemez ya da değiştiremez hâle gelir.
            byte[] bytes = File.ReadAllBytes(path);
            var stream = new MemoryStream(bytes, writable: false);
            var image = Image.FromStream(stream);

            try
            {
                YonuDuzelt(image);
            }
            catch
            {
                image.Dispose();
                throw;
            }

            if (_onbellek.Count >= EnCokFotograf)
            {
                HepsiniAt();
            }

            _onbellek[key] = image;
            _kullanilanlar.Add(key);
            _bildirilenler.Remove(path);
            return image;
        }
        catch (Exception ex)
        {
            Bildir(path, $"Zemin fotoğrafı açılamadı, zemin düz renkle çiziliyor: {path}", ex);
            return null;
        }
    }

    /// <summary>
    /// Fotoğrafın EXIF yön etiketini uygular. Telefonlar ve fotoğraf makineleri
    /// pikselleri algılayıcının yönünde saklayıp doğru yönü bu etiketle
    /// bildiriyor; GDI+ etiketi kendiliğinden uygulamıyor. Önceden dikey
    /// çekilmiş fotoğraf yan yatmış, ters tutularak çekilen baş aşağı
    /// çiziliyordu. Etiket uygulandıktan sonra kaldırılır; ön bellekte döndürülmüş
    /// hâli durur.
    /// </summary>
    private static void YonuDuzelt(Image image)
    {
        if (Array.IndexOf(image.PropertyIdList, YonEtiketi) < 0)
        {
            return;
        }

        PropertyItem? etiket = image.GetPropertyItem(YonEtiketi);
        RotateFlipType donus = etiket?.Value is { Length: >= 2 } deger
            ? BitConverter.ToUInt16(deger, 0) switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone,
            }
            : RotateFlipType.RotateNoneFlipNone;

        if (donus != RotateFlipType.RotateNoneFlipNone)
        {
            image.RotateFlip(donus);
        }

        image.RemovePropertyItem(YonEtiketi);
    }

    /// <summary>
    /// Fotoğrafın açılamadığını günlüğe bir kez, hata düzeyinde yazar. Önceden
    /// yalnız uyarı düzeyinde yazılıyordu (olağan düzey hata olduğu için hiç
    /// görünmüyordu), dosyanın bulunamaması hiç yazılmıyordu. Aynı yol yeniden
    /// açılabilene dek bir daha yazılmaz; yoksa her çizimde yinelenirdi.
    /// </summary>
    private void Bildir(string path, string message, Exception? ex)
    {
        if (_bildirilenler.Add(path))
        {
            Log.Error(message, ex);
        }
    }

    /// <summary>
    /// Son çağrıdan beri hiçbir çizimde kullanılmayan fotoğrafları bellekten
    /// atar. Masaüstü katmanı bütün ekranları çizdikten sonra çağırır; ön bellekte
    /// yalnız ekranlarda o an duran fotoğraflar kalır.
    /// </summary>
    public void KullanilmayanlariAt()
    {
        foreach (string key in _onbellek.Keys.Where(k => !_kullanilanlar.Contains(k)).ToList())
        {
            _onbellek[key].Dispose();
            _onbellek.Remove(key);
        }

        _kullanilanlar.Clear();
    }

    private void HepsiniAt()
    {
        foreach (Image image in _onbellek.Values)
        {
            image.Dispose();
        }

        _onbellek.Clear();
        _kullanilanlar.Clear();
    }

    public void Dispose() => HepsiniAt();
}
