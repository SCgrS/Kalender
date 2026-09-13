using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Kalender.Core;
using Microsoft.Win32;

namespace Kalender.Ui;

/// <summary>
/// Ayarlar penceresinin aydınlık/karanlık renk takımı.
///
/// Yalnızca fırça değerleri değişir; denetim şablonları (Denetimler.xaml)
/// bu fırçaları DynamicResource ile aldığı için geçiş anında olur, pencereyi
/// yeniden kurmaya gerek kalmaz. Renkler Windows 11 Ayarlar uygulamasının
/// tonlarına yakın tutuldu: kenar çubuğu geride, kartlar önde durur.
/// </summary>
internal static class UiTema
{
    /// <summary>Windows 10 20H1 ve sonrası: başlık çubuğunu karanlık boyar.</summary>
    private const int DwmwaUseImmersiveDarkMode = 20;

    /// <summary>Aynı ayarın 20H1 öncesi yapılardaki (belgelenmemiş) numarası.</summary>
    private const int DwmwaUseImmersiveDarkModeEski = 19;

    /// <summary>Başlık çubuğunu etkin ya da etkin değil olarak yeniden çizdiren ileti.</summary>
    private const int WmNcActivate = 0x0086;

    /// <summary>Windows 11'in ilk yapı numarası.</summary>
    private const int Windows11Yapisi = 22000;

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
            Set(r, "NavZeminFirca", "#1C1C1C");
            Set(r, "ZeminFirca", "#222222");
            Set(r, "YuzeyFirca", "#2B2B2B");
            Set(r, "AcilirFirca", "#2C2C2C");
            Set(r, "AltBarFirca", "#222222");
            Set(r, "YaziFirca", "#F3F3F3");
            Set(r, "SolukYaziFirca", "#BDBDBD");
            Set(r, "KenarFirca", "#383838");
            Set(r, "KenarGucluFirca", "#555555");
            Set(r, "DenetimFirca", "#373737");
            Set(r, "DenetimUstuFirca", "#3E3E3E");
            Set(r, "DenetimBasiliFirca", "#313131");
            Set(r, "GirdiFirca", "#1F1F1F");
            Set(r, "VurguFirca", "#60CDFF");
            Set(r, "VurguUstuFirca", "#5BBCEB");
            Set(r, "VurguBasiliFirca", "#55AAD4");
            Set(r, "VurguYaziFirca", "#000000");
            Set(r, "NavUstuFirca", "#0DFFFFFF");
            Set(r, "NavSeciliFirca", "#15FFFFFF");
            Set(r, "AnahtarZeminFirca", "#1A000000");
            Set(r, "AnahtarKenarFirca", "#9E9E9E");
            Set(r, "OrnekKenarFirca", "#40FFFFFF");
            Set(r, "KaydiriciFirca", "#7A7A7A");
            Set(r, "UyariZeminFirca", "#3D3317");
            Set(r, "UyariKenarFirca", "#55461D");
            Set(r, "UyariSimgeFirca", "#FCE100");
            Kabartma(r, "DenetimKenarFirca", "#18FFFFFF", "#12FFFFFF");
            Kabartma(r, "VurguKenarFirca", "#23000000", "#14FFFFFF");
        }
        else
        {
            Set(r, "NavZeminFirca", "#EDEDED");
            Set(r, "ZeminFirca", "#F7F7F7");
            Set(r, "YuzeyFirca", "#FFFFFF");
            Set(r, "AcilirFirca", "#FCFCFC");
            Set(r, "AltBarFirca", "#F7F7F7");
            Set(r, "YaziFirca", "#1B1B1B");
            Set(r, "SolukYaziFirca", "#5F5F5F");
            Set(r, "KenarFirca", "#E3E3E3");
            Set(r, "KenarGucluFirca", "#C4C4C4");
            Set(r, "DenetimFirca", "#FBFBFB");
            Set(r, "DenetimUstuFirca", "#F3F3F3");
            Set(r, "DenetimBasiliFirca", "#EBEBEB");
            Set(r, "GirdiFirca", "#FFFFFF");
            Set(r, "VurguFirca", "#005FB8");
            Set(r, "VurguUstuFirca", "#1A6FC0");
            Set(r, "VurguBasiliFirca", "#3380C7");
            Set(r, "VurguYaziFirca", "#FFFFFF");
            Set(r, "NavUstuFirca", "#09000000");
            Set(r, "NavSeciliFirca", "#0F000000");
            Set(r, "AnahtarZeminFirca", "#06000000");
            Set(r, "AnahtarKenarFirca", "#858585");
            Set(r, "OrnekKenarFirca", "#26000000");
            Set(r, "KaydiriciFirca", "#A3A3A3");
            Set(r, "UyariZeminFirca", "#FFF4CE");
            Set(r, "UyariKenarFirca", "#F1DFA6");
            Set(r, "UyariSimgeFirca", "#9D5D00");
            Kabartma(r, "DenetimKenarFirca", "#29000000", "#0F000000");
            Kabartma(r, "VurguKenarFirca", "#66000000", "#14FFFFFF");
        }
    }

    /// <summary>
    /// Pencerenin başlık çubuğunu temaya uydurur. Pencere tanıtıcısı henüz
    /// yoksa (SourceInitialized'dan önce) ya da Windows bu ayarı tanımıyorsa
    /// sessizce geçer; en kötü durumda başlık çubuğu aydınlık kalır.
    /// </summary>
    public static void BaslikCubugunuBoya(Window pencere, bool karanlik)
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(pencere).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            int deger = karanlik ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref deger, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeEski, ref deger, sizeof(int));
            }

            // Windows 10 görünür pencerenin başlığını bu ayar değişince hemen
            // yeniden çizmiyor; pencere odak değiştirene dek eski renk kalıyor.
            // Başlık önce öteki, sonra asıl etkinlik durumuyla çizdirilir.
            // Windows 11 kendisi çiziyor; ilk açılışta da pencere henüz görünmüyor.
            if (pencere.IsVisible && Environment.OSVersion.Version.Build < Windows11Yapisi)
            {
                nint etkin = pencere.IsActive ? 1 : 0;
                SendMessage(hwnd, WmNcActivate, 1 - etkin, 0);
                SendMessage(hwnd, WmNcActivate, etkin, 0);
            }
        }
        catch (Exception ex)
        {
            // Başlık rengi süs; pencereyi asla bozmamalı.
            Log.Warn("Başlık çubuğu teması uygulanamadı.", ex);
        }
    }

    private static void Set(ResourceDictionary sozluk, string anahtar, string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        sozluk[anahtar] = brush;
    }

    /// <summary>
    /// Windows 11 denetimlerindeki kabartma kenarı: kenarın en alttaki bir
    /// pikseli (<paramref name="alt"/>) ötekilerden (<paramref name="ust"/>)
    /// farklı tondadır; düğme yüzeyden hafifçe kalkık görünür.
    /// </summary>
    private static void Kabartma(ResourceDictionary sozluk, string anahtar, string alt, string ust)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 3),
            // Mutlak ölçüde "alt kenar" yazılamadığı için degrade ters çevrilir.
            RelativeTransform = new ScaleTransform(1, -1, 0.5, 0.5),
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(alt), 0.33));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(ust), 1.0));
        brush.Freeze();
        sozluk[anahtar] = brush;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
