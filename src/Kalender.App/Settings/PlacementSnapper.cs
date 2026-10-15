using System.Drawing;
using Kalender.Rendering;

namespace Kalender.Settings;

/// <summary>Yapışmanın hangi hizaya olduğu; taşıma ekranında kılavuz çizgisi göstermek için.</summary>
[Flags]
internal enum SnapEdge
{
    None = 0,
    Left = 1,
    HorizontalCenter = 2,
    Right = 4,
    Top = 8,
    VerticalCenter = 16,
    Bottom = 32,
}

/// <summary>
/// Takvim sürüklenirken ekran kenarlarına ve ortasına yapışmasını sağlar.
/// Elle konumlandırmada göz kararı hizalamak zor; bu yüzden yakınına
/// gelindiğinde belirli hizalara çekiliyor.
/// </summary>
internal static class PlacementSnapper
{
    /// <summary>Yapışma mesafesi (fiziksel piksel).</summary>
    private const float Threshold = 28f;

    /// <summary>Kenar payı, ekran ölçüsünün oranı olarak.</summary>
    private const double EdgeMarginRatio = 0.06;

    /// <param name="scale">Takvimin ölçeği; ekran kenarındaki pay buna göre.</param>
    public static PointF Snap(PointF proposed, SizeF block, Size monitor, float scale, out SnapEdge edge)
    {
        edge = SnapEdge.None;

        float marginX = (float)(monitor.Width * EdgeMarginRatio);
        float marginY = (float)(monitor.Height * EdgeMarginRatio);

        // Ekranın kenarına en yakın yer: masaüstündeki çizimin takvimi tuttuğu
        // pay çizgisi. Kenara yaklaşan ya da kenarı aşan takvim bu çizgiye yapışır.
        float pay = DesktopSurfaceRenderer.KenarPayi(scale);

        float x = proposed.X;
        float y = proposed.Y;

        // Yatay hizalar: sol pay, orta, sağ pay; sonra ekran kenarları.
        float left = marginX;
        float center = (monitor.Width - block.Width) / 2f;
        float right = monitor.Width - block.Width - marginX;
        float solKenar = pay;
        float sagKenar = monitor.Width - block.Width - pay;

        if (Near(x, left))
        {
            x = left;
            edge |= SnapEdge.Left;
        }
        else if (Near(x, center))
        {
            x = center;
            edge |= SnapEdge.HorizontalCenter;
        }
        else if (Near(x, right))
        {
            x = right;
            edge |= SnapEdge.Right;
        }
        else if (x <= solKenar + Threshold)
        {
            x = solKenar;
            edge |= SnapEdge.Left;
        }
        else if (x >= sagKenar - Threshold)
        {
            x = sagKenar;
            edge |= SnapEdge.Right;
        }

        // Dikey hizalar: üst pay, orta, alt pay; sonra ekran kenarları.
        float top = marginY;
        float middle = (monitor.Height - block.Height) / 2f;
        float bottom = monitor.Height - block.Height - marginY;
        float ustKenar = pay;
        float altKenar = monitor.Height - block.Height - pay;

        if (Near(y, top))
        {
            y = top;
            edge |= SnapEdge.Top;
        }
        else if (Near(y, middle))
        {
            y = middle;
            edge |= SnapEdge.VerticalCenter;
        }
        else if (Near(y, bottom))
        {
            y = bottom;
            edge |= SnapEdge.Bottom;
        }
        else if (y <= ustKenar + Threshold)
        {
            y = ustKenar;
            edge |= SnapEdge.Top;
        }
        else if (y >= altKenar - Threshold)
        {
            y = altKenar;
            edge |= SnapEdge.Bottom;
        }

        // Her hâlükârda ekran içinde, masaüstündeki çizimle aynı kenar payıyla
        // kalsın (takvim ekrandan büyükse de).
        return DesktopSurfaceRenderer.KenarPayiIcinde(new PointF(x, y), block, monitor, scale);
    }

    private static bool Near(float value, float target) => Math.Abs(value - target) <= Threshold;
}
