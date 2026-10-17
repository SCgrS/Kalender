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
    /// Bağlı ekran sayısını okur (geri yüklemede yazım yolu buna bağlı; bkz.
    /// <see cref="EkranlariGeriYukle"/>); okunamazsa 0. Geri yükleme Kalender
    /// açık değilken de (kaldırma, "--duvar-kagidini-geri-yukle") yapıldığı için
    /// masaüstü katmanından değil Windows'tan okunur. Sınama bunu değiştirir.
    /// </summary>
    internal static Func<int> BagliEkranSayisiOkuyucu { get; set; } = BagliEkranlariSay;

    private static int BagliEkranlariSay()
    {
        try
        {
            return MonitorEnumerator.All().Count;
        }
        catch (Exception ex)
        {
            Log.Debug("Bağlı ekranlar okunamadı: " + ex.Message);
            return 0;
        }
    }

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
    ///
    /// Windows'un listesi bozuksa (bkz. <see cref="DuvarKagidiKayitlari"/>)
    /// hayalet kayıtlar ekran başına yazılmaz: gösterdikleri dosya hiçbir ekranda
    /// görünmüyor, ne kullanıcının duvar kâğıdı ne de takvim sayılır (listede
    /// canlı kayıt varsa; aşağıda). Kimliği
    /// boş kayıt da ekran başına yazılmaz: boş anahtar geri yüklemede hiçbir
    /// ekranı göstermez (1.14 onu "" anahtarıyla yazıyordu). Kimliği boş canlı
    /// kaydın resmi aygıt yolu verilmeden okunan ortak yola zaten geliyor
    /// (2026-10-17'de ölçüldü); ortak yol boşsa (ekranlarda ayrı resimler)
    /// ortak yolun yerine o yazılır: liste düzelince o ekran kendi resmine
    /// döner, öbür ekranın resmine değil. Bağlı olmayan ekranın kaydı eskisi
    /// gibi yazılır: çıkarılmış haricî ekranın fotoğrafı korunur. Listede canlı
    /// kayıt hiç yoksa hayalete bakılır: gösterdiği fotoğraf, yedek başka
    /// türlü resimsiz çıkacaksa ortak yola yazılır; gösterdiği takvim "ekranda
    /// yalnız takvim var" sayılır.
    /// </summary>
    /// <returns>
    /// Yedek; ekranlarda yalnızca Kalender'in görüntüleri duruyorsa null
    /// (kullanıcının duvar kâğıdı bilinmiyor, yedek yazılmaz). O zaman da
    /// Windows'ta resim kipi kapalıysa düz renk yedeği döner.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Ekran listesi okunamadı ya da yedek resimsiz çıkacakken ekranda ne
    /// durduğu okunamadı (bir canlı kaydın dosyası; canlı kayıt yokken aygıt
    /// yolu verilmeden okunan yol).
    /// </exception>
    internal static WallpaperBackup? YedekHazirla(IDesktopWallpaper wallpaper)
    {
        var backup = new WallpaperBackup
        {
            BackgroundColor = wallpaper.GetBackgroundColor(),
            Position = (int)wallpaper.GetPosition(),
            JpegImportQuality = ReadImportQuality(),
        };

        // Liste okunamazsa yedek yazılmaz (önceden de kayıt sayısının hatası
        // yedeği durduruyordu); yalnız ortak yolla yazılan yedek ekranların
        // kendi resimlerini kaybederdi.
        IReadOnlyList<DuvarKagidiKaydi> kayitlar = DuvarKagidiKayitlari.Oku(wallpaper)
            ?? throw new InvalidOperationException("Duvar kâğıdı ekran listesi okunamadı.");

        bool bizimkiVardi = false;

        string? ortak = (kayitlar as DuvarKagidiTablosu)?.Ortak;
        if (ZeminDosyasiMi(ortak))
        {
            bizimkiVardi = true;
        }
        else
        {
            backup.Wallpaper = ortak;
        }

        // Ekranda ne durduğunu canlı kayıtlar söyler. Canlı kayıt hiç yoksa
        // hayaletin gösterdiği dosya tek ipucu olabilir; aşağıda yalnız o zaman
        // kullanılır.
        bool canliVar = kayitlar.Any(x => x.Durum == KayitDurumu.Canli);
        bool canliOkunamadi = kayitlar.Any(x => x.Durum == KayitDurumu.Canli && x.Dosya is null);
        string? hayaletinResmi = null;

        var kimliksizResimler = new List<string>();
        foreach (DuvarKagidiKaydi kayit in kayitlar)
        {
            // Dosyası okunamayan kayıt önceden de atlanıyordu.
            if (kayit.Dosya is null)
            {
                continue;
            }

            if (kayit.Durum == KayitDurumu.Hayalet)
            {
                // Hayaletteki takvim de (1.14'teki gibi) "ekranlarda yalnız
                // takvim var" sayılır; yoksa yedek düz renk çıkardı.
                if (!canliVar)
                {
                    if (ZeminDosyasiMi(kayit.Dosya))
                    {
                        bizimkiVardi = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(kayit.Dosya))
                    {
                        hayaletinResmi ??= kayit.Dosya;
                    }
                }

                continue;
            }

            if (ZeminDosyasiMi(kayit.Dosya))
            {
                bizimkiVardi = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(kayit.Kimlik))
            {
                if (kayit.Durum == KayitDurumu.Canli && !string.IsNullOrWhiteSpace(kayit.Dosya))
                {
                    kimliksizResimler.Add(kayit.Dosya);
                }

                continue;
            }

            backup.PerMonitor[kayit.Kimlik] = kayit.Dosya;
        }

        if (string.IsNullOrWhiteSpace(backup.Wallpaper) && kimliksizResimler.Count == 1)
        {
            backup.Wallpaper = kimliksizResimler[0];
        }

        bool ResimVar() => !string.IsNullOrWhiteSpace(backup.Wallpaper)
            || backup.PerMonitor.Values.Any(v => !string.IsNullOrWhiteSpace(v));

        // Hayalet atıldığı için yedek resimsiz çıkacaksa: listede canlı kayıt
        // yok, aygıt yolu verilmeden okunan yol da boş ya da okunamadı (yalnız
        // hayaletten oluşan liste; aygıt yolu verilmeden okurken Explorer
        // hayaleti saymıyor, ölçüldü). Hayaletin gösterdiği fotoğraf ortak yola
        // yazılır: önceden yedek düz renk çıkıyordu, "Eski duvar kâğıdıma dön"
        // ya da kaldırma kullanıcının fotoğrafını düz renge çeviriyor, yedek de
        // siliniyordu (1.14 bu kaydı ekran başına yazıyordu). Geri yüklemede
        // ortak yol aygıt yolu verilmeden konur, ekrana ulaşır.
        if (!ResimVar() && hayaletinResmi is not null)
        {
            Log.Debug("Listede canlı kayıt yok; hayalet kaydın fotoğrafı yedeklendi.");
            backup.Wallpaper = hayaletinResmi;
        }

        // Hiçbir ekranda resim yoksa kullanıcı düz renk kullanıyordu.
        backup.WallpaperEnabled = ResimVar();

        // Ama bunu bilmek için ekranda ne durduğu okunabilmeli: bir canlı
        // kaydın dosyası okunamadıysa ya da canlı kayıt yokken aygıt yolu
        // verilmeden okunan yol da okunamadıysa düz renk yedeği yazılmaz,
        // sonraki yazım yeniden dener. Önceden okunamayan ortak yol "ortak resim
        // yok" sayılıyordu (EkrandakiDosyalar'da "bilinmiyor" sayılıyor); böyle
        // bir anda düz renk yedeği yazılabiliyordu.
        if (!backup.WallpaperEnabled && !bizimkiVardi && (canliOkunamadi || (!canliVar && ortak is null)))
        {
            throw new InvalidOperationException("Ekrandaki duvar kâğıdı okunamadı.");
        }

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
                // Ekranda takvimin görüntüsü yoksa önceki duvar kâğıdı zaten
                // yerinde. Ekranda görünene bakılır (bkz. EkrandaZeminVar):
                // önceden hayaletin gösterdiği takvim de sayılıyordu; Windows'un
                // listesi bozukken ekranda fotoğraf dururken "geri yüklenemedi"
                // uyarısı çıkıyordu, yazılacak ekran da bulunmuyordu.
                HashSet<string>? ekrandakiler = ClearCanvasFiles(null);
                bool? zeminGorunuyor = ekrandakiler is null ? null : EkrandaZeminVar();
                eksikKaldi = zeminGorunuyor is null
                    || (zeminGorunuyor == true && !DuzRenkSuruyor(ekrandakiler, DuzRenkDosyasi));
                return !eksikKaldi;
            }

            var backup = JsonSerializer.Deserialize<WallpaperBackup>(
                File.ReadAllText(yedekDosyasi, Encoding.UTF8));
            if (backup is null)
            {
                return false;
            }

            var wallpaper = DuvarKagidiKayitlari.Ac();
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
                eksikKaldi = !EkranlariGeriYukle(wallpaper, backup, BagliEkranSayisiOkuyucu(), out int konan);
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
    ///
    /// Windows'un listesi bozuksa (bkz. <see cref="DuvarKagidiKayitlari"/>)
    /// yazım takvimdeki gibi yapılır (bkz. <see cref="BozukListeyeGeriYukle"/>).
    /// Eski yedeklerde kimliği boş kaydın "" anahtarı ortak yol gibi okunur.
    /// </summary>
    /// <param name="bagliEkranSayisi">
    /// Bağlı ekran sayısı; bozuk listede aygıt yolu verilmeden yazmak (bütün
    /// ekranlara) yalnız tek ekranda doğru. Sıfırsa canlı kayıtlardan tahmin edilir.
    /// </param>
    /// <param name="konan">Duvar kâğıdı geri konan ekran sayısı.</param>
    /// <returns>Her ekran geri yüklendiyse true.</returns>
    internal static bool EkranlariGeriYukle(
        IDesktopWallpaper wallpaper, WallpaperBackup backup, int bagliEkranSayisi, out int konan)
    {
        konan = 0;
        wallpaper.SetPosition((DesktopWallpaperPosition)backup.Position);

        // Yalnız aygıt yolu olan ekranlar; 1.14'ün yazdığı "" anahtarı hiçbir
        // ekranı göstermez (ona SetWallpaper("") yapılmaz), ortak yol yoksa onun yerine geçer.
        var ekranBasina = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? bosAnahtar = null;
        foreach ((string id, string yol) in backup.PerMonitor)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                ekranBasina[id] = yol;
            }
            else if (!string.IsNullOrWhiteSpace(yol))
            {
                bosAnahtar ??= yol;
            }
        }

        string? ortak = string.IsNullOrWhiteSpace(backup.Wallpaper) ? bosAnahtar : backup.Wallpaper;
        if (ekranBasina.Count == 0)
        {
            bool kondu = ortak is not null && EkranaKoy(wallpaper, null, ortak);
            konan = kondu ? 1 : 0;
            return kondu;
        }

        string? yerine = new[] { ortak }.Concat(backup.PerMonitor.Values).FirstOrDefault(DosyasiVar);

        IReadOnlyList<DuvarKagidiKaydi>? kayitlar = DuvarKagidiKayitlari.Oku(wallpaper);
        if (kayitlar is not null && !DuvarKagidiKayitlari.Saglikli(kayitlar))
        {
            return BozukListeyeGeriYukle(wallpaper, kayitlar, ekranBasina, ortak, yerine, bagliEkranSayisi, out konan);
        }

        // Yedekteki ekranlar ve yedekte olmayan, şu an bağlı ekranlar. Liste
        // sağlıklı: hayalet kayıt yok, kimlikler dolu ve tekil (bkz. Saglikli).
        var ekranlar = new List<string>(ekranBasina.Keys);
        if (kayitlar is null)
        {
            Log.Debug("Monitör aygıt yolları okunamadı.");
        }
        else
        {
            ekranlar.AddRange(kayitlar.Select(x => x.Kimlik)
                .Where(id => !string.IsNullOrWhiteSpace(id) && !ekranBasina.ContainsKey(id)));
        }

        bool hepsi = true;
        foreach (string id in ekranlar)
        {
            if (EkraniGeriKoy(wallpaper, id, ekranBasina, yerine))
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

    /// <summary>
    /// Windows'un listesi bozukken yedeği geri koyar (bkz. <see cref="EkranlariGeriYukle"/>).
    /// Yazım yolu takvimdeki gibidir (bkz. <see cref="DuvarKagidiKayitlari.Coz"/>
    /// ve <see cref="WallpaperCanvas.Apply"/>).
    ///
    /// Tek ekranda önceki duvar kâğıdı aygıt yolu verilmeden konur (bozuk
    /// listede de ekrana ulaştığı ölçüldü). Önceden bağlı ekran sayısına
    /// bakılmıyordu: iki ekranlı düzende tek canlı kayıt varken de öyle konuyor,
    /// iki ekran aynı fotoğrafı alıyor, öbür ekranın fotoğrafı hiç denenmiyor,
    /// sonuç yine "tam" çıkıp yedek siliniyordu.
    ///
    /// Çok ekranda kimliği dolu canlı kayıtlara kimlikleriyle konur. Kimliği boş
    /// tek bir canlı kayıt varsa (o ekrana ancak aygıt yolu verilmeden
    /// ulaşılabiliyor) ve her bağlı ekranın canlı kaydı varsa önce o ekranın
    /// yolu aygıt yolu verilmeden (bütün ekranlara), ardından öbürleri kendi
    /// kayıtlarına konur. Önceden o ekrana hiç yazılmıyordu; takvimsiz zemin de
    /// yazılamadığı için (yalnız o ekran yazılırken aygıt yolu verilmez) ekranda
    /// takvim donmuş kalıyordu. Kimliği boş birden çok canlı kayıt varsa ya da
    /// bir bağlı ekranın canlı kaydı yoksa sonuç eksiktir.
    ///
    /// Hayalet ve bağlı olmayan kayıtlara da kendi kimlikleriyle konur, başarı
    /// sayılmaz: bağlı olmayan haricî ekran takılınca, hayalet de liste düzelince
    /// (oturum yeniden açılınca hayaletin aygıt yolu ekranın gerçek kaydı oluyor)
    /// kendi fotoğrafını göstersin. Önceden aygıt yolu verilmeden yazım bağlı
    /// olmayan ekranın kaydına da öbür ekranın fotoğrafını koyuyordu, hayalet de
    /// takvimin zeminini göstermeyi sürdürüyordu; yedek silindiği için geri
    /// dönülecek bir şey kalmıyordu. Hayalete yazmak hatasız kabul ediliyor ama
    /// ekrana çıkmıyor (1.14 iki gün boyunca oraya yazdı).
    /// </summary>
    /// <param name="bagliEkranSayisi">Bağlı ekran sayısı; sıfırsa canlı kayıtlardan tahmin edilir.</param>
    private static bool BozukListeyeGeriYukle(
        IDesktopWallpaper wallpaper,
        IReadOnlyList<DuvarKagidiKaydi> kayitlar,
        Dictionary<string, string> ekranBasina,
        string? ortak,
        string? yerine,
        int bagliEkranSayisi,
        out int konan)
    {
        konan = 0;
        List<DuvarKagidiKaydi> canlilar = kayitlar.Where(x => x.Durum == KayitDurumu.Canli).ToList();
        int kimliksiz = canlilar.Count(x => string.IsNullOrWhiteSpace(x.Kimlik));
        var kimlikliler = new HashSet<string>(
            canlilar.Select(x => x.Kimlik).Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.OrdinalIgnoreCase);
        int bagli = bagliEkranSayisi > 0 ? bagliEkranSayisi : Math.Max(1, canlilar.Count);
        Log.Debug("Duvar kâğıdı ekran listesi tutarsız; önceki duvar kâğıdı "
            + (bagli <= 1 ? "aygıt yolu verilmeden" : "canlı kayıtlara")
            + $" geri konuyor ({bagli} bağlı ekran). Kayıtlar: " + DuvarKagidiKayitlari.Ozet(kayitlar));

        bool hepsi;
        string? aygitsiz = null;
        if (bagli <= 1)
        {
            // Sıra: ekrandaki kaydın kimliğiyle yedeklenen yol, dosyası olan ilk
            // ekranın yolu, ortak yol.
            string? kendi = canlilar.Count == 1 && ekranBasina.TryGetValue(canlilar[0].Kimlik, out string? k) ? k : null;
            aygitsiz = AygitsizKoy(wallpaper, new[] { kendi }.Concat(ekranBasina.Values).Append(ortak));
            hepsi = aygitsiz is not null;
            konan = hepsi ? 1 : 0;
        }
        else
        {
            hepsi = canlilar.Count >= bagli && kimliksiz <= 1;
            if (kimliksiz == 1 && hepsi)
            {
                // Kimliği boş ekranın yolu: ortak yol (yedek bozuk listede
                // alındıysa o ekranın fotoğrafı oraya yazıldı), yoksa hayaletin
                // kimliğiyle yedeklenen yol (hayalet o ekranın gerçek aygıt yolunu
                // taşıyor), yoksa yerine geçecek yol.
                IEnumerable<string?> hayaletlerin = kayitlar
                    .Where(x => x.Durum == KayitDurumu.Hayalet && !kimlikliler.Contains(x.Kimlik))
                    .Select(x => ekranBasina.TryGetValue(x.Kimlik, out string? y) ? y : null);
                aygitsiz = AygitsizKoy(wallpaper, new[] { ortak }.Concat(hayaletlerin).Append(yerine));
                if (aygitsiz is not null)
                {
                    konan++;
                }
                else
                {
                    hepsi = false;
                }
            }

            foreach (string id in kimlikliler)
            {
                if (EkraniGeriKoy(wallpaper, id, ekranBasina, yerine))
                {
                    konan++;
                }
                else
                {
                    hepsi = false;
                }
            }
        }

        // Hayalet ve bağlı olmayan kayıtlar: başarı sayılmaz. Tek ekranda yerine
        // geçecek yol aygıt yolu verilmeden konan yoldur (ekranın kendisi odur).
        string? kalanlarinYerine = bagli <= 1 ? aygitsiz : yerine;
        foreach (string id in kayitlar
            .Where(x => x.Durum != KayitDurumu.Canli && !string.IsNullOrWhiteSpace(x.Kimlik) && !kimlikliler.Contains(x.Kimlik))
            .Select(x => x.Kimlik)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            EkraniGeriKoy(wallpaper, id, ekranBasina, kalanlarinYerine);
        }

        return hepsi;
    }

    /// <summary>
    /// Verilen yollardan ilkini aygıt yolu verilmeden (bütün ekranlara) koyar.
    /// Dosyası olmayan yol denenmez; hiçbirinin dosyası yoksa ilki uyarıyla
    /// denenir (günlükte görünsün).
    /// </summary>
    /// <returns>Konan yol; hiçbiri konamadıysa null.</returns>
    private static string? AygitsizKoy(IDesktopWallpaper wallpaper, IEnumerable<string?> yollar)
    {
        List<string> adaylar = yollar
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> dosyasiOlanlar = adaylar.Where(DosyasiVar).ToList();
        foreach (string aday in dosyasiOlanlar.Count > 0 ? dosyasiOlanlar : adaylar.Take(1))
        {
            if (EkranaKoy(wallpaper, null, aday))
            {
                return aday;
            }
        }

        return null;
    }

    /// <summary>
    /// Bir ekrana önce yedekteki kendi yolunu, konamazsa yerine geçecek yolu
    /// koyar (bkz. <see cref="EkranlariGeriYukle"/>).
    /// </summary>
    /// <returns>Ekrana bir yol kondu mu?</returns>
    private static bool EkraniGeriKoy(
        IDesktopWallpaper wallpaper, string id, Dictionary<string, string> ekranBasina, string? yerine)
    {
        string? kendi = ekranBasina.TryGetValue(id, out string? k) && !string.IsNullOrWhiteSpace(k) ? k : null;
        return (kendi is not null && EkranaKoy(wallpaper, id, kendi))
            || (yerine is not null
                && !string.Equals(yerine, kendi, StringComparison.OrdinalIgnoreCase)
                && EkranaKoy(wallpaper, id, yerine));
    }

    private static bool DosyasiVar(string? yol)
        => !string.IsNullOrWhiteSpace(yol) && File.Exists(Environment.ExpandEnvironmentVariables(yol));

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
            wallpaper ??= DuvarKagidiKayitlari.Ac();
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
    ///
    /// Windows'un listesindeki hayalet kayıtlara bakılmaz: gösterdikleri dosya
    /// hiçbir ekranda görünmüyor. Bozuk listede hayaletteki takvim (1.14 oraya
    /// yazıyordu) açılışta gereksiz bir geri yükleme başlatırdı. Bağlı olmayan
    /// ekranın kaydı sayılır: o ekran takılınca bu dosyayı gösterecek;
    /// kaldırmada zemin klasörü bu yüzden korunur. Canlı kayıt yoksa ya da bir
    /// canlı kaydın dosyası okunamadıysa aygıt yolu verilmeden okunan dosyaya
    /// da bakılır. Silmeden korumada bütün kayıtlara bakmayı
    /// <see cref="EkrandakiDosyalar"/> ve <see cref="ZeminKlasoruKorunmali"/> sürdürür.
    /// </summary>
    /// <param name="wallpaper">Ekrandakileri okumak için; null ise burada açılır.</param>
    /// <returns>Ekrandakiler okunamazsa null.</returns>
    internal static bool? EkrandaZeminVar(IDesktopWallpaper? wallpaper = null)
    {
        try
        {
            wallpaper ??= DuvarKagidiKayitlari.Ac();
            IReadOnlyList<DuvarKagidiKaydi>? kayitlar = DuvarKagidiKayitlari.Oku(wallpaper);
            if (kayitlar is null)
            {
                return null;
            }

            bool bilinmiyor = false;
            bool canliVar = false;
            bool canliOkunamadi = false;
            foreach (DuvarKagidiKaydi kayit in kayitlar)
            {
                if (kayit.Durum == KayitDurumu.Hayalet)
                {
                    continue;
                }

                bool canli = kayit.Durum == KayitDurumu.Canli;
                canliVar |= canli;
                if (kayit.Dosya is null)
                {
                    canliOkunamadi |= canli;
                    bilinmiyor = true;
                    continue;
                }

                if (ZeminDosyasiMi(kayit.Dosya))
                {
                    return true;
                }
            }

            if (!canliVar || canliOkunamadi)
            {
                string? ortak = (kayitlar as DuvarKagidiTablosu)?.Ortak;
                if (ZeminDosyasiMi(ortak))
                {
                    return true;
                }

                bilinmiyor |= ortak is null;
            }

            return bilinmiyor ? null : false;
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıtları okunamadı: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Kaldırmada zemin klasörü silinmeden kalmalı mı: Windows'un listesindeki
    /// herhangi bir kayıt (hayalet ve bağlı olmayan dâhil; bkz.
    /// <see cref="EkrandakiDosyalar"/>) takvimin zeminini gösteriyorsa ya da
    /// ekrandakiler okunamıyorsa. <see cref="EkrandaZeminVar"/> yalnız ekranda
    /// görüneni sorar, hayaleti saymaz; kaldırmada o kullanılıyordu. Windows'un
    /// listesi bozukken hayalet takvimin zeminini göstermeyi sürdürürse liste
    /// düzelince (oturum yeniden açılınca hayaletin aygıt yolu ekranın gerçek
    /// kaydı oluyor) ekran silinmiş dosyayı göstermeye çalışırdı; Kalender
    /// kaldırıldığı için bunu düzelten de kalmazdı. Okunamayan durumda da klasör
    /// kalır: temizlikte de emin olunamayan yerde dosya silinmiyor.
    /// </summary>
    /// <param name="wallpaper">Ekrandakileri okumak için; null ise burada açılır.</param>
    internal static bool ZeminKlasoruKorunmali(IDesktopWallpaper? wallpaper = null)
    {
        try
        {
            wallpaper ??= DuvarKagidiKayitlari.Ac();
            return EkrandakiDosyalar(wallpaper)?.Any(ZeminDosyasiMi) ?? true;
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıtları okunamadı: " + ex.Message);
            return true;
        }
    }

    /// <summary>
    /// Ekranlarda şu an duran duvar kâğıdı dosyaları: Windows'un listesindeki
    /// bütün kayıtların (hayalet, bağlı olmayan, kimliği boş olanlar dâhil)
    /// gösterdiği dosyalar ve aygıt yolu verilmeden okunan dosya; okunamazsa
    /// null. Dosyası okunamayan hayalet kayıt atlanır: hiçbir ekranda
    /// görünmüyor. Önceden tek bir kaydın okuma hatası bütün sonucu bilinmez
    /// yapıyordu; takvim yazıldıkça her yazım büyük bir dosya bırakıyor, hiçbiri
    /// silinmiyordu (temizlik ekranı okuyamayınca hiçbir dosyaya dokunmuyor).
    /// Canlı ya da bağlı olmayan bir kaydın dosyası okunamazsa yine null.
    /// </summary>
    internal static HashSet<string>? EkrandakiDosyalar(IDesktopWallpaper wallpaper)
        => EkrandakiDosyalariOku(wallpaper, null);

    /// <summary>
    /// <see cref="EkrandakiDosyalar"/>; <paramref name="okunamayanBagliOlmayanlar"/>
    /// verilirse dosyası okunamayan, kimliği dolu bağlı olmayan kayıt sonucu
    /// bilinmez yapmaz, kimliği bu listeye eklenir. Olağan temizlik bunu
    /// kullanır (bkz. <see cref="WallpaperCanvas.KullanilmayanlariSil"/>).
    /// </summary>
    internal static HashSet<string>? EkrandakiDosyalariOku(IDesktopWallpaper wallpaper, List<string>? okunamayanBagliOlmayanlar)
    {
        try
        {
            IReadOnlyList<DuvarKagidiKaydi>? kayitlar = DuvarKagidiKayitlari.Oku(wallpaper);
            if (kayitlar is null)
            {
                return null;
            }

            var dosyalar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DuvarKagidiKaydi kayit in kayitlar)
            {
                if (kayit.Dosya is null)
                {
                    if (kayit.Durum == KayitDurumu.Hayalet)
                    {
                        continue;
                    }

                    if (okunamayanBagliOlmayanlar is not null
                        && kayit.Durum == KayitDurumu.BagliDegil
                        && !string.IsNullOrWhiteSpace(kayit.Kimlik))
                    {
                        okunamayanBagliOlmayanlar.Add(kayit.Kimlik);
                        continue;
                    }

                    return null;
                }

                if (!string.IsNullOrWhiteSpace(kayit.Dosya))
                {
                    dosyalar.Add(kayit.Dosya);
                }
            }

            string? ortak = (kayitlar as DuvarKagidiTablosu)?.Ortak;
            if (ortak is null)
            {
                return null;
            }

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
