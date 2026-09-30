using System.IO;
using System.Reflection;
using Kalender.Core;

namespace Kalender.Settings;

/// <summary>Uygulamayla gelen zemin fotoğrafı.</summary>
/// <param name="Id">Kimlik; gömülü kaynağın ve çıkarılan dosyanın adı da budur.</param>
/// <param name="Name">Ayarlarda görünen Türkçe ad.</param>
/// <param name="Photographer">Fotoğrafçı (Unsplash).</param>
internal sealed record HazirFotograf(string Id, string Name, string Photographer)
{
    public string FileName => Id + ".jpg";

    public string Resource => "Kalender.Assets.DuvarKagitlari." + Id + ".jpg";

    public string ThumbnailResource => "Kalender.Assets.DuvarKagitlari." + Id + "-kucuk.jpg";
}

/// <summary>
/// Uygulamanın içine gömülü dört zemin fotoğrafı (Unsplash lisanslı;
/// fotoğrafçılar ve lisans UCUNCU-TARAF.md'de). Takvimin varsayılan
/// renkleriyle, yani beyaz yazı ve kırmızı hafta sonuyla okunan koyu
/// fotoğraflar; 3840 × 2160, takvimin durduğu sağ alt bölge sakin kalacak
/// şekilde kırpıldı.
///
/// Seçilen fotoğraf exe'den <see cref="Folder"/> klasörüne çıkarılır ve
/// oradan kullanıcının kendi fotoğrafı gibi okunur. Böylece zemin çizimi,
/// ekran başına fotoğraf ve ayar dosyası hiç değişmeden çalışıyor.
/// </summary>
internal static class HazirFotograflar
{
    public static IReadOnlyList<HazirFotograf> All { get; } = new[]
    {
        new HazirFotograf("sisli-cam-ormani", "Sisli çam ormanı", "Dan Otis"),
        new HazirFotograf("sisli-dag-sirtlari", "Sisli dağ sırtları", "Chris wu"),
        new HazirFotograf("yildizli-karli-daglar", "Yıldızlı gökte karlı dağlar", "Patrick Carr"),
        new HazirFotograf("gece-ovasi", "Gece ovası", "Artur Adilkhanian"),
    };

    /// <summary>Çıkarılan fotoğrafların klasörü (ayarların yanında).</summary>
    public static string Folder => Path.Combine(Paths.RootFolder, "duvar-kagitlari");

    public static string PathOf(HazirFotograf photo) => Path.Combine(Folder, photo.FileName);

    /// <summary>Yol hazır fotoğraflardan birini mi gösteriyor? Değilse null.</summary>
    public static HazirFotograf? Find(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string full = Path.GetFullPath(path);
            return All.FirstOrDefault(p => string.Equals(PathOf(p), full, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            // Bozuk bir yol hazır fotoğraf olamaz.
            return null;
        }
    }

    /// <summary>
    /// Fotoğrafı klasöre çıkarır (yoksa ya da boyutu tutmuyorsa) ve yolunu
    /// verir. Yazma önce geçici dosyaya yapılır; yarım kalan bir dosya
    /// fotoğraf diye okunmasın. Çıkarılamazsa null.
    /// </summary>
    public static string? Prepare(HazirFotograf photo)
    {
        try
        {
            using Stream? source = OpenResource(photo.Resource);
            if (source is null)
            {
                Log.Warn("Hazır fotoğraf uygulamanın içinde bulunamadı: " + photo.Id);
                return null;
            }

            string path = PathOf(photo);
            var info = new FileInfo(path);
            if (info.Exists && info.Length == source.Length)
            {
                return path;
            }

            Directory.CreateDirectory(Folder);
            string temp = path + ".tmp";
            using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
            }

            File.Move(temp, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warn("Hazır fotoğraf çıkarılamadı: " + photo.Id, ex);
            return null;
        }
    }

    /// <summary>Ayarlar penceresindeki küçük resim (320 × 180 JPEG); bulunamazsa null.</summary>
    public static Stream? OpenThumbnail(HazirFotograf photo) => OpenResource(photo.ThumbnailResource);

    /// <summary>
    /// Ayarlarda kullanılan hazır fotoğraf diskten silinmişse yeniden çıkarır.
    /// Açılışta çağrılır; yoksa zemin sessizce düz renge düşerdi.
    /// </summary>
    public static void RestoreMissing(AppSettings settings)
    {
        IEnumerable<string?> paths = settings.Monitors.Values
            .Select(m => m.PhotoPath)
            .Append(settings.Appearance.PhotoPath);

        foreach (string? path in paths)
        {
            if (Find(path) is HazirFotograf photo && !File.Exists(path))
            {
                Prepare(photo);
            }
        }
    }

    private static Stream? OpenResource(string name)
        => Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
}
