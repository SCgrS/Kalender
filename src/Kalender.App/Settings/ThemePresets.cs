namespace Kalender.Settings;

/// <summary>Hazır zemin rengi.</summary>
/// <param name="Id">Ayar dosyasında saklanan kimlik.</param>
/// <param name="Name">Ayarlar ekranında görünen Türkçe ad.</param>
/// <param name="Color">Zemin rengi (#RRGGBB).</param>
internal sealed record ThemePreset(string Id, string Name, string Color);

/// <summary>
/// Uygulamanın kendi paleti. Koyudan açığa doğru sıralıdır; kullanıcı
/// bunlardan birini seçebileceği gibi renk çarkından kendi rengini de verebilir.
///
/// 2026-09-30'da kullanıcının isteğiyle Çam Yeşili, Tarçın ve Sis Grisi
/// çıkarıldı; en başa düz siyah ve Mürekkep'ten koyu Kuzgun, Kum'un önüne de
/// ondan koyu Kum Taşı geldi. Çıkan bir rengi seçmiş olanın zemini değişmez:
/// renk ayarda onaltılık olarak durur, ayarlarda "kendi rengi" görünür.
/// </summary>
internal static class ThemePresets
{
    public static IReadOnlyList<ThemePreset> All { get; } = new[]
    {
        new ThemePreset("siyah", "Siyah", "#000000"),
        new ThemePreset("kuzgun", "Kuzgun", "#0B0E13"),
        new ThemePreset("murekkep", "Mürekkep", "#141A22"),
        new ThemePreset("gece-mavisi", "Gece Mavisi", "#1E2A38"),
        new ThemePreset("komur", "Kömür", "#2E3238"),
        // Kum'dan koyu bej. Daha koyusu tatil kırmızısını (#C02B2B) 3:1
        // karşıtlığın altına düşürüyordu; bu tonda 3,1:1.
        new ThemePreset("kum-tasi", "Kum Taşı", "#C8BBA8"),
        new ThemePreset("kum", "Kum", "#D9CFC1"),
        new ThemePreset("kagit", "Kâğıt", "#F2F0EC"),
    };

    public static ThemePreset? ById(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
    }
}
