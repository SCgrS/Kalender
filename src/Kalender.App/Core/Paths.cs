using System.IO;

namespace Kalender.Core;

/// <summary>
/// Uygulamanın kullandığı bütün dosya yollarını tek yerden verir.
/// Her şey %LOCALAPPDATA%\Kalender altında toplanır.
/// </summary>
internal static class Paths
{
    public static string RootFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Kalender");

    public static string SettingsFile => Path.Combine(RootFolder, "ayarlar.json");

    public static string CacheFolder => Path.Combine(RootFolder, "cache");

    public static string LogFolder => Path.Combine(RootFolder, "logs");

    public static string BackupFile => Path.Combine(RootFolder, "onceki-duvar-kagidi.json");

    /// <summary>Klasörler yoksa oluşturur. Uygulama açılışında bir kez çağrılır.</summary>
    public static void EnsureFolders()
    {
        Directory.CreateDirectory(RootFolder);
        Directory.CreateDirectory(CacheFolder);
        Directory.CreateDirectory(LogFolder);
    }
}
