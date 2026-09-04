using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using Kalender.Calendar;
using Kalender.Calendar.Holidays;
using Kalender.Interop;
using Kalender.Rendering;
using Kalender.Settings;

namespace Kalender.Core;

/// <summary>
/// Geliştirme ve doğrulama aracı: istenen ayın takvimini bir PNG dosyasına
/// çizer. Böylece "Ekim ayındaki tatiller kırmızı mı" gibi soruları sistem
/// saatini değiştirmeden yanıtlayabiliyoruz.
///
///   Kalender.exe --onizleme 2026-10 C:\gecici\ekim.png
///
/// Arayüz açmaz, masaüstüne dokunmaz.
/// </summary>
internal static class PreviewCommand
{
    public const string Switch = "--onizleme";

    public static async Task<bool> TryRunAsync(string[] args)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        if (index + 2 >= args.Length)
        {
            Log.Error($"Kullanım: Kalender.exe {Switch} yyyy-MM cikti.png");
            return true;
        }

        string monthText = args[index + 1];
        string outputPath = args[index + 2];

        try
        {
            if (!DateOnly.TryParseExact(monthText + "-15", "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly today))
            {
                Log.Error($"Ay okunamadı: {monthText} (beklenen biçim yyyy-MM)");
                return true;
            }

            var settings = new SettingsStore();
            settings.Load();

            using var holidays = new HolidayService();
            holidays.LoadCache();

            if (holidays.Count == 0)
            {
                await holidays.RefreshAsync(force: true).ConfigureAwait(false);
            }

            // Önizleme yalnızca tatilleri gösterir; Google verisi için oturum gerekir.
            var data = new CalendarDataService(holidays, provider: null, settings);
            data.SetToday(today);

            MonitorTarget monitor = MonitorEnumerator.All().FirstOrDefault(m => m.IsPrimary)
                ?? MonitorEnumerator.All().First();

            MonitorPlacement placement = settings.Current.Monitors.TryGetValue(monitor.StableId, out var saved)
                ? saved
                : PlacementDefaults.For(monitor);

            using var renderer = new DesktopSurfaceRenderer();
            using DesktopSurface surface = renderer.Render(
                monitor.Bounds.Size, settings.Current, placement, data.Current, today, drawCalendar: true);

            string? folder = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            surface.Image.Save(outputPath, ImageFormat.Png);

            Log.Info($"Önizleme yazıldı: {outputPath} ({monthText}, {holidays.Count} tatil günü biliniyor)");
        }
        catch (Exception ex)
        {
            Log.Error("Önizleme üretilemedi.", ex);
        }

        return true;
    }
}
