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
}
