using System.Drawing;
using Kalender.Calendar;
using Kalender.Interop;

namespace Kalender.Settings;

/// <summary>
/// Bir monitör ilk kez görüldüğünde takvimin nereye konacağını hesaplar.
/// Referans yerleşim: sağ kenardan genişliğin %6'sı kadar içeride,
/// dikeyde ekranın %55'i hizasında ortalanmış.
/// </summary>
internal static class PlacementDefaults
{
    private const double RightMarginRatio = 0.06;
    private const double VerticalCenterRatio = 0.55;

    public static MonitorPlacement For(MonitorTarget monitor)
    {
        var placement = new MonitorPlacement();

        float scale = (float)monitor.DpiScale;
        SizeF block = CalendarLayout.MeasureSize(scale);

        double width = Math.Max(monitor.Bounds.Width, 1);
        double height = Math.Max(monitor.Bounds.Height, 1);

        double x = width - block.Width - (width * RightMarginRatio);
        double y = (height * VerticalCenterRatio) - (block.Height / 2.0);

        placement.XRatio = Math.Clamp(x / width, 0.01, 0.99);
        placement.YRatio = Math.Clamp(y / height, 0.01, 0.99);

        return placement;
    }
}
