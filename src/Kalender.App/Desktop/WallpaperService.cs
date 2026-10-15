using System.IO;
using System.Text;
using System.Text.Json;
using Kalender.Core;
using Kalender.Interop;
using Microsoft.Win32;

namespace Kalender.Desktop;

/// <summary>Kalender kurulmadan önceki duvar kağıdı durumu.</summary>
internal sealed class WallpaperBackup
{
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Bütün ekranlar için ortak duvar kağıdı yolu (varsa).</summary>
    public string? Wallpaper { get; set; }

    /// <summary>Zemin rengi, COLORREF olarak.</summary>
    public uint BackgroundColor { get; set; }

    /// <summary>Yerleşim biçimi.</summary>
    public int Position { get; set; }

    /// <summary>
    /// Kullanıcının duvar kağıdı olarak resim mi kullandığı. Yalnızca düz renk
    /// kullanıyorduysa geri yüklerken de resmi kapatmamız gerekir.
    /// </summary>
    public bool WallpaperEnabled { get; set; } = true;

    /// <summary>
    /// Kayıttaki JPEGImportQuality değeri; hiç ayarlanmamışsa null.
    /// Kalender bu değeri 100'e çekiyor, geri yüklerken eski haline döndürüyoruz.
    /// </summary>
    public int? JpegImportQuality { get; set; }

