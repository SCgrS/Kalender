namespace Kalender.Calendar;

/// <summary>
/// Ay ve gün adları elle yazıldı. Böylece işletim sisteminin bölge ayarı ne
/// olursa olsun takvim Türkçe görünür ve büyük harfe çevirirken "i/İ" gibi
/// Türkçeye özgü kurallarda hata olmaz.
/// </summary>
internal static class TurkishNames
{
    /// <summary>Ay adları, büyük harfle. Dizin 1-12.</summary>
    private static readonly string[] MonthsUpper =
    {
        string.Empty,
        "OCAK", "ŞUBAT", "MART", "NİSAN", "MAYIS", "HAZİRAN",
        "TEMMUZ", "AĞUSTOS", "EYLÜL", "EKİM", "KASIM", "ARALIK",
    };

    /// <summary>Ay adları, cümle içinde kullanılan biçimiyle. Dizin 1-12.</summary>
    private static readonly string[] MonthsTitle =
    {
        string.Empty,
        "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
        "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık",
    };

    /// <summary>
    /// Ay adları belirtme durumu ekiyle ("10 Ekim'i"). Belirli bir tarih
    /// bildiren ay adına gelen ek kesmeyle ayrılır ve ünlü uyumuna uyar; yazımda
    /// son ünsüz yumuşamaz (Ocak'ı, Aralık'ı). Dizin 1-12.
    /// </summary>
    private static readonly string[] MonthsAccusative =
    {
        string.Empty,
        "Ocak'ı", "Şubat'ı", "Mart'ı", "Nisan'ı", "Mayıs'ı", "Haziran'ı",
        "Temmuz'u", "Ağustos'u", "Eylül'ü", "Ekim'i", "Kasım'ı", "Aralık'ı",
    };

    /// <summary>Hafta Pazartesi başlar: P S Ç P C C P.</summary>
    public static readonly string[] WeekdayInitials = { "P", "S", "Ç", "P", "C", "C", "P" };

    public static string MonthNameUpper(int month) => MonthsUpper[Math.Clamp(month, 1, 12)];

    public static string MonthNameTitle(int month) => MonthsTitle[Math.Clamp(month, 1, 12)];

    /// <summary>Pazartesi 0, Pazar 6 olacak şekilde haftanın gün sırası.</summary>
    public static int WeekIndex(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>Sağ tık menüsündeki "10 Ekim'i" biçimi: gün ve ekli ay adı.</summary>
    public static string DayMonthAccusative(DateOnly date)
        => $"{date.Day} {MonthsAccusative[Math.Clamp(date.Month, 1, 12)]}";
}
