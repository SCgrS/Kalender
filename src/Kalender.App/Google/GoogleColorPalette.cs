namespace Kalender.Google;

/// <summary>
/// Google'ın API'den verdiği eski renk kodlarını, Google Takvim'in bugün
/// ekranda kullandığı renklere çevirir.
///
/// Neden gerekli: Calendar API'sinin <c>colors</c> uç noktası ve
/// <c>calendarList</c> girdileri hâlâ 2018 öncesi paleti döndürüyor
/// (örn. Peacock için <c>#46d6db</c>). Google Takvim'in kendi arayüzü ise
/// yenilenmiş paleti çiziyor (aynı renk için <c>#039BE5</c>). İkisini
/// eşlemezsek masaüstündeki noktacıklar, tarayıcıda gördüğünüz renklerden
/// belirgin biçimde farklı çıkıyor: turkuaz görünen bir kayıt Google'da mavi.
///
/// Eşleşmeyen bir kod gelirse (Google paletini yine değiştirirse) renk
/// olduğu gibi bırakılır; en kötü ihtimalle eski davranışa döneriz.
/// </summary>
internal static class GoogleColorPalette
{
    /// <summary>Eski API kodu -> Google Takvim arayüzündeki güncel karşılığı.</summary>
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        // Etkinlik renkleri (events.colorId 1-11)
        ["#a4bdfc"] = "#7986CB", // Lavanta
        ["#7ae7bf"] = "#33B679", // Adaçayı
        ["#dbadff"] = "#8E24AA", // Üzüm
        ["#ff887c"] = "#E67C73", // Flamingo
        ["#fbd75b"] = "#F6BF26", // Muz
        ["#ffb878"] = "#F4511E", // Mandalina
        ["#46d6db"] = "#039BE5", // Tavus kuşu
        ["#e1e1e1"] = "#616161", // Grafit
        ["#5484ed"] = "#3F51B5", // Yaban mersini
        ["#51b749"] = "#0B8043", // Fesleğen
        ["#dc2127"] = "#D50000", // Domates

        // Takvim renkleri (calendarList.backgroundColor 1-24)
        ["#ac725e"] = "#795548", // Kakao
        ["#d06b64"] = "#E67C73", // Flamingo
        ["#f83a22"] = "#D50000", // Domates
        ["#fa573c"] = "#F4511E", // Mandalina
        ["#ff7537"] = "#EF6C00", // Balkabağı
        ["#ffad46"] = "#F09300", // Mango
        ["#42d692"] = "#009688", // Okaliptüs
        ["#16a765"] = "#0B8043", // Fesleğen
        ["#7bd148"] = "#7CB342", // Antep fıstığı
        ["#b3dc6c"] = "#C0CA33", // Avokado
        ["#fbe983"] = "#E4C441", // Limon
        ["#fad165"] = "#F6BF26", // Muz
        ["#92e1c0"] = "#33B679", // Adaçayı
        ["#9fe1e7"] = "#039BE5", // Tavus kuşu
        ["#9fc6e7"] = "#4285F4", // Kobalt
        ["#4986e7"] = "#3F51B5", // Yaban mersini
        ["#9a9cff"] = "#7986CB", // Lavanta
        ["#b99aff"] = "#B39DDB", // Mor salkım
        ["#c2c2c2"] = "#616161", // Grafit
        ["#cabdbf"] = "#A79B8E", // Huş
        ["#cca6ac"] = "#AD1457", // Pancar
        ["#f691b2"] = "#D81B60", // Kiraz çiçeği
        ["#cd74e6"] = "#8E24AA", // Üzüm
        ["#a47ae2"] = "#9E69AF", // Ametist
    };

    /// <summary>Verilen rengi güncel palete çevirir; karşılığı yoksa aynen döner.</summary>
    public static string? ToCurrent(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return hex;
        }

        string s = hex.Trim();
        if (!s.StartsWith('#'))
        {
            s = "#" + s;
        }

        return Map.TryGetValue(s, out string? güncel) ? güncel : s;
    }
}
