using System.Drawing;
using Kalender.Calendar;
using Kalender.Desktop;
using Kalender.Interop;

namespace Kalender.Settings;

/// <summary>
/// Bir monitör ilk kez görüldüğünde (ya da ayarlarda "Varsayılana dön"e
/// basılınca) takvimin nereye konacağını hesaplar.
///
/// Kural (kullanıcının seçtiği yer, 2026-09-30): takvim ekranın sağ alt
/// köşesinde durur. Sağında bir masaüstü simgesi sütunu (örneğin bir klasör)
/// boş kalır; altında görev çubuğuyla arasında takvimin boyutuyla orantılı
/// bir boşluk bırakılır. Ölçüler her ekranın kendi çalışma alanından (görev
/// çubuğu hariç), DPI'ından ve simge ızgarasından hesaplanır. 2560 × 1440,
/// %100 ölçek ve 48 piksellik görev çubuğunda takvim sağ kenardan 85, görev
/// çubuğundan 38 piksel içeride durur.
/// </summary>
internal static class PlacementDefaults
{
    /// <summary>Ölçek 1.0 iken takvimle görev çubuğu arasındaki boşluk (piksel).</summary>
    private const float TaskbarGap = 38f;

    public static MonitorPlacement For(MonitorTarget monitor)
    {
        var placement = new MonitorPlacement();

        float scale = CalendarScale.For(monitor.Bounds.Size, placement.ScalePercent);
        SizeF block = CalendarLayout.MeasureSize(scale);

        // Görev çubuğu olmayan ekranda çalışma alanı ekranın kendisidir.
        Rectangle work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;

        double right = LastIconColumnStart(work, monitor.Dpi);
        double bottom = work.Bottom - (TaskbarGap * scale);

        double width = Math.Max(monitor.Bounds.Width, 1);
        double height = Math.Max(monitor.Bounds.Height, 1);

        double x = right - block.Width - monitor.Bounds.X;
        double y = bottom - block.Height - monitor.Bounds.Y;

        placement.XRatio = Math.Clamp(x / width, 0.01, 0.99);
        placement.YRatio = Math.Clamp(y / height, 0.01, 0.99);

        return placement;
    }

    /// <summary>
    /// Çalışma alanına tam sığan en sağdaki simge sütununun başladığı yer
    /// (sanal masaüstü koordinatı). Takvimin sağ kenarı buraya kadar uzanır.
    /// Izgara sanal masaüstünün sol kenarından başladığı için sağda yarım bir
    /// sütun kalabilir; o da boş kalır.
    /// </summary>
    private static double LastIconColumnStart(Rectangle work, uint dpi)
    {
        int column = Math.Max(1, MasaustuSimgeIzgarasi.SutunGenisligi(dpi));
        int origin = MasaustuSimgeIzgarasi.Baslangic;

        int fullColumns = (work.Right - origin) / column;
        double start = origin + ((fullColumns - 1) * (double)column);

        // Ekranın solunda başlayan tuhaf bir ızgarada bile en az bir sütun boş kalsın.
        return Math.Min(start, work.Right - column);
    }
}
