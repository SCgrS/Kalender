using Kalender.Core;

namespace Kalender.Interop;

/// <summary>Takvim penceresinin altına yerleşeceği masaüstü katmanı.</summary>
/// <param name="Handle">Ana pencere tanıtıcısı.</param>
/// <param name="Kind">Bulunan katmanın türü; günlüğe ve yedek plana karar vermeye yarar.</param>
internal sealed record DesktopHost(IntPtr Handle, DesktopHostKind Kind);

internal enum DesktopHostKind
{
    /// <summary>Simgelerin arkasındaki WorkerW bulundu. İstenen ve olağan durum.</summary>
    WorkerW,

    /// <summary>WorkerW bulunamadı; Progman altına, z sırasının en altına yerleşiyoruz.</summary>
    Progman,
}

/// <summary>
/// Windows kabuğunda duvar kağıdının çizildiği pencereyi bulur.
///
/// Yöntem: Progman penceresine belgesiz 0x052C mesajı gönderilir; kabuk buna
/// karşılık masaüstü simgelerinin (SHELLDLL_DefView) arkasında duran bir WorkerW
/// penceresi üretir. Kendi çizim penceremizi bunun çocuğu yaparsak takvim
/// duvar kağıdının bir parçası gibi, simgelerin altında görünür.
/// Lively Wallpaper ve Wallpaper Engine de aynı yolu kullanır.
/// </summary>
internal static class WorkerWLocator
{
    /// <summary>
    /// Kabuğun WorkerW üretmesi için denenen parametre çiftleri. Windows sürümleri
    /// arasında hangisinin işe yaradığı değiştiği için sırayla denenir.
    /// </summary>
    private static readonly (int W, int L)[] SpawnArguments =
    {
        (0, 0),
        (0x0D, 0x01),
        (0x0D, 0x00),
    };

    public static DesktopHost? Find()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            progman = NativeMethods.FindWindow("Progman", "Program Manager");
        }

        if (progman == IntPtr.Zero)
        {
            Log.Warn("Progman penceresi bulunamadı; masaüstü katmanı kurulamıyor.");
            return null;
        }

        foreach ((int w, int l) in SpawnArguments)
        {
            NativeMethods.SendMessageTimeout(
                progman,
                NativeMethods.WM_SPAWN_WORKERW,
                new IntPtr(w),
                new IntPtr(l),
                NativeMethods.SMTO_NORMAL | NativeMethods.SMTO_ABORTIFHUNG,
                1000,
                out _);

            IntPtr worker = FindWorkerBehindIcons();
            if (worker != IntPtr.Zero)
            {
                Log.Info($"Masaüstü katmanı bulundu (WorkerW, spawn {w:X}/{l:X}).");
                return new DesktopHost(worker, DesktopHostKind.WorkerW);
            }
        }

        // Bazı Windows 10 yapılarında ve üçüncü parti kabuklarda WorkerW hiç
        // üretilmez. O durumda Progman'ın çocuğu oluruz ve z sırasının en altına
        // ineriz; simgeler yine üstte kalır.
        Log.Warn("WorkerW bulunamadı; Progman yedek planına geçiliyor.");
        return new DesktopHost(progman, DesktopHostKind.Progman);
    }

    /// <summary>
    /// Masaüstü simgelerini barındıran pencereyi bulur ve z sırasında ondan
    /// hemen sonra gelen WorkerW'yi döndürür. Aradığımız pencere budur:
    /// simgelerin arkasında, duvar kağıdının önünde.
    /// </summary>
    private static IntPtr FindWorkerBehindIcons()
    {
        IntPtr result = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            IntPtr defView = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                return true;
            }

            IntPtr next = NativeMethods.FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            if (next != IntPtr.Zero)
            {
                result = next;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        if (result != IntPtr.Zero && NativeMethods.IsWindow(result))
        {
            return result;
        }

        return IntPtr.Zero;
    }
}
