using System.Windows;
using System.Windows.Threading;
using Kalender.Core;

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
