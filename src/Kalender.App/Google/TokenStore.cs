using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kalender.Core;

namespace Kalender.Google;

/// <summary>Diskte saklanan yetki bilgisi.</summary>
internal sealed class StoredToken
{
    public string? RefreshToken { get; set; }

    /// <summary>Hangi istemci kimliğiyle alındı; kimlik değişirse token geçersizdir.</summary>
    public string? ClientId { get; set; }

    public string? AccountEmail { get; set; }

    public DateTimeOffset SavedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Google yenileme anahtarını saklar.
///
/// Dosya, Windows'un DPAPI'siyle yalnızca bu kullanıcı hesabının çözebileceği
/// şekilde şifrelenir. Başka bir kullanıcı ya da başka bir bilgisayar dosyayı
/// kopyalasa bile okuyamaz.
/// </summary>
internal sealed class TokenStore
{
    /// <summary>Şifrelemeye karıştırılan sabit; dosyanın amacını da işaretler.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kalender.Google.Token.v1");

    private static string FilePath => Path.Combine(Paths.RootFolder, "google-token.dat");

    public StoredToken? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            byte[] encrypted = File.ReadAllBytes(FilePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);

            return JsonSerializer.Deserialize<StoredToken>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception ex)
        {
            Log.Warn("Google yetki dosyası okunamadı; yeniden bağlanmak gerekebilir.", ex);
            return null;
        }
    }

    public void Save(StoredToken token)
    {
        try
        {
            Directory.CreateDirectory(Paths.RootFolder);

            byte[] plain = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(FilePath, encrypted);
        }
        catch (Exception ex)
        {
            Log.Error("Google yetki dosyası yazılamadı.", ex);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Google yetki dosyası silinemedi.", ex);
        }
    }
}
