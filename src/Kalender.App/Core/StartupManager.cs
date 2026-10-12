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
    private const string ValueName = "Kalender";

    /// <summary>Açılış kaydının anahtarı. Sınamada geçici bir anahtara çevrilebilsin diye sabit değil.</summary>
    internal static string RunKey { get; set; } = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Windows'un "Başlangıç uygulamaları" listesi (Görev Yöneticisi, Ayarlar)
    /// açık/kapalı durumunu buraya yazar. Run kaydı silinip yeniden yazılsa da
    /// bu değer yerinde kalır: Windows'ta kapatılmış Kalender, Run kaydı
    /// dursa bile açılışta başlamaz. Sınamada geçici bir anahtara çevrilebilsin
    /// diye sabit değil.
    /// </summary>
    internal static string StartupApprovedKey { get; set; } =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

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

    /// <summary>
    /// Ayara göre kaydı yazar ya da siler. Açılışta da çağrılır; kaydı yazarken
    /// Windows'un başlangıç listesindeki işarete dokunulmaz ki orada verilen
    /// karar her açılışta ezilmesin (bkz. <see cref="ApplyByUser"/>). Kayıt
    /// silinirken işaret de silinir: Windows listesinde kaldırılmış programın
    /// artığı kalmasın.
    /// </summary>
    public static void Apply(bool enabled) => Apply(enabled, Environment.ProcessPath);

    /// <summary>
    /// Kullanıcı ayarlardaki "Windows ile başlat" anahtarına dokundu. Açtıysa
    /// Windows'un başlangıç listesindeki "kapalı" işareti de kaldırılır; yoksa
    /// anahtar açık görünür ama Kalender açılışta başlamazdı.
    /// </summary>
    public static void ApplyByUser(bool enabled)
    {
        Apply(enabled);
        if (enabled)
        {
            RemoveStartupApproval();
        }
    }

    /// <summary>Belirli bir dosya için kaydı yazar ya da siler (kurulum sırasında gerekir).</summary>
    public static void Apply(bool enabled, string? exePath)
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

                RemoveStartupApproval();
                return;
            }

            string? exe = exePath;
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

    /// <summary>Başlangıç uygulamaları listesindeki onay kaydını siler; yoksa bir şey yapmaz.</summary>
    private static void RemoveStartupApproval()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Başlangıç uygulamaları onay kaydı silindi.");
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Başlangıç uygulamaları onay kaydı silinemedi.", ex);
        }
    }
}
