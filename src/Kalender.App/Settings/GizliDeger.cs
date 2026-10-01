using System.Security.Cryptography;
using System.Text;
using Kalender.Core;

namespace Kalender.Settings;

/// <summary>
/// Ayar dosyasında düz metin durmaması gereken değerleri (Google istemci
/// gizli anahtarı) Windows'un DPAPI'siyle şifreler ve çözer.
///
/// Google yetki dosyası gibi, yalnızca bu kullanıcı hesabının bu bilgisayarda
/// çözebileceği biçimde şifrelenir; ayar dosyası başka bir hesaba ya da
/// bilgisayara kopyalansa bile değer okunamaz.
/// </summary>
internal static class GizliDeger
{
    /// <summary>Şifrelemeye karıştırılan sabit; değerin amacını da işaretler.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kalender.Google.ClientSecret.v1");

    /// <summary>Değeri şifreleyip Base64 olarak verir. Boş değer ya da hata için null.</summary>
    public static string? Sifrele(string? duz)
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
            Log.Error("Google istemci gizli anahtarı şifrelenemedi; ayar dosyasına yazılmayacak.", ex);
            return null;
        }
    }

    /// <summary>
    /// Şifreli değeri çözer. Boşsa ya da çözülemiyorsa (ayar dosyası başka bir
    /// hesaptan kopyalanmış) null; kullanıcı ayarlardan yeniden girer.
    /// </summary>
    public static string? Coz(string? sifreli)
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
            Log.Warn("Google istemci gizli anahtarı çözülemedi; ayarlardan yeniden girilmesi gerekebilir.", ex);
            return null;
        }
    }
}
