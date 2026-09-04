namespace Kalender.Calendar.Holidays;

/// <summary>Tatilin türü.</summary>
internal enum HolidayKind
{
    /// <summary>Tam gün resmi tatil. Gün kırmızı gösterilir.</summary>
    Full = 0,

    /// <summary>
    /// Yarım günlük tatil (arife). Şartname gereği özel bir renk almaz,
    /// normal gün gibi görünür; adı yalnızca gün kutusunda yazar.
    /// </summary>
    HalfDay = 1,
}

/// <summary>Bir güne düşen resmi tatil.</summary>
/// <param name="Date">Tatil günü.</param>
/// <param name="Name">Tatilin adı.</param>
/// <param name="Kind">Tam gün mü, yarım gün mü?</param>
/// <param name="DateUncertain">
/// Kaynak "tarih geçicidir ve değişebilir" diyorsa true. Dini bayramlarda
/// oluyor; kullanıcıya resmi kaynaktan teyit uyarısı bu yüzden gösteriliyor.
/// </param>
internal sealed record HolidayInfo(DateOnly Date, string Name, HolidayKind Kind, bool DateUncertain);
