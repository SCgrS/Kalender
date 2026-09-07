namespace Kalender.Google;

/// <summary>Bir takvim (Google'da "calendar").</summary>
internal sealed class ProviderCalendar
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Takvimin kendi rengi (#RRGGBB). Etkinliğin kendi rengi yoksa bu kullanılır.</summary>
    public string ColorHex { get; set; } = "#4C8DFF";

    public bool Primary { get; set; }
}

/// <summary>Bir görev listesi (Google Tasks).</summary>
internal sealed class ProviderTaskList
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

/// <summary>Tek bir etkinlik. Çok günlü etkinlikler tek kayıt olarak durur, gösterimde açılır.</summary>
internal sealed class ProviderEvent
{
    public string Id { get; set; } = string.Empty;

    public string CalendarId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool AllDay { get; set; }

    public DateOnly StartDate { get; set; }

    /// <summary>Saatli etkinliklerde başlangıç saati (yerel).</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Son gün, dahil.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Noktacığın rengi.</summary>
    public string ColorHex { get; set; } = "#4C8DFF";
}

/// <summary>Tek bir görev.</summary>
internal sealed class ProviderTask
{
    public string Id { get; set; } = string.Empty;

    public string ListId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateOnly Due { get; set; }

    /// <summary>
    /// Görev tamamlandı mı? Tamamlanmış görevler de gösteriliyor (Google Takvim
    /// de gösteriyor), ama gri çiziliyorlar.
    /// </summary>
    public bool Completed { get; set; }
}

/// <summary>Sağlayıcıdan gelen verinin tamamı; önbelleğe de bu yazılır.</summary>
internal sealed class ProviderBundle
{
    public string? AccountEmail { get; set; }

    /// <summary>Verinin kapsadığı gün aralığı.</summary>
    public DateOnly RangeFrom { get; set; }

    public DateOnly RangeTo { get; set; }

    /// <summary>En son tam yenilemenin zamanı.</summary>
    public DateTimeOffset? FullSyncUtc { get; set; }

    /// <summary>En son (artımlı ya da tam) yenilemenin zamanı; bir sonraki updatedMin değeri.</summary>
    public DateTimeOffset? LastSyncUtc { get; set; }

    public List<ProviderCalendar> Calendars { get; set; } = new();

    public List<ProviderTaskList> TaskLists { get; set; } = new();

    public List<ProviderEvent> Events { get; set; } = new();

    public List<ProviderTask> Tasks { get; set; } = new();

    /// <summary>Google'ın renk kimliği -> onaltılık renk. Neredeyse hiç değişmez.</summary>
    public Dictionary<string, string> EventColors { get; set; } = new();

    /// <summary>
    /// Etkinlik etiketi kimliği -> onaltılık renk. Kullanıcının Google Takvim'de
    /// kendi tanımladığı renkler burada durur ve eski renk listesinin yerini alır.
    /// </summary>
    public Dictionary<string, string> EventLabels { get; set; } = new();
}

/// <summary>
/// Takvim kaynağı. v1'de tek uygulaması Google; Microsoft/Outlook sonra
/// eklenecek ve veri katmanının geri kalanı değişmeyecek.
/// </summary>
internal interface ICalendarProvider
{
    /// <summary>Ayarlarda gösterilecek ad.</summary>
    string Name { get; }

    /// <summary>Bağlanmaya hazır mı (kimlik verilmiş ve yetki alınmış mı)?</summary>
    bool IsConnected { get; }

    /// <summary>Elde tutulan veri.</summary>
    ProviderBundle Data { get; }

    /// <summary>Veriyi tazeler.</summary>
    /// <param name="from">Görünen aralığın ilk günü.</param>
    /// <param name="to">Görünen aralığın son günü.</param>
    /// <param name="full">Artımlı değil, tam yenileme istensin mi?</param>
    /// <returns>Veri değiştiyse true.</returns>
    Task<bool> SyncAsync(DateOnly from, DateOnly to, bool full, CancellationToken cancel);
}
