using System.Globalization;

namespace Kalender.Google;

/// <summary>
/// Google Takvim'in web adresleri. Kalender hesaba yalnız okuma izniyle
/// bağlandığı için kayıt eklemeyi Google'ın kendi ekranına bırakıyoruz;
/// bu sınıf doğru sayfayı doğru gün seçili açan adresleri üretir.
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

    /// <summary>
    /// Yeni etkinlik ekranı. Gün verilirse o güne tüm gün bir taslak açılır,
    /// verilmezse Google kendi varsayılan gününü seçer.
    /// </summary>
    public static string NewEvent(DateOnly? date)
    {
        if (date is null)
        {
            return $"{Base}/eventedit";
        }

        string basla = date.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string bitir = date.Value.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return $"{Base}/eventedit?dates={basla}/{bitir}";
    }

    /// <summary>
    /// Yeni görev için Google Takvim'in o günkü görünümü.
    ///
    /// Google, görev oluşturma ekranı için adres desteklemiyor (etkinlikteki
    /// gibi bir "taskedit" yolu yok). En yakını doğru günü açmak: oradaki
    /// Oluştur &gt; Görev ile eklenen görev o güne düşer.
    /// </summary>
    public static string NewTask(DateOnly? date)
        => date is null ? $"{Base}/day" : Day(date.Value);
}
