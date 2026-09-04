using System.Drawing;
using System.IO;
using System.Text;
using System.Text.Json;
using Kalender.Core;
using Kalender.Interop;
using Kalender.Settings;

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

    /// <summary>Monitör aygıt yolu -> duvar kağıdı yolu.</summary>
    public Dictionary<string, string> PerMonitor { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Windows'un gerçek duvar kağıdını Kalender'in zeminiyle eşitler.
///
/// Amaç, oturum açılışında Kalender devreye girene kadar geçen bir iki
/// saniyede ekranın farklı görünmemesi. Buraya takvim çizilmez, yalnızca zemin.
/// Kullanıcının önceki duvar kağıdı ilk değişiklikten önce yedeklenir ve
/// kaldırma sırasında geri yüklenir.
/// </summary>
internal sealed class WallpaperService
{
    private readonly SettingsStore _settings;

    public WallpaperService(SettingsStore settings)
    {
        _settings = settings;
    }

    /// <summary>Ayarlardaki zemini Windows duvar kağıdına yazar.</summary>
    /// <returns>
    /// Duvar kağıdı gerçekten değiştiyse true. Bu önemli: kabuk duvar kağıdı
    /// değişince WorkerW'yi yıkıp yeniden kuruyor, dolayısıyla çağıran taraf
    /// masaüstü katmanını biraz bekleyip kurmalı.
    /// </returns>
    public bool Apply()
    {
        AppSettings settings = _settings.Current;
        if (!settings.SyncSystemWallpaper)
        {
            return false;
        }

        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            EnsureBackup(wallpaper);

            string signature = BuildSignature(settings);

            // Zaten istediğimiz durumdaysa hiç dokunmuyoruz. Her açılışta
            // gereksiz yere duvar kağıdı yazmak masaüstünü sarsıyor.
            if (string.Equals(settings.LastAppliedBackground, signature, StringComparison.Ordinal)
                && LooksApplied(wallpaper, settings))
            {
                Log.Debug("Windows duvar kağıdı zaten güncel; değiştirilmedi.");
                return false;
            }

            if (settings.Appearance.BackgroundMode == BackgroundMode.Photo)
            {
                ApplyPhoto(wallpaper, settings);
            }
            else
            {
                ApplySolid(wallpaper, settings);
            }

            _settings.Update(s => s.LastAppliedBackground = signature);

            Log.Info("Windows duvar kağıdı zeminle eşitlendi.");
            return true;
        }
        catch (Exception ex)
        {
            // Duvar kağıdı yazılamazsa uygulama çalışmaya devam eder;
            // yalnızca açılıştaki bir iki saniyelik boşlukta eski zemin görünür.
            Log.Warn("Windows duvar kağıdı güncellenemedi.", ex);
            return false;
        }
    }

    /// <summary>Ayarların duvar kağıdını etkileyen kısmının özeti.</summary>
    private static string BuildSignature(AppSettings settings)
    {
        AppearanceSettings a = settings.Appearance;
        if (a.BackgroundMode != BackgroundMode.Photo)
        {
            return "duz:" + a.SolidColor;
        }

        var parcalar = new List<string> { "foto:" + a.PhotoFit, a.PhotoPath ?? string.Empty };
        foreach ((string id, MonitorPlacement placement) in settings.Monitors.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(placement.PhotoPath))
            {
                parcalar.Add(id + "=" + placement.PhotoPath);
            }
        }

        return string.Join("|", parcalar);
    }

    /// <summary>
    /// Kabaca bir doğrulama: imza tutsa bile kullanıcı arada duvar kağıdını
    /// değiştirmiş olabilir. Düz renkte zemin rengine, fotoğrafta ilk ekranın
    /// yoluna bakıyoruz.
    /// </summary>
    private static bool LooksApplied(IDesktopWallpaper wallpaper, AppSettings settings)
    {
        try
        {
            if (settings.Appearance.BackgroundMode != BackgroundMode.Photo)
            {
                Color color = Rendering.ColorTools.Parse(
                    settings.Appearance.SolidColor, Color.FromArgb(0x2E, 0x32, 0x38));
                return wallpaper.GetBackgroundColor() == ToColorRef(color);
            }

            uint count = wallpaper.GetMonitorDevicePathCount();
            if (count == 0)
            {
                return false;
            }

            string id = wallpaper.GetMonitorDevicePathAt(0);
            string current = wallpaper.GetWallpaper(id) ?? string.Empty;
            string? expected = FindPhotoFor(settings, id) ?? settings.Appearance.PhotoPath;

            return !string.IsNullOrWhiteSpace(expected)
                && string.Equals(current, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kağıdı durumu okunamadı: " + ex.Message);
            return false;
        }
    }

    private static void ApplySolid(IDesktopWallpaper wallpaper, AppSettings settings)
    {
        Color color = Rendering.ColorTools.Parse(settings.Appearance.SolidColor, Color.FromArgb(0x2E, 0x32, 0x38));
        wallpaper.SetBackgroundColor(ToColorRef(color));

        // Resim yolunu boşaltmak yetmiyor: Windows o durumda varsayılan duvar
        // kağıdına düşüyor. Doğru yol, duvar kağıdı resmini tamamen kapatmak;
        // o zaman zemin rengi görünür.
        wallpaper.Enable(false);
    }

    private static void ApplyPhoto(IDesktopWallpaper wallpaper, AppSettings settings)
    {
        Color color = Rendering.ColorTools.Parse(settings.Appearance.SolidColor, Color.Black);
        wallpaper.SetBackgroundColor(ToColorRef(color));

        wallpaper.Enable(true);
        wallpaper.SetPosition(ToPosition(settings.Appearance.PhotoFit));

        uint count = wallpaper.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            string id = wallpaper.GetMonitorDevicePathAt(i);

            string? path = FindPhotoFor(settings, id) ?? settings.Appearance.PhotoPath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                wallpaper.SetWallpaper(id, path);
            }
        }
    }

    private static string? FindPhotoFor(AppSettings settings, string monitorDevicePath)
    {
        foreach ((string id, MonitorPlacement placement) in settings.Monitors)
        {
            if (!string.IsNullOrWhiteSpace(placement.PhotoPath)
                && string.Equals(id, monitorDevicePath, StringComparison.OrdinalIgnoreCase))
            {
                return placement.PhotoPath;
            }
        }

        return null;
    }

    /// <summary>İlk değişiklikten önce mevcut durumu bir kez yedekler.</summary>
    private static void EnsureBackup(IDesktopWallpaper wallpaper)
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
            };

            try
            {
                backup.Wallpaper = wallpaper.GetWallpaper(null);
            }
            catch (Exception ex)
            {
                Log.Debug("Ortak duvar kağıdı okunamadı: " + ex.Message);
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
                    Log.Debug($"Monitör duvar kağıdı okunamadı ({id}): {ex.Message}");
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

            Log.Info($"Önceki duvar kağıdı yedeklendi (resim: {backup.WallpaperEnabled}).");
        }
        catch (Exception ex)
        {
            Log.Warn("Duvar kağıdı yedeklenemedi.", ex);
        }
    }

    /// <summary>Yedeklenen duvar kağıdını geri yükler. Kaldırma sırasında çağrılır.</summary>
    public static bool Restore()
    {
        try
        {
            if (!File.Exists(Paths.BackupFile))
            {
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
                wallpaper.Enable(true);
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
            }

            // Kalender kapalıyken de çağrılabildiği için masaüstünde asılı
            // kalmış bir çizim varsa burada temizlenmiş olur.
            DesktopRefresher.ForceRedraw();

            Log.Info("Önceki duvar kağıdı geri yüklendi.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("Duvar kağıdı geri yüklenemedi.", ex);
            return false;
        }
    }

    private static uint ToColorRef(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));

    private static DesktopWallpaperPosition ToPosition(PhotoFit fit) => fit switch
    {
        PhotoFit.Fit => DesktopWallpaperPosition.Fit,
        PhotoFit.Center => DesktopWallpaperPosition.Center,
        PhotoFit.Tile => DesktopWallpaperPosition.Tile,
        _ => DesktopWallpaperPosition.Fill,
    };
}
