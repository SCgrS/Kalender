using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Kalender.Desktop;
using Kalender.Interop;
using Microsoft.Win32;

namespace Kalender.Core;

/// <summary>Kurulum ya da kaldırma sonucunun kullanıcıya anlatılacak hâli.</summary>
/// <param name="Success">İşlem başarılı mı?</param>
/// <param name="Message">Türkçe açıklama.</param>
internal sealed record SetupResult(bool Success, string Message);

/// <summary>
/// Kalender'i bu bilgisayara kurar ve kaldırır.
///
/// Her şey kullanıcının kendi alanında olur: dosyalar %LOCALAPPDATA% altına,
/// kayıtlar HKCU'ya yazılır. Yönetici hakkı hiçbir adımda istenmez.
///
/// Ayrı bir kurulum programı yok: yayınlanan tek dosya hem uygulama hem kurucu.
/// "Kalender-Kurulum.exe" adıyla çalıştırıldığında kendini kurulum klasörüne
/// kopyalar. Böylece indirilecek tek bir dosya kalıyor.
/// </summary>
internal static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Kalender";
    private const string ShortcutName = "Kalender.lnk";

    /// <summary>Uygulama dosyalarının durduğu klasör. Ayarlar ve önbellek bunun dışındadır.</summary>
    public static string AppFolder => Path.Combine(Paths.RootFolder, "app");

    public static string InstalledExe => Path.Combine(AppFolder, "Kalender.exe");

    /// <summary>Şu an çalışan dosya kurulu sürüm mü?</summary>
    public static bool RunningInstalled =>
        string.Equals(Environment.ProcessPath, InstalledExe, StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(InstalledExe);

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);

    // ------------------------------------------------------------- kurulum

    public static SetupResult Install()
    {
        try
        {
            string? source = Environment.ProcessPath;
            if (string.IsNullOrEmpty(source))
            {
                return new SetupResult(false, "Çalıştırılabilir dosyanın yolu okunamadı.");
            }

            if (RunningInstalled)
            {
                return new SetupResult(false, "Kalender zaten kurulu ve bu dosyadan çalışıyor.");
            }

            // Kurulu sürüm açıksa dosyayı değiştiremeyiz; önce kibarca kapatıyoruz.
            AppInstance.StopRunning();

            Directory.CreateDirectory(AppFolder);
            File.Copy(source, InstalledExe, overwrite: true);
            RemoveMarkOfTheWeb(InstalledExe);

            CreateShortcut();
            StartupManager.Apply(true, InstalledExe);
            WriteUninstallEntry();

            Log.Info("Kalender kuruldu: " + InstalledExe);
            return new SetupResult(true, "Kalender kuruldu ve başlatılıyor.");
        }
        catch (Exception ex)
        {
            Log.Error("Kurulum başarısız.", ex);
            return new SetupResult(false, "Kurulum yapılamadı: " + ex.Message);
        }
    }

    /// <summary>
    /// Dosyadaki "internetten indirildi" işaretini (Zone.Identifier akışı) siler.
    ///
    /// Tarayıcı, indirilen kurucuya bu işareti koyar; kullanıcı SmartScreen
    /// uyarısını bir kez geçer. File.Copy işareti kurulu Kalender.exe'ye de
    /// taşıdığı için Windows kurulu kopyayı da her açılışta yeni indirilmiş bir
    /// dosya gibi görüyor ve "çalıştırılsın mı?" diye soruyordu. Onay kurucu
    /// için zaten verildi; kurulu kopyada işaretin kalmasının anlamı yok.
    /// İşaret yoksa ya da silinemezse uygulama yine çalışır.
    /// </summary>
    public static void RemoveMarkOfTheWeb(string path)
    {
        try
        {
            if (NativeMethods.DeleteFile(path + ":Zone.Identifier"))
            {
                Log.Info("İndirme işareti kaldırıldı: " + path);
                return;
            }

            int hata = Marshal.GetLastWin32Error();
            if (hata is NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND)
            {
                return; // İşaret zaten yok.
            }

            Log.Warn($"İndirme işareti kaldırılamadı (hata {hata}): {path}");
        }
        catch (Exception ex)
        {
            Log.Warn("İndirme işareti kaldırılamadı: " + path, ex);
        }
    }

    /// <summary>Kurulan sürümü başlatır.</summary>
    public static void LaunchInstalled()
    {
        try
        {
            Process.Start(new ProcessStartInfo(InstalledExe)
            {
                UseShellExecute = true,
                WorkingDirectory = AppFolder,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Kurulan sürüm başlatılamadı.", ex);
        }
    }

    private static void CreateShortcut()
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                Log.Warn("Kısayol oluşturulamadı: WScript.Shell yok.");
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(ShortcutPath);

            link.TargetPath = InstalledExe;
            link.WorkingDirectory = AppFolder;
            link.Description = "Masaüstü duvar kağıdına işlenmiş aylık takvim";
            link.IconLocation = InstalledExe + ",0";
            link.Save();
        }
        catch (Exception ex)
        {
            // Kısayol olmazsa uygulama yine çalışır.
            Log.Warn("Başlat menüsü kısayolu oluşturulamadı.", ex);
        }
    }

    /// <summary>
    /// Windows'un "Uygulamalar ve özellikler" listesine kaydeder. Böylece
    /// kullanıcı Kalender'i alıştığı yerden de kaldırabilir.
    /// </summary>
    private static void WriteUninstallEntry()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);

            key.SetValue("DisplayName", "Kalender");
            key.SetValue("DisplayVersion", Version);
            key.SetValue("Publisher", "Kalender");
            key.SetValue("InstallLocation", AppFolder);
            key.SetValue("DisplayIcon", InstalledExe);
            key.SetValue("UninstallString", $"\"{InstalledExe}\" --kaldir");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            var info = new FileInfo(InstalledExe);
            if (info.Exists)
            {
                key.SetValue("EstimatedSize", (int)(info.Length / 1024), RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Kaldırma kaydı yazılamadı.", ex);
        }
    }

    // ------------------------------------------------------------- kaldırma

    /// <summary>
    /// Kalender'i kaldırır: duvar kağıdını geri yükler, kayıtları ve kısayolu
    /// siler, son olarak dosyaları silmek için küçük bir toplu iş bırakır
    /// (çalışan dosya kendini silemez).
    /// </summary>
    /// <param name="removeData">Ayarlar ve önbellek de silinsin mi?</param>
    public static SetupResult Uninstall(bool removeData)
    {
        try
        {
            AppInstance.StopRunning();

            // Kullanıcının Kalender'den önceki duvar kağıdı geri gelsin.
            WallpaperService.Restore();

            StartupManager.Apply(false, null);
            RemoveShortcut();
            RemoveUninstallEntry();

            ScheduleFileRemoval(removeData);

            Log.Info("Kalender kaldırıldı.");
            return new SetupResult(true, removeData
                ? "Kalender kaldırıldı. Ayarlarınız ve yerel kopyanız da silindi."
                : "Kalender kaldırıldı. Ayarlarınız bilgisayarınızda kaldı.");
        }
        catch (Exception ex)
        {
            Log.Error("Kaldırma başarısız.", ex);
            return new SetupResult(false, "Kaldırma tamamlanamadı: " + ex.Message);
        }
    }

    private static void RemoveShortcut()
    {
        try
        {
            if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Kısayol silinemedi.", ex);
        }
    }

    private static void RemoveUninstallEntry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Log.Warn("Kaldırma kaydı silinemedi.", ex);
        }
    }

    /// <summary>
    /// Çalışan dosya kendini silemediği için, biz çıktıktan birkaç saniye sonra
    /// klasörü silen küçük bir toplu iş başlatılır. Yalnızca Kalender'in kendi
    /// klasörlerine dokunur.
    /// </summary>
    private static void ScheduleFileRemoval(bool removeData)
    {
        try
        {
            string target = removeData ? Paths.RootFolder : AppFolder;

            // Güvenlik: yol beklediğimiz yerin altında değilse hiçbir şey silme.
            string root = Paths.RootFolder;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                Log.Warn("Silinecek klasör beklenen yerin dışında; işlem yapılmadı: " + target);
                return;
            }

            string batch = Path.Combine(Path.GetTempPath(), "kalender-kaldir.cmd");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("timeout /t 3 /nobreak >nul");
            sb.AppendLine($"rd /s /q \"{target}\"");
            sb.AppendLine("del \"%~f0\"");

            // Toplu iş dosyası Windows kabuğunca okunacağı için sistem kod
            // sayfasıyla yazılıyor; yollarda Türkçe karakter olabilir.
            File.WriteAllText(batch, sb.ToString(), Encoding.Default);

            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batch}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            Log.Warn("Dosya silme işi başlatılamadı.", ex);
        }
    }
}

/// <summary>Çalışan Kalender örneğini kibarca kapatmaya yarar.</summary>
internal static class AppInstance
{
    /// <summary>Çalışan örneğe kapanma isteği gönderir ve kapanmasını bekler.</summary>
    public static void StopRunning(int timeoutMs = 6000)
    {
        try
        {
            IntPtr window = NativeMethods.FindWindow(AppMessageWindow.WindowClassName, null);
            if (window == IntPtr.Zero)
            {
                return;
            }

            NativeMethods.PostMessage(window, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMs)
            {
                if (NativeMethods.FindWindow(AppMessageWindow.WindowClassName, null) == IntPtr.Zero)
                {
                    // Kapanan pencereden sonra dosya kilidinin bırakılması için kısa bir soluk.
                    Thread.Sleep(400);
                    return;
                }

                Thread.Sleep(150);
            }

            Log.Warn("Çalışan Kalender kapanmadı; devam ediliyor.");
        }
        catch (Exception ex)
        {
            Log.Warn("Çalışan Kalender kapatılamadı.", ex);
        }
    }
}