    /// <summary>Monitör aygıt yolu -> duvar kağıdı yolu.</summary>
    public Dictionary<string, string> PerMonitor { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Kullanıcının Kalender'den önceki duvar kağıdını yedekler ve geri yükler.
///
/// Kalender takvimi doğrudan duvar kağıdına işlediği için
/// (<see cref="WallpaperCanvas"/>) kullanıcının kendi duvar kağıdının üstüne
/// yazıyor. Bu yüzden ilk yazmadan önce eski durum bir kez kayda geçiriliyor;
/// "eski duvar kağıdıma dön" ve kaldırma işlemi onu geri koyuyor.
///
/// Yedek geri yüklenince silinir. Önceden hiç silinmiyordu: kullanıcı geri
/// döndükten sonra yeni bir duvar kâğıdı seçip yazmayı yeniden açtığında (ya da
/// ayarlarını koruyarak yeniden kurduğunda) bu seçim yedeklenmiyor, sonraki geri
/// dönüş aylar önceki ilk duvar kâğıdını koyuyordu. Hiçbir ekrana geri
/// konamayan yedek ise silinmez, bekler (bkz. <see cref="BekleyenYedekDosyasi"/>).
/// </summary>
internal static class WallpaperService
{
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string QualityValue = "JPEGImportQuality";

    /// <summary>
    /// Hiçbir ekrana geri konamayan yedek: fotoğraf o sırada takılı olmayan bir
    /// diskte ya da ulaşılamayan bir ağ sürücüsünde olabilir. Sonraki geri
    /// yükleme onu yeniden dener. Yazma yeniden açılınca ekranda kullanıcının bir
    /// duvar kâğıdı varsa yerine o yedeklenir; ekranda yalnız takvimin görüntüsü
    /// varsa bu yedek yeniden geçerli olur. Ayrı adla durur, çünkü
    /// <see cref="EnsureBackup"/> var olan yedeğe dokunmuyor: aynı adla kalsaydı
    /// fotoğrafı silinmiş bir yedek, kullanıcının sonraki seçimlerinin
    /// yedeklenmesini hep engellerdi.
    /// </summary>
    internal static string BekleyenYedekDosyasi => Path.ChangeExtension(Paths.BackupFile, ".bekliyor.json");

    /// <summary>
    /// Düz renge dönüldükten sonra Windows'un ekranlarda bildirmeyi sürdürdüğü
    /// duvar kâğıdı yolları (bkz. <see cref="DuzRengiNotEt"/>).
    /// </summary>
    internal static string DuzRenkDosyasi => Path.ChangeExtension(Paths.BackupFile, ".duz-renk.txt");

    /// <summary>
    /// Duvar kağıdında resim kipini açar.
    ///
    /// Dikkat: bu çağrı ancak ortada gerçekten bir duvar kağıdı dosyası varken
    /// çalışır. Kullanıcı düz renk kullanıyorsa (kayıtlı bir resim yolu yoksa)
    /// kabuk açacak dosyayı bulamıyor ve 0x80070002 ile hata veriyor. Bu yüzden
    /// çağrı her zaman resim yazıldıktan sonra yapılır ve başarısızlığı
    /// ölümcül sayılmaz: görüntü zaten yerine kondu.
    /// </summary>
    internal static void EnableImages(IDesktopWallpaper wallpaper)
    {
        try
        {
            wallpaper.Enable(true);
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kâğıdı resim kipi açılamadı: " + ex.Message);
        }
    }

    /// <summary>
    /// İlk değişiklikten önce mevcut durumu bir kez yedekler. Okunamayan yedek
    /// yokmuş gibi sayılır, yerine taze yedek alınır (bkz.
    /// <see cref="OkunamayanYedegiKenaraAl"/>).
    /// </summary>
    internal static void EnsureBackup(IDesktopWallpaper wallpaper)
    {
        OkunamayanYedegiKenaraAl(Paths.BackupFile);
        if (File.Exists(Paths.BackupFile))
        {
            return;
        }

        try
        {
            WallpaperBackup? backup = YedekHazirla(wallpaper);
            if (backup is null)
            {
                // Ekranda kullanıcının duvar kâğıdı yok; geri konamamış bir
                // yedek varsa en iyi bilgi hâlâ o.
                if (File.Exists(BekleyenYedekDosyasi))
                {
                    File.Move(BekleyenYedekDosyasi, Paths.BackupFile);
                    Log.Info("Geri konamamış duvar kâğıdı yedeği yeniden geçerli.");
                }

                return;
            }

            Directory.CreateDirectory(Paths.RootFolder);
            YedegiYaz(Paths.BackupFile, backup);
            File.Delete(BekleyenYedekDosyasi);

            // Düz renk notu bu yedekte kullanıldı; bundan sonra ekrana yeni görüntüler yazılıyor.
            File.Delete(DuzRenkDosyasi);

            Log.Info($"Önceki duvar kâğıdı yedeklendi (resim: {backup.WallpaperEnabled}).");
        }
        catch (Exception ex)
        {
            Log.Warn("Duvar kâğıdı yedeklenemedi.", ex);
        }
    }

    /// <summary>
    /// Yedeği önce geçici dosyaya yazar ve diske boşaltır, sonra yerine taşır
    /// (ayar dosyasındaki gibi). Önceden doğrudan asıl dosyaya yazılıyordu:
    /// yazımdan hemen sonra elektrik kesilir ya da bilgisayar zorla kapanırsa
    /// dosya boş ya da yarım kalabiliyordu.
    /// </summary>
    internal static void YedegiYaz(string dosya, WallpaperBackup backup)
    {
        string gecici = dosya + ".tmp";
        using (var akis = new FileStream(gecici, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            akis.Write(new UTF8Encoding(false).GetBytes(
                JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true })));
            akis.Flush(flushToDisk: true);
        }

        File.Move(gecici, dosya, overwrite: true);
    }

    /// <summary>
    /// Yedek dosyası okunamıyorsa (boş, yarım ya da bozuk: yazılırken elektrik
    /// kesildi, bilgisayar zorla kapandı) "onceki-duvar-kagidi.bozuk-&lt;tarih&gt;.json"
    /// adıyla kenara alır; bundan sonra yedek yokmuş gibi davranılır. Önceden
    /// yerinde kalıyordu: her geri dönüş başarısız oluyor, var olduğu için de
    /// yerine yeni yedek alınmıyordu; kurtulmanın tek yolu dosyayı elle silmekti.
    /// Dosyaya o an hiç erişilemiyorsa (kilitli) içeriği bilinmediği için dokunulmaz.
    /// </summary>
    internal static void OkunamayanYedegiKenaraAl(string dosya)
    {
        string json;
        try
        {
            if (!File.Exists(dosya))
            {
                return;
            }

            json = File.ReadAllText(dosya, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Debug($"Duvar kâğıdı yedeği okunamadı ({Path.GetFileName(dosya)}): {ex.Message}");
            return;
        }

        try
        {
            if (JsonSerializer.Deserialize<WallpaperBackup>(json) is not null)
            {
                return;
            }
        }
        catch (JsonException)
        {
            // İçerik okunamıyor; aşağıda kenara alınır.
        }
        catch (Exception ex)
        {
            // İçerikten olmayan bir hata: dosyanın bozuk olduğu bilinmiyor.
            Log.Debug($"Duvar kâğıdı yedeği denetlenemedi ({Path.GetFileName(dosya)}): {ex.Message}");
            return;
        }

        try
        {
            string klasor = Path.GetDirectoryName(dosya)!;
            string ad = $"{Path.GetFileNameWithoutExtension(dosya)}.bozuk-{DateTime.Now:yyyy-MM-dd-HHmmss}";
            string hedef = Path.Combine(klasor, ad + ".json");
            for (int i = 2; File.Exists(hedef); i++)
            {
                hedef = Path.Combine(klasor, $"{ad}-{i}.json");
            }

            File.Move(dosya, hedef);
            Log.Warn("Okunamayan duvar kâğıdı yedeği kenara alındı: " + Path.GetFileName(hedef));
        }
        catch (Exception ex)
        {
            Log.Warn("Okunamayan duvar kâğıdı yedeği kenara alınamadı.", ex);
        }
    }

    /// <summary>
    /// Windows'un bütün masaüstü için verdiği duvar kâğıdı yolunu okur
    /// (SPI_GETDESKWALLPAPER); okunamazsa null. Sınama bunu değiştirir, gerçek
    /// ayar yalnız okunur.
    /// </summary>
    internal static Func<string?> SistemDuvarKagidiOkuyucu { get; set; } = SistemDuvarKagidiniOku;

    /// <summary>
    /// Windows'ta resimli duvar kâğıdı kapalı mı (düz renk)? Windows düz renkte
    /// bu yolu boş verir; resim varken (ekranlarda ayrı resimler olsa da) dolu.
    /// Okunamazsa false: emin olunamayan yerde eski davranış sürer.
    /// </summary>
    internal static bool ResimKipiKapali()
        => SistemDuvarKagidiOkuyucu() is string yol && string.IsNullOrWhiteSpace(yol);

    private static string? SistemDuvarKagidiniOku()
    {
        try
        {
            var yol = new StringBuilder(1024);
            return NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETDESKWALLPAPER, (uint)yol.Capacity, yol, 0)
                ? yol.ToString()
                : null;
        }
        catch (Exception ex)
        {
            Log.Debug("Windows'un duvar kâğıdı ayarı okunamadı: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Ekrandaki duvar kâğıdı durumunu okur. Kalender'in kendi zemin
    /// görüntüleri yedeğe yazılmaz: geri yüklemede kullanıcının duvar kâğıdı
    /// yerine takvimin donmuş hâli gelirdi. O ekran geri yüklenirken ortak yol
    /// ya da başka bir ekranınki kullanılır (bkz. <see cref="EkranlariGeriYukle"/>).
    /// </summary>
    /// <returns>
    /// Yedek; ekranlarda yalnızca Kalender'in görüntüleri duruyorsa null
    /// (kullanıcının duvar kâğıdı bilinmiyor, yedek yazılmaz). O zaman da
    /// Windows'ta resim kipi kapalıysa düz renk yedeği döner.
    /// </returns>
    internal static WallpaperBackup? YedekHazirla(IDesktopWallpaper wallpaper)
    {
        var backup = new WallpaperBackup
        {
            BackgroundColor = wallpaper.GetBackgroundColor(),
            Position = (int)wallpaper.GetPosition(),
            JpegImportQuality = ReadImportQuality(),
        };

        bool bizimkiVardi = false;

        try
        {
            string? ortak = wallpaper.GetWallpaper(null);
            if (ZeminDosyasiMi(ortak))
            {
                bizimkiVardi = true;
            }
            else
            {
                backup.Wallpaper = ortak;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Ortak duvar kâğıdı okunamadı: " + ex.Message);
        }

        uint count = wallpaper.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            string id = wallpaper.GetMonitorDevicePathAt(i);
            try
            {
                string yol = wallpaper.GetWallpaper(id) ?? string.Empty;
                if (ZeminDosyasiMi(yol))
                {
                    bizimkiVardi = true;
                    continue;
                }

                backup.PerMonitor[id] = yol;
            }
            catch (Exception ex)
            {
                Log.Debug($"Monitör duvar kâğıdı okunamadı ({id}): {ex.Message}");
            }
        }

        // Hiçbir ekranda resim yoksa kullanıcı düz renk kullanıyordu.
        backup.WallpaperEnabled = !string.IsNullOrWhiteSpace(backup.Wallpaper)
            || backup.PerMonitor.Values.Any(v => !string.IsNullOrWhiteSpace(v));

        if (bizimkiVardi && !backup.WallpaperEnabled)
        {
            // Windows'ta resim kipi kapalıysa kullanıcı düz renk görüyor; yedek
            // düz renk olarak yazılır. "Eski duvar kâğıdıma dön" düz renkte
            // yalnız resim kipini kapatıyor. Windows ekranların yolunu bundan
            // sonra da takvimin görüntüsü olarak veriyorsa (ölçülmedi) önceden
            // yedek hiç alınmıyor, sonraki geri dönüş düz renk yerine takvimsiz
            // zemini koyup uyarı veriyordu. Windows o sırada resim kipini de
            // açık bildirirse ekranların düz renge dönüldüğü andaki gibi
            // durmasına bakılır (bkz. DuzRenkSuruyor).
            if (ResimKipiKapali() || DuzRenkSuruyor(EkrandakiDosyalar(wallpaper), DuzRenkDosyasi))
            {
                Log.Debug("Ekranlarda takvimin görüntüleri var ama resim kipi kapalı; düz renk yedeklendi.");
                return backup;
            }

            Log.Debug("Ekranlarda yalnızca takvimin kendi görüntüleri var; duvar kâğıdı yedeklenmedi.");
            return null;
        }

        return backup;
    }

    /// <summary>Yedeklenen duvar kağıdını geri yükler. Kaldırma sırasında da çağrılır.</summary>
    public static bool Restore() => Restore(out _);

    /// <summary>
    /// Yedeklenen duvar kâğıdını geri yükler. Yedek kullanıldıktan sonra silinir;
    /// yazma yeniden açılınca o sırada ekranda duran duvar kâğıdı yedeklenir.
    /// Bir ekran geri yüklenemese de yedek silinir: dosyası silinmiş ya da
    /// Windows'un kabul etmediği bir yolu yeniden denemek işe yaramaz, tutulan
    /// yedek de kullanıcının sonraki seçimlerinin yedeklenmesini engellerdi.
    /// Hiçbir ekran geri yüklenemediyse yedek silinmez, bekler (bkz.
    /// <see cref="BekleyenYedekDosyasi"/>): önceden o zaman da siliniyordu;
    /// fotoğraf takılı olmayan bir diskteyse önceki duvar kâğıdının tek kaydı
    /// gidiyordu. Okunamayan yedek kenara alınır, yokmuş gibi davranılır (bkz.
    /// <see cref="OkunamayanYedegiKenaraAl"/>).
    /// </summary>
    /// <param name="eksikKaldi">
    /// Ekranlarda Kalender'in görüntüsü kalmış olabilir mi? Yedek yoksa ve
    /// ekranda takvimin görüntüsü duruyorsa, geri yükleme yarıda kaldıysa ya da
    /// bir ekran geri yüklenemediyse doğru. Düz renge dönüldükten sonra
    /// Windows'un bildirmeyi sürdürdüğü görüntüler sayılmaz (bkz.
    /// <see cref="DuzRenkSuruyor"/>): ekranda düz renk duruyor.
    /// </param>
    /// <returns>
    /// Önceki duvar kâğıdı ekrandaysa true: yedek uygulandı (bir ekran geri
    /// yüklenemese de) ya da yedek yok ama ekranlarda takvimin görüntüsü de yok.
    /// İkincisi, yedek kullanılınca silindiği için tepsiden ikinci kez geri
    /// dönülünce ya da anahtar kapatıldıktan sonra olur; önceden uyarı veriyordu.
    /// </returns>
    internal static bool Restore(out bool eksikKaldi)
    {
        eksikKaldi = true;

        try
        {
            OkunamayanYedegiKenaraAl(Paths.BackupFile);
            OkunamayanYedegiKenaraAl(BekleyenYedekDosyasi);

            string? yedekDosyasi = File.Exists(Paths.BackupFile) ? Paths.BackupFile
                : File.Exists(BekleyenYedekDosyasi) ? BekleyenYedekDosyasi
                : null;
            if (yedekDosyasi is null)
            {
                // Yedek yoksa bile bizim ürettiğimiz görüntüler ortada kalmasın.
                // Ekranda takvimin görüntüsü yoksa önceki duvar kâğıdı zaten yerinde.
                HashSet<string>? ekrandakiler = ClearCanvasFiles(null);
                eksikKaldi = ekrandakiler is null
                    || (ekrandakiler.Any(ZeminDosyasiMi) && !DuzRenkSuruyor(ekrandakiler, DuzRenkDosyasi));
                return !eksikKaldi;
            }

            var backup = JsonSerializer.Deserialize<WallpaperBackup>(
                File.ReadAllText(yedekDosyasi, Encoding.UTF8));
            if (backup is null)
            {
                return false;
            }

            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            wallpaper.SetBackgroundColor(backup.BackgroundColor);

            // Bayrağa körü körüne güvenmiyoruz: eski yedek dosyalarında bu alan
            // yok ve varsayılan true geliyor. Kayıtlı yol da yoksa kullanıcı
            // düz renk kullanıyordu demektir.
            bool hadImage = backup.WallpaperEnabled
                && (!string.IsNullOrWhiteSpace(backup.Wallpaper)
                    || backup.PerMonitor.Values.Any(v => !string.IsNullOrWhiteSpace(v)));
            bool geriGeldi = true;

            if (!hadImage)
            {
                // Kullanıcı düz renk kullanıyordu.
                wallpaper.Enable(false);
                DuzRengiNotEt(wallpaper, DuzRenkDosyasi);
                eksikKaldi = false;
            }
            else
            {
                eksikKaldi = !EkranlariGeriYukle(wallpaper, backup, out int konan);
                geriGeldi = konan > 0;

                // Resim kipi ancak eski yol geri konduktan sonra açılabiliyor.
                EnableImages(wallpaper);
            }

            RestoreImportQuality(backup.JpegImportQuality);
            ClearCanvasFiles(wallpaper);

            // Kalender kapalıyken de çağrılabildiği için kabuk duvar kağıdını
            // yeniden okusun diye açıkça tazeliyoruz.
            DesktopRefresher.ForceRedraw();

            YedegiKaldir(yedekDosyasi, geriGeldi);

            if (!geriGeldi)
            {
                Log.Warn("Önceki duvar kâğıdı hiçbir ekrana geri konamadı; yedek bekletiliyor.");
            }
            else
            {
                Log.Info(eksikKaldi
                    ? "Önceki duvar kâğıdı geri yüklendi; bir ekran geri yüklenemedi."
                    : "Önceki duvar kâğıdı geri yüklendi.");
            }

            return geriGeldi;
        }
        catch (Exception ex)
        {
            Log.Warn("Duvar kâğıdı geri yüklenemedi.", ex);
            return false;
        }
    }

    /// <summary>
    /// Kullanılan yedeği siler. Hiçbir ekrana geri konamadıysa silmez,
    /// <see cref="BekleyenYedekDosyasi"/> adıyla bekletir.
    /// </summary>
    /// <param name="yedekDosyasi">Geri yüklemede kullanılan yedek dosyası.</param>
    /// <param name="geriGeldi">Önceki duvar kâğıdı en az bir ekrana geri kondu mu?</param>
    internal static void YedegiKaldir(string yedekDosyasi, bool geriGeldi)
    {
        try
        {
            if (geriGeldi)
            {
                File.Delete(yedekDosyasi);
            }
            else if (!string.Equals(yedekDosyasi, BekleyenYedekDosyasi, StringComparison.OrdinalIgnoreCase))
            {
                File.Move(yedekDosyasi, BekleyenYedekDosyasi, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kâğıdı yedeği kaldırılamadı: " + ex.Message);
        }
    }

    /// <summary>
    /// Düz renge dönüldükten sonra Windows ekranlarda hâlâ takvimin görüntüsünün
    /// yolunu bildiriyorsa bildirdiği yolları not eder; bildirmiyorsa eski notu
    /// siler. Windows'un bunu yapıp yapmadığı ölçülmedi. Yapar ve resim kipini
    /// de açık bildirirse, ekranda düz renk dururken takvim duruyor sanılırdı:
    /// açılışta ya da sonraki geri dönüşte düz rengin yerine takvimsiz zemin
    /// yazılırdı (bkz. <see cref="DuzRenkSuruyor"/>).
    /// </summary>
    /// <param name="dosya">Notun yazılacağı dosya (<see cref="DuzRenkDosyasi"/>).</param>
    internal static void DuzRengiNotEt(IDesktopWallpaper wallpaper, string dosya)
    {
        try
        {
            HashSet<string>? ekrandakiler = EkrandakiDosyalar(wallpaper);
            if (ekrandakiler is not null && ekrandakiler.Any(ZeminDosyasiMi))
            {
                File.WriteAllLines(dosya, ekrandakiler, new UTF8Encoding(false));
            }
            else
            {
                File.Delete(dosya);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Düz renk notu yazılamadı: " + ex.Message);
        }
    }

    /// <summary>
    /// Ekranlar düz renge dönüldüğü andaki gibi mi: Windows'un şimdi bildirdiği
    /// her yol <see cref="DuzRengiNotEt"/>'in not ettikleri arasında mı? Öyleyse
    /// ekranda düz renk duruyor, yollardaki takvim görüntüleri görünmüyor.
    /// Kullanıcı sonradan bir duvar kâğıdı seçtiyse ya da takvim yeniden
    /// yazıldıysa yollar değişir, not geçersiz olur. Not yoksa ya da
    /// okunamazsa false: eski davranış sürer.
    /// </summary>
    /// <param name="ekrandakiler">Ekranlarda şu an duran dosyalar (<see cref="EkrandakiDosyalar"/>).</param>
    /// <param name="dosya">Not dosyası (<see cref="DuzRenkDosyasi"/>).</param>
    internal static bool DuzRenkSuruyor(HashSet<string>? ekrandakiler, string dosya)
    {
        try
        {
            if (ekrandakiler is null || ekrandakiler.Count == 0 || !File.Exists(dosya))
            {
                return false;
            }

            var notlar = new HashSet<string>(File.ReadAllLines(dosya, Encoding.UTF8), StringComparer.OrdinalIgnoreCase);
            return ekrandakiler.All(notlar.Contains);
        }
        catch (Exception ex)
        {
            Log.Debug("Düz renk notu okunamadı: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Yedekteki duvar kâğıtlarını ekranlara geri koyar.
    ///
    /// Yedekte karşılığı olmayan ekrana (yedekten sonra takılmış ya da aygıt
    /// yolu değişmiş) ortak yol, o da yoksa yedekteki ilk ekranın yolu konur.
    /// Önceden bu ekrana hiç dokunulmuyordu; takvim orada donmuş kalıyordu.
    /// Ekranın kendi yolu konamazsa (fotoğraf silinmiş, Windows kabul etmiyor)
    /// da bu yol denenir: ekranda takvimin zemini kalırsa, kaldırmada dosyası
    /// silindiği için Windows görüntüyü yeniden okuyunca ekran siyaha
    /// dönebilirdi. Dosyası olmayan yol yerine geçmez.
    /// Her ekran ayrı denenir: bir ekrandaki hata (fotoğraf silinmiş, yedekteki
    /// ekran artık bağlı değil) önceden bütün geri yüklemeyi yarıda bırakıyordu.
    /// </summary>
    /// <param name="konan">Duvar kâğıdı geri konan ekran sayısı.</param>
    /// <returns>Her ekran geri yüklendiyse true.</returns>
    internal static bool EkranlariGeriYukle(IDesktopWallpaper wallpaper, WallpaperBackup backup, out int konan)
    {
        konan = 0;
        wallpaper.SetPosition((DesktopWallpaperPosition)backup.Position);

        string? ortak = string.IsNullOrWhiteSpace(backup.Wallpaper) ? null : backup.Wallpaper;
        if (backup.PerMonitor.Count == 0)
        {
            bool kondu = ortak is not null && EkranaKoy(wallpaper, null, ortak);
            konan = kondu ? 1 : 0;
            return kondu;
        }

        string? yerine = new[] { ortak }.Concat(backup.PerMonitor.Values)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && File.Exists(Environment.ExpandEnvironmentVariables(v)));

        // Yedekteki ekranlar ve yedekte olmayan, şu an bağlı ekranlar.
        var ekranlar = new List<string>(backup.PerMonitor.Keys);
        try
        {
            uint count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = wallpaper.GetMonitorDevicePathAt(i);
                if (!string.IsNullOrWhiteSpace(id) && !backup.PerMonitor.ContainsKey(id))
                {
                    ekranlar.Add(id);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Monitör aygıt yolları okunamadı: " + ex.Message);
        }

        bool hepsi = true;
        foreach (string id in ekranlar)
        {
            string? kendi = backup.PerMonitor.TryGetValue(id, out string? k) && !string.IsNullOrWhiteSpace(k) ? k : null;

            // Önce ekranın kendi yolu, konamazsa yerine geçecek yol.
            bool kondu = (kendi is not null && EkranaKoy(wallpaper, id, kendi))
                || (yerine is not null
                    && !string.Equals(yerine, kendi, StringComparison.OrdinalIgnoreCase)
                    && EkranaKoy(wallpaper, id, yerine));

            if (kondu)
            {
                konan++;
            }
            else
            {
                hepsi = false;
            }
        }

        return hepsi;
    }

    /// <summary>Bir ekranın duvar kâğıdını koyar; hata olursa günlüğe yazar, başarısını döndürür.</summary>
    private static bool EkranaKoy(IDesktopWallpaper wallpaper, string? id, string yol)
    {
        try
        {
            if (!File.Exists(Environment.ExpandEnvironmentVariables(yol)))
            {
                Log.Warn($"Önceki duvar kâğıdının dosyası yok; ekran geri yüklenemedi ({id ?? "bütün ekranlar"}).");
                return false;
            }

            wallpaper.SetWallpaper(id, yol);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn($"Ekranın duvar kâğıdı geri yüklenemedi ({id ?? "bütün ekranlar"}).", ex);
            return false;
        }
    }

    /// <summary>
    /// Yol Kalender'in ürettiği zemin görüntülerinden biri mi (zemin
    /// klasöründe, adı "zemin-" ile başlayan)?
    /// </summary>
    internal static bool ZeminDosyasiMi(string? yol)
    {
        if (string.IsNullOrWhiteSpace(yol))
        {
            return false;
        }

        try
        {
            return string.Equals(
                    Path.GetDirectoryName(Path.GetFullPath(yol)),
                    Path.GetFullPath(Paths.CanvasFolder).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(yol).StartsWith("zemin-", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Kalender'in ürettiği zemin görüntülerini siler. Bir ekranda hâlâ duran
    /// görüntüye dokunulmaz: geri yüklenemeyen ekran onu gösteriyor; dosya
    /// silinince Windows görüntüyü yeniden okumak zorunda kaldığında
    /// (çözünürlük değişimi, Explorer'ın yeniden başlaması) ekran siyaha dönerdi.
    /// </summary>
    /// <param name="wallpaper">Ekrandakileri okumak için; null ise burada açılır.</param>
    /// <returns>Ekranlarda duran dosyalar; okunamazsa null.</returns>
    private static HashSet<string>? ClearCanvasFiles(IDesktopWallpaper? wallpaper)
    {
        try
        {
            wallpaper ??= (IDesktopWallpaper)new DesktopWallpaperClass();
            HashSet<string>? ekrandakiler = EkrandakiDosyalar(wallpaper);
            ZeminDosyalariniSil(Paths.CanvasFolder, ekrandakiler);
            return ekrandakiler;
        }
        catch (Exception ex)
        {
            Log.Debug("Zemin klasörü temizlenemedi: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Ekranlardan birinde takvimin zemin görüntüsü duruyor mu? Önceki duvar
    /// kâğıdı bir ekrana hiçbir yoldan geri konamadıysa ya da yazma önceki bir
    /// sürümde kapatıldıysa (takvim donmuş kalıyordu) böyle olur.
    /// </summary>
    /// <param name="wallpaper">Ekrandakileri okumak için; null ise burada açılır.</param>
    /// <returns>Ekrandakiler okunamazsa null.</returns>
    internal static bool? EkrandaZeminVar(IDesktopWallpaper? wallpaper = null)
    {
        try
        {
            wallpaper ??= (IDesktopWallpaper)new DesktopWallpaperClass();
            return EkrandakiDosyalar(wallpaper)?.Any(ZeminDosyasiMi);
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıtları okunamadı: " + ex.Message);
            return null;
        }
    }

    /// <summary>Ekranlarda şu an duran duvar kâğıdı dosyaları; okunamazsa null.</summary>
    internal static HashSet<string>? EkrandakiDosyalar(IDesktopWallpaper wallpaper)
    {
        try
        {
            var dosyalar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            uint count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string? yol = wallpaper.GetWallpaper(wallpaper.GetMonitorDevicePathAt(i));
                if (!string.IsNullOrWhiteSpace(yol))
                {
                    dosyalar.Add(yol);
                }
            }

            string? ortak = wallpaper.GetWallpaper(null);
            if (!string.IsNullOrWhiteSpace(ortak))
            {
                dosyalar.Add(ortak);
            }

            return dosyalar;
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıtları okunamadı: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Klasördeki zemin görüntülerini siler, ekranda duranlar hariç. Ekranda ne
    /// durduğu bilinmiyorsa (<paramref name="ekrandakiler"/> null) hiçbirini silmez.
    /// </summary>
    internal static void ZeminDosyalariniSil(string klasor, HashSet<string>? ekrandakiler)
    {
        try
        {
            if (ekrandakiler is null || !Directory.Exists(klasor))
            {
                return;
            }

            // Uzantı verilmiyor: 1.4.4'e kadar PNG, sonra BMP yazıldı; ikisi de silinsin.
            foreach (string file in Directory.EnumerateFiles(klasor, "zemin-*"))
            {
                if (ekrandakiler.Contains(file))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Log.Debug($"Zemin dosyası silinemedi ({file}): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Zemin klasörü temizlenemedi: " + ex.Message);
        }
    }

    private static int? ReadImportQuality()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(DesktopKey);
            return key?.GetValue(QualityValue) as int?;
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kâğıdı sıkıştırma kalitesi okunamadı: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Sıkıştırma kalitesini eski haline döndürür. Yedekte bir değer yoksa
    /// kullanıcı bu ayara hiç dokunmamıştı; değeri tamamen siliyoruz ki
    /// Windows kendi varsayılanına dönsün.
    /// </summary>
    private static void RestoreImportQuality(int? previous)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(DesktopKey, writable: true);
            if (key is null)
            {
                return;
            }

            if (previous is int deger)
            {
                key.SetValue(QualityValue, deger, RegistryValueKind.DWord);
            }
            else
            {
                key.DeleteValue(QualityValue, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kâğıdı sıkıştırma kalitesi geri yüklenemedi: " + ex.Message);
        }
    }
}
