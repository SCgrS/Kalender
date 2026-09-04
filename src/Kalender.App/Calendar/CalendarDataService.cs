using Kalender.Calendar.Holidays;
using Kalender.Settings;

namespace Kalender.Calendar;

/// <summary>
/// Görünen ayın gün içeriklerini hazırlar.
///
/// Şu an yalnızca resmi tatilleri birleştiriyor; 4. aşamada Google takvim
/// etkinlikleri ve görevler de buraya katılacak. Her kaynak değiştiğinde
/// yeni bir anlık görüntü üretilip tek atışta yayınlanır, böylece çizim ve
/// gün kutusu her zaman tutarlı veri görür.
/// </summary>
internal sealed class CalendarDataService
{
    private readonly HolidayService _holidays;
    private readonly SettingsStore _settings;

    private DateOnly _today = DateOnly.FromDateTime(DateTime.Now);

    public CalendarDataService(HolidayService holidays, SettingsStore settings)
    {
        _holidays = holidays;
        _settings = settings;
    }

    /// <summary>Yeni anlık görüntü hazır.</summary>
    public event Action<CalendarDataSnapshot>? Changed;

    public CalendarDataSnapshot Current { get; private set; } = CalendarDataSnapshot.Empty;

    public void SetToday(DateOnly today)
    {
        _today = today;
        Rebuild();
    }

    /// <summary>Kaynaklardan görünen ayın içeriğini yeniden derler.</summary>
    public void Rebuild()
    {
        (DateOnly first, DateOnly last) = CalendarLayout.GridRange(_today);

        var days = new Dictionary<DateOnly, DayContent>();
        bool holidaysOn = _settings.Current.Holidays.Enabled;

        for (DateOnly day = first; day <= last; day = day.AddDays(1))
        {
            HolidayInfo? holiday = null;
            if (holidaysOn && _holidays.Days.TryGetValue(day, out HolidayInfo? found))
            {
                holiday = found;
            }

            var entries = new List<DayEntry>();

            if (holiday is not null)
            {
                entries.Add(new DayEntry
                {
                    Kind = EntryKind.Holiday,
                    Title = holiday.Name,
                });
            }

            // Google etkinlikleri ve görevler 4. aşamada buraya eklenecek.

            if (holiday is null && entries.Count == 0)
            {
                continue;
            }

            days[day] = new DayContent
            {
                Date = day,
                HolidayName = holiday?.Name,
                IsHalfDayHoliday = holiday?.Kind == HolidayKind.HalfDay,
                Entries = entries,
            };
        }

        Current = new CalendarDataSnapshot(days);
        Changed?.Invoke(Current);
    }
}
