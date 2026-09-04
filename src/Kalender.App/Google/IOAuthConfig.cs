using Kalender.Settings;

namespace Kalender.Google;

/// <summary>
/// OAuth istemci yapılandırması.
///
/// Şu an kullanıcı kendi Google Cloud istemci kimliğini ayarlar ekranına
/// yapıştırıyor. İleride tek merkezi ve doğrulanmış bir kimliğe geçilirse
/// yalnızca bu arayüzün başka bir uygulaması yazılacak; akışın geri kalanı
/// değişmeyecek.
/// </summary>
internal interface IOAuthConfig
{
    /// <summary>Google Cloud'daki OAuth istemci kimliği.</summary>
    string? ClientId { get; }

    /// <summary>
    /// İstemci parolası. Masaüstü istemcilerinde Google bunu isteyebiliyor.
    /// Uygulamanın içine gömülmez; kullanıcı kendi projesindekini yapıştırır
    /// ve şifreli olarak kendi bilgisayarında durur.
    /// </summary>
    string? ClientSecret { get; }

    /// <summary>Yapılandırma bağlantı kurmaya yeterli mi?</summary>
    bool IsReady { get; }
}

/// <summary>Ayarlar dosyasından okuyan uygulama.</summary>
internal sealed class SettingsOAuthConfig : IOAuthConfig
{
    private readonly SettingsStore _settings;

    public SettingsOAuthConfig(SettingsStore settings)
    {
        _settings = settings;
    }

    public string? ClientId => Clean(_settings.Current.Google.ClientId);

    public string? ClientSecret => Clean(_settings.Current.Google.ClientSecret);

    public bool IsReady => !string.IsNullOrWhiteSpace(ClientId);

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
