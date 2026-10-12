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

    /// <summary>Kaldırmadan sonra klasör silmenin kaç kez (saniyede bir) deneneceği.</summary>
    private const int RemovalTries = 30;

    /// <summary>
    /// Silme komutunun en uzun hâli. cmd'nin sınırı 8191 karakter; altında
    /// kalınsın diye uzun hedef listesi birden çok komuta bölünür.
    /// </summary>
    internal const int MaxRemovalCommandLength = 7000;

    private static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

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

    /// <summary>Kurulu exe'nin sürümü (üç parçalı); kurulu değilse ya da okunamazsa null.</summary>
    public static Version? InstalledVersion => ReadVersion(InstalledExe);

    /// <summary>
    /// Kurulu sürüm bu dosyanınkinden yeniyse onun sürümü, değilse null.
    /// Eski bir kurucu yeni sürümün üzerine kurulmaz: uygulama eskiye döner,
    /// eski sürüm de yeni sürümün ayarlarını tanımaz (ör. 1.11'den önceki
    /// sürümler şifreli Google istemci parolasını okuyamaz, eşitleme durur).
    /// </summary>
    public static Version? NewerInstalledVersion =>
        InstalledVersion is Version installed && installed > UpdateService.CurrentVersion ? installed : null;

    /// <summary>
    /// Dosyanın sürümü (üç parçalı). Dosya yoksa, sürüm bilgisi taşımıyorsa ya
    /// da okunamazsa null.
    /// </summary>
    internal static Version? ReadVersion(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
            return version > new Version(0, 0, 0) ? version : null;
        }
        catch (Exception ex)
        {
            Log.Warn("Dosyanın sürümü okunamadı: " + path, ex);
            return null;
        }
    }

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

            // Eski bir kurucu daha yeni kurulu sürümün üzerine yazmasın. Kurulum
            // penceresi ve güncelleme yolu buna önceden bakıyor; burası son güvence.
            if (NewerInstalledVersion is Version newer)
            {
                Log.Warn($"Kurulu sürüm ({VersionText(newer)}) bu dosyanınkinden ({Version}) yeni; kurulum yapılmadı.");
                return new SetupResult(false,
                    $"Bu bilgisayarda daha yeni bir sürüm ({VersionText(newer)}) kurulu; eski sürüm ({Version}) kurulmadı.");
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
            link.Description = "Masaüstü duvar kâğıdına işlenmiş aylık takvim";
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
    /// siler, son olarak bu süreç kapanınca dosyaları silecek komutu hazırlar
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
    /// Çalışan dosya kendini silemediği için klasörler bu süreç kapanınca
    /// silinir: kapanışta Windows'un kendi komut satırı (cmd) pencere açmadan
    /// başlatılır, klasörleri saniyede bir, 30 saniyeye kadar silmeyi dener.
    /// Yalnızca Kalender'in kendi klasörlerine dokunur.
    ///
    /// Neden PowerShell değil: 1.10'a kadar bu iş gizli pencereli,
    /// "-EncodedCommand" ile verilmiş bir PowerShell betiğiyle yapılıyordu.
    /// Virüs korumaları ve kurumsal güvenlik yazılımları bu kalıbı kötü amaçlı
    /// yazılım davranışı sayıyor; kaldırma engellenebilir ya da uyarı çıkarırdı.
    ///
    /// Daha önceki .cmd dosyasındaki Türkçe harf sorunu (dosya UTF-8 yazılıyor,
    /// cmd OEM kod sayfasıyla okuyordu; 2026-09-30'da evde app\Kalender.exe
    /// yerinde kalmıştı) burada yok: komut dosyaya yazılmıyor, Unicode komut
    /// satırıyla veriliyor. Kaldırma penceresi açık durdukça exe kilitli kaldığı
    /// için komut ancak süreç kapanırken başlatılıyor.
    ///
    /// Silme yarım kalırsa (süreç zorla kapatılırsa, oturum kapanırsa) iş
    /// kaybolmasın diye hedefler önce HKCU RunOnce altına yazılır; bir sonraki
    /// oturum açılışında Windows kalan klasörleri siler. Silme bitince bu
    /// kayıtlar kaldırılır, yeni bir kurulum da hepsini iptal eder.
    /// </summary>
    private static void ScheduleFileRemoval(bool removeData)
    {
        try
        {
            var targets = new List<string> { removeData ? Paths.RootFolder : AppFolder };

            // Kullanılmayan açılım klasörleri (eski kurucular, güncelleme indirmeleri)
            // hemen silinir; komuta yalnızca silinemeyenler bırakılır. Bu sürecin
            // kendi klasörüne şimdi dokunulmaz: açık pencere oradan sonradan
            // kütüphane yükleyebilir, yarım silmek onu bozardı.
            string own = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
            foreach (string folder in SingleFileExtractionFolders())
            {
                bool inUse = string.Equals(Path.GetFileName(folder), own, StringComparison.OrdinalIgnoreCase);
                if (inUse || !TryDeleteNow(folder))
                {
                    targets.Add(folder);
                }
            }

            // Güvenlik: yalnızca kendi klasörümüz ve Temp\.net altındaki açılım klasörleri silinir.
            string extractionBase = Path.Combine(Path.GetTempPath(), ".net");
            targets.RemoveAll(t =>
                !t.StartsWith(Paths.RootFolder, StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith(extractionBase, StringComparison.OrdinalIgnoreCase));

            // cmd, komut satırındaki %…% dizilerini açar; yüzde işaretli bir yol
            // başka bir yolu gösterebilirdi. Böyle bir klasör silinmeden kalır.
            foreach (string skipped in targets.Where(t => t.Contains('%')))
            {
                Log.Warn("Yolunda % işareti olduğu için silinmeyecek: " + skipped);
            }

            targets.RemoveAll(t => t.Contains('%'));

            if (targets.Count == 0)
            {
                return;
            }

            // Önceki bir kaldırmadan kalan yedek kayıtların numaraları karışmasın.
            RemovePendingCleanup();
            WriteCleanupFallback(targets);

            IReadOnlyList<string> commands = BuildRemovalCommands(targets, RunOnceKey, CleanupValuePrefix, RemovalTries);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (string command in commands)
                {
                    StartRemoval(command);
                }
            };
        }
        catch (Exception ex)
        {
            Log.Warn("Dosya silme işi hazırlanamadı.", ex);
        }
    }

    /// <summary>Klasörü hemen silmeyi dener; kilitliyse (ya da başka hata) false.</summary>
    private static bool TryDeleteNow(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Silme yarım kalırsa bir sonraki oturum açılışında Windows'un silmesi
    /// için her hedefi RunOnce altına ayrı bir kayıt olarak yazar.
    /// </summary>
    private static void WriteCleanupFallback(IReadOnlyList<string> targets)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunOnceKey, writable: true);
            for (int n = 0; n < targets.Count; n++)
            {
                key.SetValue(CleanupValuePrefix + (n + 1), FallbackCommand(targets[n]), RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Yedek silme kaydı yazılamadı.", ex);
        }
    }

    /// <summary>RunOnce kaydına yazılan, tek klasörü silen komut.</summary>
    internal static string FallbackCommand(string target)
        => "\"" + CmdPath + "\" /d /c rd /s /q \"" + target + "\"";

    /// <summary>Silme komutunu pencere açmadan başlatır. Bu süreç kapanırken çağrılır.</summary>
    private static void StartRemoval(string arguments)
    {
        try
        {
            var info = new ProcessStartInfo(CmdPath, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,

                // Çalışma klasörü silinecek klasörün içinde kalırsa Windows onu silemez.
                WorkingDirectory = Path.GetTempPath(),
            };

            using Process? process = Process.Start(info);
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
    /// kilitli olduğu için onun silinmesi, kapanışta başlayan komuta bırakılır.
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
    /// Kapanışta başlatılacak silme komutlarının cmd argümanları. Her komut
    /// kendi klasörlerini saniyede bir silmeyi dener; hepsi gidince RunOnce
    /// altındaki yedek kayıtlarını kaldırıp çıkar, olmazsa
    /// <paramref name="tries"/> denemeden sonra kayıtları bırakıp çıkar.
    /// Uzun liste, cmd'nin komut satırı sınırına takılmasın diye bölünür.
    /// Ayrı ve dosyaya dokunmayan bir yöntem: sınamada doğrudan çalıştırılabilsin.
    /// </summary>
    /// <param name="targets">Silinecek klasörler (tam yol, % işaretsiz). n. hedefin yedek kaydı <paramref name="valuePrefix"/> + (n + 1).</param>
    /// <param name="runOnceKey">Yedek kayıtların HKCU altındaki anahtarı.</param>
    /// <param name="valuePrefix">Yedek kayıtların ad öneki.</param>
    /// <param name="tries">En çok kaç kez (saniyede bir) denensin.</param>
    internal static IReadOnlyList<string> BuildRemovalCommands(
        IReadOnlyList<string> targets, string runOnceKey, string valuePrefix, int tries)
    {
        var commands = new List<string>();
        var chunk = new List<(string Target, int Number)>();

        for (int n = 0; n < targets.Count; n++)
        {
            chunk.Add((targets[n], n + 1));
            if (chunk.Count > 1 && RemovalCommand(chunk, runOnceKey, valuePrefix, tries).Length > MaxRemovalCommandLength)
            {
                chunk.RemoveAt(chunk.Count - 1);
                commands.Add(RemovalCommand(chunk, runOnceKey, valuePrefix, tries));
                chunk = new List<(string, int)> { (targets[n], n + 1) };
            }
        }

        if (chunk.Count > 0)
        {
            commands.Add(RemovalCommand(chunk, runOnceKey, valuePrefix, tries));
        }

        return commands;
    }

    /// <summary>
    /// Tek silme komutu. /s ile cmd yalnızca en dıştaki tırnak çiftini atar,
    /// içerideki tırnaklı yollar olduğu gibi kalır; tırnak içindeki &amp;, ( ve )
    /// komut sayılmaz. /v:off ünlem işaretli yolları korur.
    /// </summary>
    private static string RemovalCommand(
        IReadOnlyList<(string Target, int Number)> chunk, string runOnceKey, string valuePrefix, int tries)
    {
        static string Q(string s) => "\"" + s + "\"";

        string reg = Q(Path.Combine(Environment.SystemDirectory, "reg.exe"));
        string wait = Q(Path.Combine(Environment.SystemDirectory, "timeout.exe"));
        string key = Q(@"HKCU\" + runOnceKey);

        var body = new StringBuilder();
        foreach ((string target, _) in chunk)
        {
            body.Append("(if exist ").Append(Q(target)).Append(" rd /s /q ").Append(Q(target)).Append(") >nul 2>&1 & ");
        }

        // Hepsi gittiyse yedek kayıtları sil ve çık; yoksa bir saniye bekle.
        body.Append('(');
        foreach ((string target, _) in chunk)
        {
            body.Append("if not exist ").Append(Q(target)).Append(' ');
        }

        body.Append('(');
        foreach ((_, int number) in chunk)
        {
            body.Append(reg).Append(" delete ").Append(key).Append(" /v ").Append(valuePrefix).Append(number)
                .Append(" /f >nul 2>&1 & ");
        }

        body.Append("exit 0)) & ").Append(wait).Append(" /t 1 /nobreak >nul");

        return "/d /q /s /v:off /c \"for /l %i in (1,1," + tries + ") do (" + body + ")\"";
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
