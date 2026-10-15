using System.Drawing;
using Kalender.Calendar.Holidays;

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

    /// <summary>
    /// Tamamlanmış bir görev mi? Öyleyse hem noktacık hem gün kutusundaki
    /// satır, kendi renginin yerine soluk griyle çiziliyor.
    /// </summary>
    public bool Completed { get; init; }

    /// <summary>
    /// Tatil satırında: yarım günlük tatil (arife) mi? Gün kutusunda tam gün
    /// tatiller kırmızı, arifeler olağan renkte yazılır.
    /// </summary>
    public bool YarimGun { get; init; }

    /// <summary>
    /// Aynı gün içinde sıralama anahtarı. Saati bilinen bir görev, "en altta"
    /// değil saatli kayıtların arasında doğru yerinde durur; kutuya bakan kişi
    /// günü baştan sona saat sırasıyla okuyabilsin.
    /// </summary>
    public (int, int) SortKey => (
        Kind == EntryKind.Task && Start.HasValue ? (int)EntryKind.Timed : (int)Kind,
        Start.HasValue ? (Start.Value.Hour * 60) + Start.Value.Minute : 0);
}

/// <summary>Bir günün bütün içeriği.</summary>
internal sealed class DayContent
{
    public required DateOnly Date { get; init; }

    /// <summary>
    /// Güne düşen resmî tatiller; tatil değilse boş. Aynı güne iki tatil
    /// düşebilir (19 Mayıs 2027 hem Kurban Bayramı hem 19 Mayıs); tam günler önce.
    /// </summary>
    public IReadOnlyList<HolidayInfo> Tatiller { get; init; } = Array.Empty<HolidayInfo>();

    public IReadOnlyList<DayEntry> Entries { get; init; } = Array.Empty<DayEntry>();

    /// <summary>
    /// Gün kırmızı yazılmalı mı? Yalnızca tam gün resmi tatillerde. Şartname
    /// gereği arifeler (yarım gün) özel renk almaz, adları yalnızca gün
    /// kutusunda görünür; aynı güne tam gün bir tatil de düşerse gün kırmızı.
    /// </summary>
    public bool IsHoliday => Tatiller.Any(t => t.Kind == HolidayKind.Full);

    /// <summary>Popup açılmasını gerektiren bir şey var mı?</summary>
    public bool HasAnything => Tatiller.Count > 0 || Entries.Count > 0;
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
