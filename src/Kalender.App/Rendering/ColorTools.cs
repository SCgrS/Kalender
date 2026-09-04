using System.Drawing;
using System.Globalization;

namespace Kalender.Rendering;

/// <summary>Renk hesapları: ayrıştırma, parlaklık, karıştırma.</summary>
internal static class ColorTools
{
    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        string s = hex.Trim().TrimStart('#');

        try
        {
            if (s.Length == 6)
            {
                int r = int.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int g = int.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int b = int.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Color.FromArgb(255, r, g, b);
            }

            if (s.Length == 8)
            {
                int a = int.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int r = int.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int g = int.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int b = int.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Color.FromArgb(a, r, g, b);
            }

            if (s.Length == 3)
            {
                int r = int.Parse(new string(s[0], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int g = int.Parse(new string(s[1], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int b = int.Parse(new string(s[2], 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return Color.FromArgb(255, r, g, b);
            }
        }
        catch (FormatException)
        {
            // Bozuk değer: varsayılana düşülür.
        }

        return fallback;
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>WCAG bağıl parlaklığı (0 siyah, 1 beyaz).</summary>
    public static double RelativeLuminance(Color c)
    {
        static double Kanal(int v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Kanal(c.R)) + (0.7152 * Kanal(c.G)) + (0.0722 * Kanal(c.B));
    }

    /// <summary>Zemin açık mı? (Açıksa üstüne koyu yazı yazılır.)</summary>
    public static bool IsLight(Color background) => RelativeLuminance(background) > 0.42;

    /// <summary>İki renk arasında oranla karıştırır. amount=0 -> a, amount=1 -> b.</summary>
    public static Color Mix(Color a, Color b, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            255,
            (int)Math.Round(a.R + ((b.R - a.R) * amount)),
            (int)Math.Round(a.G + ((b.G - a.G) * amount)),
            (int)Math.Round(a.B + ((b.B - a.B) * amount)));
    }

    public static Color Lighten(Color c, double amount) => Mix(c, Color.White, amount);

    public static Color Darken(Color c, double amount) => Mix(c, Color.Black, amount);

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);
}
