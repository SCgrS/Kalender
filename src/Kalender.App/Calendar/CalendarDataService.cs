using System.Drawing;
using Kalender.Calendar.Holidays;
using Kalender.Google;
using Kalender.Rendering;
using Kalender.Settings;

namespace Kalender.Calendar;

/// <summary>
/// Görünen ayın gün içeriklerini hazırlar: resmi tatiller, Google takvim
/// etkinlikleri ve görevler tek bir anlık görüntüde birleşir.
///
/// Her kaynak değiştiğinde yeni bir anlık görüntü üretilip tek atışta
/// yayınlanır; böylece çizim ve gün kutusu her zaman tutarlı veri görür.
/// </summary>
internal sealed class CalendarDataService
{
    private readonly HolidayService _holidays;
    private readonly ICalendarProvider? _provider;
    private readonly SettingsStore _settings;

    private DateOnly _viewMonth = new(DateTime.Now.Year, DateTime.Now.Month, 1);

    public CalendarDataService(HolidayService holidays, ICalendarProvider? provider, SettingsStore settings)
    {
        _holidays = holidays;
        _provider = provider;
        _settings = settings;
    }

    /// <summary>Yeni anlık görüntü hazır.</summary>
    public event Action<CalendarDataSnapshot>? Changed;

    public CalendarDataSnapshot Current { get; private set; } = CalendarDataSnapshot.Empty;

    /// <summary>Şu an gösterilen ayın kapsadığı gün aralığı.</summary>
    public (DateOnly First, DateOnly Last) VisibleRange => CalendarLayout.GridRange(_viewMonth);

    /// <summary>
    /// Hangi ayın verisi hazırlanacağını belirler. Google'dan da yalnızca bu
    /// aralık istenir; menüden başka bir aya gidildiğinde burası değişir.
    /// </summary>
    public void SetViewMonth(DateOnly month)
    {
        var yeni = new DateOnly(month.Year, month.Month, 1);
        if (yeni == _viewMonth)
        {
            Rebuild();
            return;
        }

        _viewMonth = yeni;
        Rebuild();
    }

    /// <summary>Kaynaklardan görünen ayın içeriğini yeniden derler.</summary>
    public void Rebuild()
    {
        (DateOnly first, DateOnly last) = VisibleRange;

        var days = new Dictionary<DateOnly, List<DayEntry>>();
        var holidayNames = new Dictionary<DateOnly, IReadOnlyList<HolidayInfo>>();

        AddHolidays(first, last, days, holidayNames);
        AddEvents(first, last, days);
        AddTasks(first, last, days);

        var result = new Dictionary<DateOnly, DayContent>(days.Count);

        foreach ((DateOnly day, List<DayEntry> entries) in days)
        {
            MergeDuplicateTasks(entries);

            // Kararlı sıralama: aynı güne düşen iki tatil hep aynı sırada kalır.
            List<DayEntry> sirali = entries.OrderBy(e => e.SortKey).ToList();

            holidayNames.TryGetValue(day, out IReadOnlyList<HolidayInfo>? tatiller);

            result[day] = new DayContent
            {
                Date = day,
                Tatiller = tatiller ?? Array.Empty<HolidayInfo>(),
                Entries = sirali,
            };
        }

        Current = new CalendarDataSnapshot(result);
        Changed?.Invoke(Current);
    }

    private void AddHolidays(DateOnly first, DateOnly last,
        Dictionary<DateOnly, List<DayEntry>> days, Dictionary<DateOnly, IReadOnlyList<HolidayInfo>> holidayNames)
    {
        if (!_settings.Current.Holidays.Enabled)
        {
            return;
        }

        for (DateOnly day = first; day <= last; day = day.AddDays(1))
        {
            if (!_holidays.Days.TryGetValue(day, out IReadOnlyList<HolidayInfo>? tatiller))
            {
                continue;
            }

            // Aynı güne düşen her tatil gün kutusunda ayrı satır.
            holidayNames[day] = tatiller;
            foreach (HolidayInfo holiday in tatiller)
            {
                Bucket(days, day).Add(new DayEntry
                {
                    Kind = EntryKind.Holiday,
                    Title = holiday.Name,
                    YarimGun = holiday.Kind == HolidayKind.HalfDay,
                });
            }
        }
    }

