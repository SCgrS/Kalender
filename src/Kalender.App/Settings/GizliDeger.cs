using System.Security.Cryptography;
using System.Text;
using Kalender.Core;

namespace Kalender.Settings;

/// <summary>
/// Ayar dosyasında düz metin durmaması gereken değerleri (Google istemci
/// kimliği ve gizli anahtarı, hesabın e-posta adresi, seçili takvimler)
/// Windows'un DPAPI'siyle şifreler ve çözer.
///
/// Google yetki dosyası gibi, yalnızca bu kullanıcı hesabının bu bilgisayarda
/// çözebileceği biçimde şifrelenir; ayar dosyası başka bir hesaba ya da
/// bilgisayara kopyalansa bile değer okunamaz.
/// </summary>
internal static class GizliDeger
{
    /// <summary>
    /// Şifrelemeye karıştırılan sabit. Adı ilk şifrelenen değerden (1.11'de gizli
    /// anahtar) kalıyor; değiştirilirse 1.11–1.16'nın yazdığı gizli anahtar çözülemez.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kalender.Google.ClientSecret.v1");

    /// <summary>Değeri şifreleyip Base64 olarak verir. Boş değer ya da hata için null.</summary>
    /// <param name="ad">Günlükte görünecek adı (ör. "Google istemci kimliği").</param>
    public static string? Sifrele(string? duz, string ad = "Google istemci gizli anahtarı")
    {
        if (string.IsNullOrEmpty(duz))
        {
            return null;
        }

        try
        {
            byte[] sifreli = ProtectedData.Protect(Encoding.UTF8.GetBytes(duz), Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(sifreli);
        }
        catch (Exception ex)
        {
            Log.Error($"{ad} şifrelenemedi; ayar dosyasına yazılmayacak.", ex);
            return null;
        }
    }

    /// <summary>
    /// Şifreli değeri çözer. Boşsa ya da çözülemiyorsa (ayar dosyası başka bir
    /// hesaptan kopyalanmış) null; kullanıcı ayarlardan yeniden girer.
    /// </summary>
    /// <param name="ad">Günlükte görünecek adı (ör. "Google istemci kimliği").</param>
    public static string? Coz(string? sifreli, string ad = "Google istemci gizli anahtarı")
    {
        if (string.IsNullOrEmpty(sifreli))
        {
            return null;
        }

        try
        {
            byte[] duz = ProtectedData.Unprotect(Convert.FromBase64String(sifreli), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(duz);
        }
        catch (Exception ex)
        {
            Log.Warn($"{ad} çözülemedi; ayarlardan yeniden girilmesi gerekebilir.", ex);
            return null;
        }
    }
}
