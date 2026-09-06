using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Kalender.Ui;

/// <summary>
/// Ayarlar penceresinin aydınlık/karanlık renk takımı.
///
/// Yalnızca fırça değerleri değişir; denetim şablonları (Denetimler.xaml)
/// bu fırçaları DynamicResource ile aldığı için geçiş anında olur, pencereyi
/// yeniden kurmaya gerek kalmaz.
/// </summary>
internal static class UiTema
{
    /// <summary>Windows'un uygulama teması karanlık mı? Okunamazsa aydınlık kabul edilir.</summary>
    public static bool WindowsKaranlikMi()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1);

            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Renkleri pencereye uygular.</summary>
    public static void Uygula(FrameworkElement hedef, bool karanlik)
    {
        ResourceDictionary r = hedef.Resources;

        if (karanlik)
        {
            Set(r, "ZeminFirca", "#1B1E23");
            Set(r, "YuzeyFirca", "#23272E");
            Set(r, "AltBarFirca", "#181B20");
            Set(r, "YaziFirca", "#E7E9EC");
            Set(r, "SolukYaziFirca", "#A2A9B4");
            Set(r, "KenarFirca", "#343A43");
            Set(r, "KenarGucluFirca", "#4A5262");
            Set(r, "DenetimFirca", "#2C313A");
            Set(r, "DenetimUstuFirca", "#363D48");
            Set(r, "DenetimBasiliFirca", "#434C59");
            Set(r, "GirdiFirca", "#171A1F");
            Set(r, "VurguFirca", "#4C8DFF");
            Set(r, "KaydiriciFirca", "#4A5262");
            Set(r, "UyariZeminFirca", "#241E0E");
            Set(r, "UyariKenarFirca", "#5A4718");
        }
        else
        {
            Set(r, "ZeminFirca", "#F5F6F8");
            Set(r, "YuzeyFirca", "#FFFFFF");
            Set(r, "AltBarFirca", "#ECEEF1");
            Set(r, "YaziFirca", "#1A1D21");
            Set(r, "SolukYaziFirca", "#5C636E");
            Set(r, "KenarFirca", "#DADDE2");
            Set(r, "KenarGucluFirca", "#B6BCC5");
            Set(r, "DenetimFirca", "#FFFFFF");
            Set(r, "DenetimUstuFirca", "#EDF1F7");
            Set(r, "DenetimBasiliFirca", "#DFE5EE");
            Set(r, "GirdiFirca", "#FFFFFF");
            Set(r, "VurguFirca", "#2C6FE0");
            Set(r, "KaydiriciFirca", "#C2C7CF");
            Set(r, "UyariZeminFirca", "#FFF6E2");
            Set(r, "UyariKenarFirca", "#E4C489");
        }
    }

    private static void Set(ResourceDictionary sozluk, string anahtar, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        sozluk[anahtar] = brush;
    }
}
