using System.Drawing;

namespace Kalender.Calendar;

/// <summary>Bir güne düşen kaydın türü. Popup'taki sıralamayı da belirler.</summary>
internal enum EntryKind
{
    /// <summary>Resmi tatil. Her zaman en üstte.</summary>
    Holiday = 0,

    /// <summary>Tüm gün süren etkinlik.</summary>
    AllDay = 1,

    /// <summary>Saati belli etkinlik.</summary>
    Timed = 2,

    /// <summary>Google görevi. Her zaman en altta.</summary>
    Task = 3,
}

/// <summary>Popup'ta tek bir satır olarak görünen kayıt.</summary>
internal sealed class DayEntry
{
    public required EntryKind Kind { get; init; }

    public required string Title { get; init; }

    /// <summary>Saatli etkinliklerde başlangıç saati.</summary>
    public TimeOnly? Start { get; init; }

    /// <summary>Gün altındaki noktacığın rengi. Tatiller nokta üretmez.</summary>
    public Color DotColor { get; init; }

    /// <summary>Aynı gün içinde sıralama anahtarı.</summary>
    public (int, int) SortKey => ((int)Kind, Start.HasValue ? (Start.Value.Hour * 60) + Start.Value.Minute : 0);
}

/// <summary>Bir günün bütün içeriği.</summary>
internal sealed class DayContent
{
    public required DateOnly Date { get; init; }

    /// <summary>Resmi tatil adı; tatil değilse null.</summary>
    public string? HolidayName { get; init; }

    /// <summary>
    /// Yarım günlük tatil (arife) mi? Şartname gereği arifeler özel renk almaz;
    /// adı yalnızca gün kutusunda görünür.
    /// </summary>
    public bool IsHalfDayHoliday { get; init; }

    public IReadOnlyList<DayEntry> Entries { get; init; } = Array.Empty<DayEntry>();

    /// <summary>Gün kırmızı yazılmalı mı? Yalnızca tam gün resmi tatillerde.</summary>
    public bool IsHoliday => HolidayName is not null && !IsHalfDayHoliday;

    /// <summary>Popup açılmasını gerektiren bir şey var mı?</summary>
    public bool HasAnything => HolidayName is not null || Entries.Count > 0;
}

/// <summary>
/// Görünen aya ait bütün gün içeriklerinin bir anlık görüntüsü.
/// Veri katmanı yeni bir anlık görüntü hazırlayıp tek atışta değiştirir;
/// çizim ve popup her zaman tutarlı bir veri kümesi görür.
/// </summary>
internal sealed class CalendarDataSnapshot
{
    public static CalendarDataSnapshot Empty { get; } = new(new Dictionary<DateOnly, DayContent>());

    private readonly IReadOnlyDictionary<DateOnly, DayContent> _days;

    public CalendarDataSnapshot(IReadOnlyDictionary<DateOnly, DayContent> days)
    {
        _days = days;
    }

    public DayContent? For(DateOnly date) => _days.TryGetValue(date, out var content) ? content : null;

    public int Count => _days.Count;
}
