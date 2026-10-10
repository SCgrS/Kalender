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
/// </summary>
internal static class WallpaperService
{
    private const string DesktopKey = @"Control Panel\Desktop";
    private const string QualityValue = "JPEGImportQuality";

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

    /// <summary>İlk değişiklikten önce mevcut durumu bir kez yedekler.</summary>
    internal static void EnsureBackup(IDesktopWallpaper wallpaper)
    {
        if (File.Exists(Paths.BackupFile))
        {
            return;
        }

        try
        {
            var backup = new WallpaperBackup
            {
                BackgroundColor = wallpaper.GetBackgroundColor(),
                Position = (int)wallpaper.GetPosition(),
                JpegImportQuality = ReadImportQuality(),
            };

            try
            {
                backup.Wallpaper = wallpaper.GetWallpaper(null);
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
                    backup.PerMonitor[id] = wallpaper.GetWallpaper(id) ?? string.Empty;
                }
                catch (Exception ex)
                {
                    Log.Debug($"Monitör duvar kâğıdı okunamadı ({id}): {ex.Message}");
                }
            }

            // Hiçbir ekranda resim yoksa kullanıcı düz renk kullanıyordu.
            backup.WallpaperEnabled = !string.IsNullOrWhiteSpace(backup.Wallpaper)
                || backup.PerMonitor.Values.Any(v => !string.IsNullOrWhiteSpace(v));

            Directory.CreateDirectory(Paths.RootFolder);
            File.WriteAllText(
                Paths.BackupFile,
                JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));

            Log.Info($"Önceki duvar kâğıdı yedeklendi (resim: {backup.WallpaperEnabled}).");
        }
        catch (Exception ex)
        {
            Log.Warn("Duvar kâğıdı yedeklenemedi.", ex);
        }
    }

    /// <summary>Yedeklenen duvar kağıdını geri yükler. Kaldırma sırasında da çağrılır.</summary>
    public static bool Restore()
    {
        try
        {
            if (!File.Exists(Paths.BackupFile))
            {
                // Yedek yoksa bile bizim ürettiğimiz görüntüler ortada kalmasın.
                ClearCanvasFiles();
                return false;
            }

            var backup = JsonSerializer.Deserialize<WallpaperBackup>(
                File.ReadAllText(Paths.BackupFile, Encoding.UTF8));
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

            if (!hadImage)
            {
                // Kullanıcı düz renk kullanıyordu.
                wallpaper.Enable(false);
            }
            else
            {
                wallpaper.SetPosition((DesktopWallpaperPosition)backup.Position);

                if (backup.PerMonitor.Count > 0)
                {
                    foreach ((string id, string path) in backup.PerMonitor)
                    {
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            wallpaper.SetWallpaper(id, path);
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(backup.Wallpaper))
                {
                    wallpaper.SetWallpaper(null, backup.Wallpaper);
                }

                // Resim kipi ancak eski yol geri konduktan sonra açılabiliyor.
                EnableImages(wallpaper);
            }

            RestoreImportQuality(backup.JpegImportQuality);
            ClearCanvasFiles();

            // Kalender kapalıyken de çağrılabildiği için kabuk duvar kağıdını
            // yeniden okusun diye açıkça tazeliyoruz.
            DesktopRefresher.ForceRedraw();

            Log.Info("Önceki duvar kâğıdı geri yüklendi.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Duvar kâğıdı geri yüklenemedi.", ex);
            return false;
        }
    }

    /// <summary>Kalender'in ürettiği zemin görüntülerini siler.</summary>
    private static void ClearCanvasFiles()
    {
        try
        {
            if (!Directory.Exists(Paths.CanvasFolder))
            {
                return;
            }

            // Uzantı verilmiyor: 1.4.4'e kadar PNG, sonra BMP yazıldı; ikisi de silinsin.
            foreach (string file in Directory.EnumerateFiles(Paths.CanvasFolder, "zemin-*"))
            {
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
