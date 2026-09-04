using System.Text.Json.Serialization;

namespace Kalender.Google;

// Google REST yanıtlarının yalnızca kullandığımız alanları. İstekte "fields"
// parametresiyle zaten bu alanlar isteniyor; gereksiz veri hiç inmiyor.

internal sealed class CalendarListResponse
{
    [JsonPropertyName("items")] public List<CalendarListEntry>? Items { get; set; }

    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

internal sealed class CalendarListEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("summary")] public string? Summary { get; set; }

    [JsonPropertyName("summaryOverride")] public string? SummaryOverride { get; set; }

    [JsonPropertyName("backgroundColor")] public string? BackgroundColor { get; set; }

    [JsonPropertyName("primary")] public bool? Primary { get; set; }

    [JsonPropertyName("deleted")] public bool? Deleted { get; set; }
}

internal sealed class ColorsResponse
{
    [JsonPropertyName("event")] public Dictionary<string, ColorEntry>? Event { get; set; }
}

internal sealed class ColorEntry
{
    [JsonPropertyName("background")] public string? Background { get; set; }
}

internal sealed class EventsResponse
{
    [JsonPropertyName("items")] public List<EventEntry>? Items { get; set; }

    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

internal sealed class EventEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("summary")] public string? Summary { get; set; }

    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("colorId")] public string? ColorId { get; set; }

    [JsonPropertyName("start")] public EventTime? Start { get; set; }

    [JsonPropertyName("end")] public EventTime? End { get; set; }
}

internal sealed class EventTime
{
    /// <summary>Tüm gün süren etkinliklerde dolu (yyyy-MM-dd).</summary>
    [JsonPropertyName("date")] public string? Date { get; set; }

    /// <summary>Saatli etkinliklerde dolu (RFC3339).</summary>
    [JsonPropertyName("dateTime")] public DateTimeOffset? DateTime { get; set; }
}

internal sealed class TaskListsResponse
{
    [JsonPropertyName("items")] public List<TaskListEntry>? Items { get; set; }

    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

internal sealed class TaskListEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }
}

internal sealed class TasksResponse
{
    [JsonPropertyName("items")] public List<TaskEntry>? Items { get; set; }

    [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
}

internal sealed class TaskEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }

    /// <summary>RFC3339; Google Tasks yalnızca gün bilgisini anlamlı tutar.</summary>
    [JsonPropertyName("due")] public string? Due { get; set; }

    /// <summary>"needsAction" ya da "completed".</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }

    [JsonPropertyName("deleted")] public bool? Deleted { get; set; }

    [JsonPropertyName("hidden")] public bool? Hidden { get; set; }
}
