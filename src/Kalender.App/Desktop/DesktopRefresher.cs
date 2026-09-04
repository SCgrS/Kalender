using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Masaüstünü zorla yeniden boyatır.
///
/// Buna neden gerek var: çizim penceremiz yok olduğunda Windows kabuğu duvar
/// kağıdını kendiliğinden yeniden boyamıyor; ekranda bizim çizdiğimiz son
/// görüntü olduğu gibi kalıyor. Kalender kapandığında takvimin ekranda asılı
/// kalmaması için kabuğa açıkça "yeniden boya" demek gerekiyor.
/// </summary>
internal static class DesktopRefresher
{
    public static void ForceRedraw()
    {
        InvalidateShellWindows();
        RewriteWallpaperInPlace();
    }

    /// <summary>Masaüstünü çizen kabuk pencerelerini geçersiz kılar.</summary>
    private static void InvalidateShellWindows()
    {
        try
        {
            const uint flags = NativeMethods.RDW_INVALIDATE | NativeMethods.RDW_ERASE
                | NativeMethods.RDW_ALLCHILDREN | NativeMethods.RDW_UPDATENOW;

            IntPtr progman = NativeMethods.FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                NativeMethods.RedrawWindow(progman, IntPtr.Zero, IntPtr.Zero, flags);
            }

            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (NativeMethods.GetClassNameOf(hwnd) == "WorkerW"
                    && NativeMethods.IsWindowVisible(hwnd))
                {
                    NativeMethods.RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero, flags);
                }

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Log.Debug("Masaüstü pencereleri geçersiz kılınamadı: " + ex.Message);
        }
    }

    /// <summary>
    /// Kabuğa duvar kağıdını yeniden çizdirir. Okuduğumuz değerleri aynen geri
    /// yazıyoruz; kullanıcının ayarı değişmiyor, yalnızca kabuk boyamayı
    /// tazeliyor.
    /// </summary>
    private static void RewriteWallpaperInPlace()
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();

            wallpaper.SetBackgroundColor(wallpaper.GetBackgroundColor());

            uint count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = wallpaper.GetMonitorDevicePathAt(i);
                string current = wallpaper.GetWallpaper(id) ?? string.Empty;
                wallpaper.SetWallpaper(id, current);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kağıdı yeniden yazılamadı: " + ex.Message);
        }
    }
}
