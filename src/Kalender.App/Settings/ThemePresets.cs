namespace Kalender.Settings;

/// <summary>Hazır zemin rengi.</summary>
/// <param name="Id">Ayar dosyasında saklanan kimlik.</param>
/// <param name="Name">Ayarlar ekranında görünen Türkçe ad.</param>
/// <param name="Color">Zemin rengi (#RRGGBB).</param>
internal sealed record ThemePreset(string Id, string Name, string Color);

/// <summary>
/// Uygulamanın kendi paleti. Koyudan açığa doğru sıralıdır; kullanıcı
/// bunlardan birini seçebileceği gibi renk çarkından kendi rengini de verebilir.
/// </summary>
internal static class ThemePresets
{
    public static IReadOnlyList<ThemePreset> All { get; } = new[]
    {
        new ThemePreset("murekkep", "Mürekkep", "#141A22"),
        new ThemePreset("gece-mavisi", "Gece Mavisi", "#1E2A38"),
        new ThemePreset("komur", "Kömür", "#2E3238"),
        new ThemePreset("koyu-zeytin", "Koyu Zeytin", "#2C3A30"),
        new ThemePreset("tarcin", "Tarçın", "#4A3A33"),
        new ThemePreset("sis-grisi", "Sis Grisi", "#8A9099"),
        new ThemePreset("kum", "Kum", "#D9CFC1"),
        new ThemePreset("kagit", "Kağıt", "#F2F0EC"),
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
