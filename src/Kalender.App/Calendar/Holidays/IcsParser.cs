using System.Globalization;
using System.Text;

namespace Kalender.Calendar.Holidays;

/// <summary>ICS dosyasından okunan ham bir kayıt.</summary>
/// <param name="Start">Başlangıç günü.</param>
/// <param name="EndExclusive">Bitiş günü (dahil değil).</param>
/// <param name="Summary">Kaydın adı.</param>
/// <param name="Description">Açıklama; tatil türünü buradan anlıyoruz.</param>
internal sealed record IcsEvent(DateOnly Start, DateOnly EndExclusive, string Summary, string Description);

/// <summary>
/// Küçük bir ICS okuyucu. Tam bir takvim kütüphanesi değil; yalnızca resmi
/// tatil akışındaki tüm gün süren kayıtları okuyacak kadarını yapar:
/// satır katlamasını açar, kaçış dizilerini çözer ve VEVENT bloklarından
/// DTSTART / DTEND / SUMMARY / DESCRIPTION alanlarını çıkarır.
///
/// Bunun için üçüncü taraf paket eklemedik; ihtiyacımız bu kadar dar.
/// </summary>
internal static class IcsParser
{
    public static IReadOnlyList<IcsEvent> Parse(string ics)
    {
        var events = new List<IcsEvent>();

        DateOnly? start = null;
        DateOnly? end = null;
        string summary = string.Empty;
        string description = string.Empty;
        bool inEvent = false;

        foreach (string line in Unfold(ics))
        {
            if (line.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                inEvent = true;
                start = null;
                end = null;
                summary = string.Empty;
                description = string.Empty;
                continue;
            }

            if (line.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (inEvent && start is not null)
                {
                    // DTEND yoksa tek günlük kabul edilir.
                    DateOnly bitis = end ?? start.Value.AddDays(1);
                    if (bitis <= start.Value)
                    {
                        bitis = start.Value.AddDays(1);
                    }

                    events.Add(new IcsEvent(start.Value, bitis, summary, description));
                }

                inEvent = false;
                continue;
            }

            if (!inEvent)
            {
                continue;
            }

            (string name, string value) = SplitProperty(line);

            switch (name)
            {
                case "DTSTART":
                    start = ParseDate(value);
                    break;
                case "DTEND":
                    end = ParseDate(value);
                    break;
                case "SUMMARY":
                    summary = Unescape(value);
                    break;
                case "DESCRIPTION":
                    description = Unescape(value);
                    break;
            }
        }

        return events;
    }

    /// <summary>
    /// ICS satırları 75 sütunda katlanır; devam satırları boşluk ya da sekmeyle
    /// başlar. Burada birleştirip tek satır hâline getiriyoruz.
    /// </summary>
    private static IEnumerable<string> Unfold(string ics)
    {
        var current = new StringBuilder();

        foreach (string raw in ics.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                current.Append(line, 1, line.Length - 1);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }

            current.Append(line);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    /// <summary>"DTSTART;VALUE=DATE:20260101" -> ("DTSTART", "20260101")</summary>
    private static (string Name, string Value) SplitProperty(string line)
    {
        int colon = line.IndexOf(':');
        if (colon < 0)
        {
            return (string.Empty, string.Empty);
        }

        string left = line[..colon];
        int semicolon = left.IndexOf(';');
        string name = semicolon >= 0 ? left[..semicolon] : left;

        return (name.ToUpperInvariant(), line[(colon + 1)..]);
    }

    /// <summary>
    /// "20260101" ya da "20260101T000000Z" biçimindeki değerden günü alır.
    /// Tatil akışındaki kayıtlar tüm gün olduğu için saat kısmı önemsiz.
    /// </summary>
    private static DateOnly? ParseDate(string value)
    {
        if (value.Length < 8)
        {
            return null;
        }

        string datePart = value[..8];
        if (!int.TryParse(datePart, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly date))
        {
            return null;
        }

        return date;
    }

    /// <summary>ICS metin kaçışlarını çözer.</summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 >= value.Length)
            {
                sb.Append(value[i]);
                continue;
            }

            char next = value[++i];
            sb.Append(next switch
            {
                'n' or 'N' => '\n',
                ',' => ',',
                ';' => ';',
                '\\' => '\\',
                _ => next,
            });
        }

        return sb.ToString();
    }
}
