using System.Drawing;
using System.Runtime.InteropServices;
using Accessibility;
using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Masaüstü simgeleri. Takvim duvar kağıdının parçası olduğu için simgeler
/// hep onun üstünde durur; bir simgenin altında kalan ay düğmesi ya da takvim
/// alanı tıklamayı almamalı, tıklama simgeye gitmeli.
///
/// Noktada simge olup olmadığı masaüstü listesinin erişilebilirlik (MSAA)
/// arayüzüne sorulur: ekran okuyucuların da kullandığı, Windows'un kendi
/// yolu. Soru 0,1–0,4 ms sürüyor; yalnızca tıklama anında ve takvimin
/// üzerindeyken soruluyor.
/// </summary>
internal static class MasaustuSimgeleri
{
    private const uint OBJID_CLIENT = 0xFFFFFFFC;
    private const int ROLE_SYSTEM_LISTITEM = 0x22;

    private static readonly Guid IID_IAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint id, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAccessible? acc);

    /// <summary>Önbellekteki liste ve arayüzü; Explorer yeniden başlayınca liste değişir.</summary>
    private static IntPtr _liste;
    private static IAccessible? _erisim;

    /// <summary>
    /// Ekran koordinatındaki noktada bir masaüstü simgesi (klasör, kısayol)
    /// var mı? Emin olunamazsa false: takvim eskisi gibi davranır.
    /// </summary>
    public static bool Ustunde(Point ekranNoktasi)
    {
        try
        {
            IAccessible? erisim = Erisim();
            if (erisim is null)
            {
                return false;
            }

            object? cocuk = erisim.accHitTest(ekranNoktasi.X, ekranNoktasi.Y);

            // Simgeler listenin basit öğeleridir: 1'den başlayan kimlik gelir.
            // Boş masaüstünde listenin kendisi (0) ya da hiçbir şey (null) gelir.
            if (cocuk is int kimlik)
            {
                return kimlik > 0;
            }

            if (cocuk is IAccessible nesne)
            {
                try
                {
                    return nesne.get_accRole(0) is int rol && rol == ROLE_SYSTEM_LISTITEM;
                }
                finally
                {
                    Marshal.ReleaseComObject(nesne);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            // Explorer yeniden başladıysa arayüz geçersizdir; bir sonrakinde yeniden alınır.
            Log.Debug("Masaüstü simgesi denetlenemedi: " + ex.Message);
            Birak();
            return false;
        }
    }

    /// <summary>
    /// Masaüstü simgelerini tutan liste penceresi. SHELLDLL_DefView normalde
    /// Progman'ın çocuğudur; duvar kağıdı geçişinden sonra bir WorkerW'ye taşınabilir.
    /// </summary>
    public static IntPtr Liste()
    {
        IntPtr defView = NativeMethods.FindWindowEx(
            NativeMethods.FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defView == IntPtr.Zero)
        {
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                IntPtr aday = NativeMethods.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (aday == IntPtr.Zero)
                {
                    return true;
                }

                defView = aday;
                return false;
            }, IntPtr.Zero);
        }

        return defView == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    private static IAccessible? Erisim()
    {
        if (_erisim is not null && _liste != IntPtr.Zero && NativeMethods.IsWindow(_liste))
        {
            return _erisim;
        }

        Birak();

        IntPtr liste = Liste();
        if (liste == IntPtr.Zero)
        {
            return null;
        }

        Guid iid = IID_IAccessible;
        if (AccessibleObjectFromWindow(liste, OBJID_CLIENT, ref iid, out IAccessible? erisim) != 0 || erisim is null)
        {
            return null;
        }

        _liste = liste;
        _erisim = erisim;
        return erisim;
    }

    private static void Birak()
    {
        if (_erisim is not null)
        {
            try
            {
                Marshal.ReleaseComObject(_erisim);
            }
            catch (Exception)
            {
                // Zaten kopmuş bir bağlantı; bırakılacak bir şey kalmadı.
            }
        }

        _erisim = null;
        _liste = IntPtr.Zero;
    }
}
