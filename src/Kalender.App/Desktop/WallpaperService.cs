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
    public void Apply()
    {
        AppSettings settings = _settings.Current;
        if (!settings.SyncSystemWallpaper)
        {
            return;
        }

        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            EnsureBackup(wallpaper);

            if (settings.Appearance.BackgroundMode == BackgroundMode.Photo)
            {
                ApplyPhoto(wallpaper, settings);
            }
            else
            {
                ApplySolid(wallpaper, settings);
            }
        }
        catch (Exception ex)
        {
            // Duvar kağıdı yazılamazsa uygulama çalışmaya devam eder;
            // yalnızca açılıştaki bir iki saniyelik boşlukta eski zemin görünür.
            Log.Warn("Windows duvar kağıdı güncellenemedi.", ex);
        }
    }

    private static void ApplySolid(IDesktopWallpaper wallpaper, AppSettings settings)
    {
        Color color = Rendering.ColorTools.Parse(settings.Appearance.SolidColor, Color.FromArgb(0x2E, 0x32, 0x38));
        wallpaper.SetBackgroundColor(ToColorRef(color));

        // Resim kaldırılınca zemin rengi görünür.
        uint count = wallpaper.GetMonitorDevicePathCount();
        for (uint i = 0; i < count; i++)
        {
            string id = wallpaper.GetMonitorDevicePathAt(i);
            wallpaper.SetWallpaper(id, string.Empty);
        }
    }

    private static void ApplyPhoto(IDesktopWallpaper wallpaper, AppSettings settings)
    {
        Color color = Rendering.ColorTools.Parse(settings.Appearance.SolidColor, Color.Black);
        wallpaper.SetBackgroundColor(ToColorRef(color));
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
            else
            {
                wallpaper.SetWallpaper(id, string.Empty);
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

            Directory.CreateDirectory(Paths.RootFolder);
            File.WriteAllText(
                Paths.BackupFile,
                JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));

            Log.Info("Önceki duvar kağıdı yedeklendi.");
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
            wallpaper.SetPosition((DesktopWallpaperPosition)backup.Position);

            if (backup.PerMonitor.Count > 0)
            {
                foreach ((string id, string path) in backup.PerMonitor)
                {
                    wallpaper.SetWallpaper(id, path ?? string.Empty);
                }
            }
            else if (!string.IsNullOrWhiteSpace(backup.Wallpaper))
            {
                wallpaper.SetWallpaper(null, backup.Wallpaper);
            }

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
