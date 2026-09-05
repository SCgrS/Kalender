using Microsoft.Win32;

namespace Kalender.Core;

/// <summary>
/// Windows oturum açılışında Kalender'in kendiliğinden başlamasını yönetir.
///
/// Kayıt yalnızca kullanıcının kendi anahtarına (HKCU) yazılır; yönetici hakkı
/// istenmez. Zamanlanmış görev ya da hizmet kullanılmıyor, çünkü ikisi de
/// yükseltilmiş yetki isterdi.
/// </summary>
internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kalender";

    /// <summary>Şu an açılışta başlatılacak şekilde kayıtlı mı?</summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            Log.Warn("Açılış kaydı okunamadı.", ex);
            return false;
        }
    }

    /// <summary>Kayıtlı yol, bu çalıştırılabilir dosyayı gösteriyor mu?</summary>
    public static bool PointsToCurrentExe()
    {
        try
        {
            string? current = Environment.ProcessPath;
            if (string.IsNullOrEmpty(current))
            {
                return false;
            }

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is not string value)
            {
                return false;
            }

            return string.Equals(value.Trim('"'), current, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Warn("Açılış kaydı karşılaştırılamadı.", ex);
            return false;
        }
    }

    /// <summary>Ayara göre kaydı yazar ya da siler.</summary>
    public static void Apply(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

            if (!enabled)
            {
                if (key.GetValue(ValueName) is not null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    Log.Info("Açılışta başlatma kapatıldı.");
                }

                return;
            }

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe))
            {
                Log.Warn("Çalıştırılabilir dosyanın yolu okunamadı; açılış kaydı yazılamadı.");
                return;
            }

            string wanted = "\"" + exe + "\"";
            if (key.GetValue(ValueName) as string == wanted)
            {
                return;
            }

            key.SetValue(ValueName, wanted, RegistryValueKind.String);
            Log.Info("Açılışta başlatma açıldı: " + exe);
        }
        catch (Exception ex)
        {
            // Kayıt yazılamazsa uygulama yine çalışır; yalnızca açılışta gelmez.
            Log.Warn("Açılış kaydı güncellenemedi.", ex);
        }
    }
}
