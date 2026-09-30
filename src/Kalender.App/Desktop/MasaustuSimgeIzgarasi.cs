using Kalender.Core;
using Kalender.Interop;

namespace Kalender.Desktop;

/// <summary>
/// Masaüstü simgelerinin dizildiği ızgara. Takvimin varsayılan yeri bunu
/// hesaba katar: sağında bir simge sütunu (örneğin bir klasör) boş kalmalı.
///
/// Simgeler, sanal masaüstünün sol kenarından başlayan eşit genişlikte
/// sütunlara dizilir ("Simgeleri kılavuza hizala"). Sütun genişliği
/// Windows'un simge aralığı ayarından ve masaüstünün simge boyutundan gelir;
/// %100 ölçekte orta boy simgelerle 75 piksel.
/// </summary>
internal static class MasaustuSimgeIzgarasi
{
    /// <summary>Hiçbir şey okunamazsa %100 ölçekteki varsayılan sütun genişliği.</summary>
    private const int VarsayilanSutun = 75;

    /// <summary>Explorer yanıt vermiyorsa en fazla bu kadar beklenir (milisaniye).</summary>
    private const uint YanitSuresi = 200;

    /// <summary>Izgaranın başladığı yatay konum: sanal masaüstünün sol kenarı (fiziksel piksel).</summary>
    public static int Baslangic => NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);

    /// <summary>
    /// Verilen DPI'daki sütun genişliği (fiziksel piksel). Masaüstü büyük
    /// simgelerdeyse sütun Windows'un genel simge aralığından geniş olur; bu
    /// yüzden masaüstü listesinin kendi aralığı da sorulur, büyüğü alınır.
    /// </summary>
    public static int SutunGenisligi(uint dpi)
    {
        int genel = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXICONSPACING, dpi);
        if (genel <= 0)
        {
            genel = (int)Math.Round(VarsayilanSutun * dpi / 96.0);
        }

        return Math.Max(genel, ListeAraligi(dpi));
    }

    /// <summary>
    /// Masaüstü listesinin kendi simge aralığı, verilen DPI'a çevrilmiş.
    /// Explorer yanıt vermiyorsa ya da liste bulunamazsa 0.
    /// </summary>
    private static int ListeAraligi(uint dpi)
    {
        try
        {
            IntPtr liste = MasaustuSimgeleri.Liste();
            if (liste == IntPtr.Zero)
            {
                return 0;
            }

            // İleti işaretçi taşımıyor; başka sürecin penceresine güvenle
            // gönderilir. Explorer takılıysa beklemeyiz.
            if (NativeMethods.SendMessageTimeout(liste, NativeMethods.LVM_GETITEMSPACING, IntPtr.Zero, IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG, YanitSuresi, out IntPtr sonuc) == IntPtr.Zero)
            {
                return 0;
            }

            int genislik = (int)(sonuc.ToInt64() & 0xFFFF);
            uint listeDpi = NativeMethods.GetDpiForWindow(liste);
            return listeDpi == 0 ? genislik : (int)Math.Round(genislik * (double)dpi / listeDpi);
        }
        catch (Exception ex)
        {
            Log.Debug("Masaüstü simge aralığı okunamadı: " + ex.Message);
            return 0;
        }
    }
}
