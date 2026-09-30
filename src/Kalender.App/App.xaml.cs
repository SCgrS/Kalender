using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Kalender.Core;
using Kalender.Ui;

namespace Kalender;

/// <summary>
/// Uygulama giriş noktası. Görünür bir ana pencere yoktur; Kalender tepsiden
/// yönetilir ve çizimini masaüstü katmanına yapar.
/// </summary>
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Paths.EnsureFolders();

        // Kaldırma aracının ve sorun gidermenin kullandığı kısa yol:
        // uygulamayı açmadan yalnızca duvar kağıdını eski hâline döndürür.
        if (e.Args.Any(a => string.Equals(a, "--duvar-kagidini-geri-yukle", StringComparison.OrdinalIgnoreCase)))
        {
            // Sessiz çalışır: kaldırma aracı kendi penceresini gösterecek.
            Desktop.WallpaperService.Restore();
            Shutdown();
            return;
        }

        // Uygulamanın içinden başlatılan güncelleme. Dosyanın adında "Kurulum"
        // geçtiği için kurulum denetiminden önce bakılmalı.
        if (TryRunUpdate(e.Args))
        {
            return;
        }

        // Kurulum ve kaldırma: yayınlanan tek dosya hem uygulama hem kurucudur.
        if (TryRunSetup(e.Args))
        {
            return;
        }

        // Geliştirme aracı: istenen ayı bir PNG'ye çizip çıkar.
        if (e.Args.Any(a => string.Equals(a, PreviewCommand.Switch, StringComparison.OrdinalIgnoreCase)))
        {
            Log.Level = LogLevel.Info;
            _ = PreviewCommand.TryRunAsync(e.Args).ContinueWith(_ =>
                Dispatcher.BeginInvoke(Shutdown), TaskScheduler.Default);
            return;
        }

        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            // Zaten çalışan bir Kalender var. İkinci kez çalıştırmak bir işe
            // yaramadan kapanmak yerine, çalışan örneğin ayarlarını açsın.
            IntPtr existing = Interop.NativeMethods.FindWindow(
                Interop.AppMessageWindow.WindowClassName, null);

            if (existing != IntPtr.Zero)
            {
                Interop.NativeMethods.PostMessage(
                    existing, Interop.AppMessageWindow.OpenSettingsMessage, IntPtr.Zero, IntPtr.Zero);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _host = new AppHost();
            _host.ExitRequested += Shutdown;
            _host.Start();

            if (e.Args.Any(a => string.Equals(a, UpdateService.UpdatedSwitch, StringComparison.OrdinalIgnoreCase)))
            {
                _host.ShowUpdated(
                    quiet: e.Args.Any(a => string.Equals(a, UpdateService.NoticeSwitch, StringComparison.OrdinalIgnoreCase)));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Kalender başlatılamadı.", ex);
            MessageBox.Show(
                "Kalender başlatılamadı. Ayrıntılar günlük dosyasına yazıldı:\n" + Paths.LogFolder,
                "Kalender",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>
    /// Uygulamanın içinden başlatılan güncelleme (bkz. <see cref="UpdateService"/>).
    /// Eski sürüm kapanana kadar bekler, kendini kurar ve yeni sürümü açar.
    /// Pencere göstermez; bir sorun olursa haber verir ve eski sürümü geri
    /// açar ki kullanıcı takvimsiz kalmasın.
    /// </summary>
    private bool TryRunUpdate(string[] args)
    {
        if (!args.Any(a => string.Equals(a, UpdateService.InstallSwitch, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        Log.Level = LogLevel.Info;
        WaitForCaller(args);

        SetupResult result = Core.Installer.Install();
        if (result.Success)
        {
            // Hatırlatma penceresinden gelindiyse yeni sürüm ayarları açmasın, bildirimle yetinsin.
            bool quiet = args.Any(a => string.Equals(a, UpdateService.NoticeSwitch, StringComparison.OrdinalIgnoreCase));
            Core.Installer.LaunchInstalled(quiet
                ? new[] { UpdateService.UpdatedSwitch, UpdateService.NoticeSwitch }
                : new[] { UpdateService.UpdatedSwitch });
        }
        else
        {
            MessageBox.Show(
                "Kalender güncellenemedi.\n\n" + result.Message,
                "Kalender",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            if (Core.Installer.IsInstalled)
            {
                Core.Installer.LaunchInstalled();
            }
        }

        Shutdown();
        return true;
    }

    /// <summary>
    /// Güncellemeyi başlatan eski sürümün kapanmasını bekler; dosyası
    /// kullanımdayken üzerine yazılamaz. Beklemek yetmezse kurulum yine de
    /// çalışan sürümü kibarca kapatmayı dener.
    /// </summary>
    private static void WaitForCaller(string[] args)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, UpdateService.WaitSwitch, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length
            || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int pid))
        {
            return;
        }

        try
        {
            using Process caller = Process.GetProcessById(pid);
            if (!caller.WaitForExit(20_000))
            {
                Log.Warn("Eski sürüm 20 saniyede kapanmadı; kurulum yine de deneniyor.");
            }
        }
        catch (ArgumentException)
        {
            // Süreç çoktan kapanmış.
        }
    }

    /// <summary>
    /// Uygulama kurulum ya da kaldırma kipinde mi açıldı?
    ///
    /// İki yol var: açık bir komut satırı anahtarı, ya da dosya adının
    /// "Kurulum" içermesi. İkincisi sayesinde kullanıcı indirdiği
    /// Kalender-Kurulum.exe dosyasına çift tıklayınca kurulum ekranı açılıyor;
    /// ayrı bir kurulum programı indirmesi gerekmiyor.
    /// </summary>
    private bool TryRunSetup(string[] args)
    {
        bool install = args.Any(a => string.Equals(a, "--kur", StringComparison.OrdinalIgnoreCase));
        bool uninstall = args.Any(a => string.Equals(a, "--kaldir", StringComparison.OrdinalIgnoreCase));

        if (!install && !uninstall)
        {
            string name = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);

            install = name.Contains("Kurulum", StringComparison.OrdinalIgnoreCase)
                && !Core.Installer.RunningInstalled;

            uninstall = name.Contains("Kaldir", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Kaldır", StringComparison.OrdinalIgnoreCase);
        }

        if (!install && !uninstall)
        {
            return false;
        }

        Log.Level = LogLevel.Info;

        var window = new SetupWindow(install ? SetupMode.Install : SetupMode.Uninstall);
        window.Closed += (_, _) => Shutdown();
        window.Show();

        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _instance?.Dispose();
        Log.Info("Kalender kapandı.");
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Arayüz iş parçacığındaki bir hata yüzünden uygulama kapanmasın:
        // logla ve devam et. Kararlılık her şeyin önünde.
        Log.Error("Arayüz iş parçacığında yakalanmamış hata.", e.Exception);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Yakalanmamış hata.", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("Gözlenmemiş görev hatası.", e.Exception);
        e.SetObserved();
    }
}