    private void AddEvents(DateOnly first, DateOnly last, Dictionary<DateOnly, List<DayEntry>> days)
    {
        if (_provider is null)
        {
            return;
        }

        List<string> secim = _settings.Current.Google.SelectedCalendarIds;

        foreach (ProviderEvent e in _provider.Data.Events)
        {
            if (!Secili(secim, e.CalendarId))
            {
                continue;
            }

            Color dot = ColorTools.Parse(e.ColorHex, Color.FromArgb(0x4C, 0x8D, 0xFF));

            DateOnly from = e.StartDate < first ? first : e.StartDate;
            DateOnly to = e.EndDate > last ? last : e.EndDate;

            for (DateOnly day = from; day <= to; day = day.AddDays(1))
            {
                // Çok günlü saatli etkinliklerde saat yalnızca ilk günde anlamlı;
                // sonraki günler "tüm gün" gibi gösteriliyor.
                bool showTime = !e.AllDay && day == e.StartDate;

                Bucket(days, day).Add(new DayEntry
                {
                    Kind = showTime ? EntryKind.Timed : EntryKind.AllDay,
                    Title = e.Title,
                    Start = showTime ? e.StartTime : null,
                    DotColor = dot,
                });
            }
        }
    }

    private void AddTasks(DateOnly first, DateOnly last, Dictionary<DateOnly, List<DayEntry>> days)
    {
        if (_provider is null)
        {
            return;
        }

        Color dot = ColorTools.Parse(
            _settings.Current.Google.TaskColor, Color.FromArgb(0x4C, 0x8D, 0xFF));

        List<string> secim = _settings.Current.Google.SelectedTaskListIds;

        foreach (ProviderTask task in _provider.Data.Tasks)
        {
            if (task.Due < first || task.Due > last || !Secili(secim, task.ListId))
            {
                continue;
            }

            Bucket(days, task.Due).Add(new DayEntry
            {
                Kind = EntryKind.Task,
                Title = task.Title,
                DotColor = dot,
                Completed = task.Completed,
            });
        }
    }

    /// <summary>
    /// Takvim ya da görev listesi ayarlarda seçili mi? Seçim Google'dan alınırken
    /// de uygulanıyor, ama ay belleğinden gelen ay eski seçimle alınmıştı: kapatılan
    /// takvimin kayıtları eşitleme bitene dek, çevrim dışıyken de sürekli
    /// çiziliyordu. Boş seçim "hepsi" demek; "hiçbiri" işareti hiçbir kimlikle
    /// eşleşmez.
    /// </summary>
    private static bool Secili(List<string> secim, string kimlik)
        => secim.Count == 0 || secim.Contains(kimlik);

    /// <summary>
    /// Aynı gün içinde başlığı bir görevle birebir aynı olan etkinlikleri
    /// görevin içine katar.
    ///
    /// Google Görevler'i hem Tasks API'sinden hem de takvim etkinliği olarak
    /// döndürebiliyor; ikisi birden gösterilirse aynı kayıt iki kez çıkıyor.
    /// Görev kaydını tutuyoruz, çünkü tamamlanmışlık bilgisi yalnızca orada.
    /// Ama **saat** yalnızca etkinlik tarafında: Görevler API'si görevin
    /// saatini hiç vermiyor, `due` her zaman gecenin yarısı geliyor. O yüzden
    /// atmadan önce saati görevin üstüne alıyoruz.
    /// </summary>
    private static void MergeDuplicateTasks(List<DayEntry> entries)
    {
        if (entries.All(e => e.Kind != EntryKind.Task))
        {
            return;
        }

        // Başlık -> etkinlikten gelen saat. Aynı başlıkta birden çok kayıt
        // varsa ilk saatli olan geçerli.
        var times = new Dictionary<string, TimeOnly>(StringComparer.Ordinal);
        foreach (DayEntry e in entries)
        {
            if (e.Kind == EntryKind.Timed && e.Start is TimeOnly start)
            {
                times.TryAdd(Normalize(e.Title), start);
            }
        }

        var taskTitles = entries
            .Where(e => e.Kind == EntryKind.Task)
            .Select(e => Normalize(e.Title))
            .ToHashSet(StringComparer.Ordinal);

        for (int i = 0; i < entries.Count; i++)
        {
            DayEntry entry = entries[i];
            if (entry.Kind != EntryKind.Task || entry.Start is not null)
            {
                continue;
            }

            if (times.TryGetValue(Normalize(entry.Title), out TimeOnly saat))
            {
                entries[i] = new DayEntry
                {
                    Kind = entry.Kind,
                    Title = entry.Title,
                    Start = saat,
                    DotColor = entry.DotColor,
                    Completed = entry.Completed,
                };
            }
        }

        entries.RemoveAll(e => e.Kind is EntryKind.Timed or EntryKind.AllDay
            && taskTitles.Contains(Normalize(e.Title)));
    }

    private static string Normalize(string title)
        => string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private static List<DayEntry> Bucket(Dictionary<DateOnly, List<DayEntry>> days, DateOnly day)
    {
        if (!days.TryGetValue(day, out List<DayEntry>? list))
        {
            list = new List<DayEntry>();
            days[day] = list;
        }

        return list;
    }
}
