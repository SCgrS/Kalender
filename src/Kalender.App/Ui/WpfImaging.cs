using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kalender.Ui;

/// <summary>
/// GDI+ ile ürettiğimiz görüntüleri WPF'in gösterebileceği biçime çevirir.
/// Piksel verisi kopyalanır; böylece kaynak bitmap serbest bırakılabilir ve
/// arkada GDI tanıtıcısı kalmaz.
/// </summary>
internal static class WpfImaging
{
    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(
            rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        try
        {
            var target = new WriteableBitmap(
                bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Pbgra32, null);

            target.WritePixels(
                new System.Windows.Int32Rect(0, 0, bitmap.Width, bitmap.Height),
                data.Scan0,
                data.Stride * bitmap.Height,
                data.Stride);

            target.Freeze();
            return target;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>System.Drawing rengini WPF rengine çevirir.</summary>
    public static System.Windows.Media.Color ToMedia(System.Drawing.Color color)
        => System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>WPF rengini System.Drawing rengine çevirir.</summary>
    public static System.Drawing.Color ToDrawing(System.Windows.Media.Color color)
        => System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
}
