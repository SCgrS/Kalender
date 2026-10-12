namespace Kalender.Google;

/// <summary>
/// Google Takvim'in web adresleri. Takvimin sağ tık menüsü bir günü ya da
/// ayı Google Takvim'de açarken bunları kullanır.
/// </summary>
internal static class GoogleLinks
{
    private const string Base = "https://calendar.google.com/calendar/u/0/r";

    /// <summary>Belirli bir günün gün görünümü.</summary>
    public static string Day(DateOnly date)
        => $"{Base}/day/{date.Year}/{date.Month}/{date.Day}";

    /// <summary>Belirli bir ayın ay görünümü.</summary>
    public static string Month(DateOnly month)
        => $"{Base}/month/{month.Year}/{month.Month}/1";

    /// <summary>
    /// Adresin Kalender'e bağlı hesapla açılmasını ister. Adresteki "u/0"
    /// tarayıcıda ilk oturum açılmış Google hesabıdır, bağlı hesap değil:
    /// tarayıcıda birden çok hesap açıkken öbür hesabın takvimi açılıyordu.
    /// "authuser" Google'a hangi hesabın açılacağını söylüyor. Hesap
    /// bilinmiyorsa adres olduğu gibi kalır.
    /// </summary>
    /// <param name="adres"><see cref="Day"/> ya da <see cref="Month"/> adresi.</param>
    /// <param name="hesap">Bağlı hesabın e-postası; bilinmiyorsa null.</param>
    public static string HesapIle(string adres, string? hesap)
        => string.IsNullOrWhiteSpace(hesap)
            ? adres
            : adres + (adres.Contains('?') ? "&" : "?") + "authuser=" + Uri.EscapeDataString(hesap.Trim());
}
