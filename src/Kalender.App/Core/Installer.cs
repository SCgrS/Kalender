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
    private const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    /// <summary>Kaldırmada RunOnce altına yazılan yedek silme kayıtlarının ad öneki.</summary>
    private const string CleanupValuePrefix = "KalenderTemizlik";

    /// <summary>Uygulama dosyalarının durduğu klasör. Ayarlar ve önbellek bunun dışındadır.</summary>
    public static string AppFolder => Path.Combine(Paths.RootFolder, "app");

    public static string InstalledExe => Path.Combine(AppFolder, "Kalender.exe");

    /// <summary>Şu an çalışan dosya kurulu sürüm mü?</summary>
    public static bool RunningInstalled =>
        string.Equals(Environment.ProcessPath, InstalledExe, StringComparison.OrdinalIgnoreCase);

    public static bool IsInstalled => File.Exists(InstalledExe);

    public static string Version =>
        VersionText(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0));

    /// <summary>
    /// Sürümün kullanıcıya gösterilen yazılışı. Sürüm numaraları iki bölümlüdür
    /// (1.9); üçüncü bölüm yalnızca sıfırdan büyükse yazılır (eski 1.6.4 gibi).
    /// </summary>
    public static string VersionText(Version v) => v.Build > 0 ? v.ToString(3) : v.ToString(2);

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

            // Önceki bir kaldırmadan oturum açılışına ertelenmiş silme kalmışsa
            // iptal et; yoksa bu yeni kurulum bir sonraki açılışta silinirdi.
            RemovePendingCleanup();

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
    /// <param name="args">Komut satırı anahtarları (boşluksuz, tırnaksız).</param>
    public static void LaunchInstalled(params string[] args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(InstalledExe)
            {
                UseShellExecute = true,
                WorkingDirectory = AppFolder,
                Arguments = string.Join(" ", args),
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
            UpdateService.RemoveDownloadFolder();

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
    /// Çalışan dosya kendini silemediği için, bu süreç kapandıktan sonra
    /// klasörleri silen gizli bir PowerShell betiği başlatılır. Yalnızca
    /// Kalender'in kendi klasörlerine dokunur.
    ///
    /// Eskiden bu iş bir .cmd dosyasıyla ve sabit 3 saniyelik beklemeyle
    /// yapılıyordu; iki yerde bozuluyordu:
    /// - .NET 8'de Encoding.Default UTF-8'dir, cmd ise dosyayı konsolun OEM kod
    ///   sayfasıyla okur. Kullanıcı adında "Ç, ğ, ı, Ş" olan hesaplarda yol
    ///   bozuluyor ve hiçbir şey silinmiyordu (2026-09-30'da evde görüldü:
    ///   app\Kalender.exe yerinde kaldı). Betik artık Base64 kodlanmış Unicode
    ///   komut satırıyla veriliyor; dosyaya hiç yazılmıyor.
    /// - Kaldırma kurulu exe'nin kendisinden çalışıyor ve sonuç penceresi
    ///   açık durdukça exe kilitli kalıyor. Betik bu süreç gerçekten kapanana
    ///   kadar bekliyor, sonra 30 saniyeye kadar yeniden deniyor.
    ///
    /// Silme yine de yarım kalırsa (örneğin kullanıcı oturumu kapatırsa) iş
    /// kaybolmasın diye hedefler önce HKCU RunOnce altına yazılır; bir sonraki
    /// oturum açılışında Windows kalan klasörleri siler. Başarıyla silinen
    /// hedefin kaydı hemen kaldırılır, yeni bir kurulum da hepsini iptal eder.
    /// </summary>
    private static void ScheduleFileRemoval(bool removeData)
    {
        try
        {
            var targets = new List<string> { removeData ? Paths.RootFolder : AppFolder };
            targets.AddRange(SingleFileExtractionFolders());

            // Güvenlik: yalnızca kendi klasörümüz ve Temp\.net altındaki açılım klasörleri silinir.
            string extractionBase = Path.Combine(Path.GetTempPath(), ".net");
            targets.RemoveAll(t =>
                !t.StartsWith(Paths.RootFolder, StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith(extractionBase, StringComparison.OrdinalIgnoreCase));

            if (targets.Count == 0)
            {
                return;
            }

            string script = BuildRemovalScript(targets, Environment.ProcessId);
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

            var info = new ProcessStartInfo("powershell.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,

                // Çalışma klasörü silinecek klasörün içinde kalırsa Windows onu silemez.
                WorkingDirectory = Path.GetTempPath(),
            };

            foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encoded })
            {
                info.ArgumentList.Add(arg);
            }

            Process.Start(info);
        }
        catch (Exception ex)
        {
            Log.Warn("Dosya silme işi başlatılamadı.", ex);
        }
    }

    /// <summary>
    /// Tek dosyalık exe, içindeki yerel kütüphaneleri ilk açılışta
    /// %TEMP%\.net\&lt;exe adı&gt; altına açar (.NET'in kendi davranışı). Kurulu
    /// kopyanın (Kalender) ve kurucunun (Kalender-Kurulum*) klasörleri
    /// kaldırmadan sonra onlarca megabaytlık kalıntıdır. Çalışan sürecin klasörü
    /// kilitli olduğu için silme, sürecin kapanmasını bekleyen betiğe bırakılır.
    /// Kalender Lite'ın klasörleri (KalenderLite, Kalender-Lite-Kurulum*) bu
    /// adlara uymaz, dokunulmaz.
    /// </summary>
    private static List<string> SingleFileExtractionFolders()
    {
        var found = new List<string>();
        try
        {
            string baseFolder = Path.Combine(Path.GetTempPath(), ".net");
            if (!Directory.Exists(baseFolder))
            {
                return found;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Kalender" };
            string current = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
            if (current.StartsWith("Kalender", StringComparison.OrdinalIgnoreCase)
                && !current.StartsWith("Kalender-Lite", StringComparison.OrdinalIgnoreCase)
                && !current.StartsWith("KalenderLite", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(current);
            }

            foreach (string folder in Directory.EnumerateDirectories(baseFolder))
            {
                string name = Path.GetFileName(folder);
                if (names.Contains(name) || name.StartsWith("Kalender-Kurulum", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(folder);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Temp altındaki açılım klasörleri listelenemedi.", ex);
        }

        return found;
    }

    /// <summary>
    /// Bekle-sil-yeniden dene betiğinin metni. Ayrı bir yöntem, çünkü betik
    /// uygulamadan bağımsız olarak PowerShell'de sınanabilsin.
    /// </summary>
    /// <param name="targets">Silinecek klasörler (tam yol).</param>
    /// <param name="processId">Kapanması beklenecek süreç (kaldırma penceresini gösteren).</param>
    internal static string BuildRemovalScript(IReadOnlyList<string> targets, int processId)
    {
        // Tek tırnaklı PowerShell dizesinde tek tırnak ikilenerek kaçar.
        static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        sb.AppendLine("$hedefler = @(" + string.Join(", ", targets.Select(Quote)) + ")");
        sb.AppendLine("$anahtar = 'HKCU:\\" + RunOnceKey + "'");
        sb.AppendLine("$onek = '" + CleanupValuePrefix + "'");
        sb.AppendLine("# Guvence: silme yarim kalirsa bir sonraki oturum acilisinda Windows tamamlar.");
        sb.AppendLine("if (-not (Test-Path -LiteralPath $anahtar)) { New-Item -Path $anahtar -Force | Out-Null }");
        sb.AppendLine("for ($n = 0; $n -lt $hedefler.Count; $n++) {");
        sb.AppendLine("  Set-ItemProperty -LiteralPath $anahtar -Name ($onek + ($n + 1)) -Value ('cmd.exe /c rd /s /q \"' + $hedefler[$n] + '\"')");
        sb.AppendLine("}");
        sb.AppendLine("# Kaldirma penceresi kapanip exe serbest kalsin.");
        sb.AppendLine("Wait-Process -Id " + processId);
        sb.AppendLine("Start-Sleep -Milliseconds 500");
        sb.AppendLine("for ($n = 0; $n -lt $hedefler.Count; $n++) {");
        sb.AppendLine("  $h = $hedefler[$n]");
        sb.AppendLine("  for ($i = 0; $i -lt 60 -and (Test-Path -LiteralPath $h); $i++) {");
        sb.AppendLine("    Remove-Item -LiteralPath $h -Recurse -Force");
        sb.AppendLine("    if (Test-Path -LiteralPath $h) { Start-Sleep -Milliseconds 500 }");
        sb.AppendLine("  }");
        sb.AppendLine("  if (-not (Test-Path -LiteralPath $h)) { Remove-ItemProperty -LiteralPath $anahtar -Name ($onek + ($n + 1)) }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    /// <summary>
    /// Önceki bir kaldırmanın RunOnce altına bıraktığı, oturum açılışında klasör
    /// silecek kayıtları kaldırır. Kurulumun başında çağrılır; yoksa ertelenmiş
    /// silme yeni kurulumu bir sonraki açılışta götürürdü.
    /// </summary>
    private static void RemovePendingCleanup()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunOnceKey, writable: true);
            if (key is null)
            {
                return;
            }

            foreach (string name in key.GetValueNames())
            {
                if (name.StartsWith(CleanupValuePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                    Log.Info("Bekleyen temizlik kaydı iptal edildi: " + name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Bekleyen temizlik kaydı silinemedi.", ex);
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
