using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kalender.Core;

namespace Kalender.Google;

/// <summary>Bağlanma denemesinin sonucu.</summary>
/// <param name="Success">Bağlantı kuruldu mu?</param>
/// <param name="Message">Kullanıcıya gösterilecek Türkçe açıklama.</param>
internal sealed record SignInResult(bool Success, string Message);

/// <summary>
/// Google OAuth 2.0 "yüklü uygulama" akışı.
///
/// PKCE kullanılıyor ve yanıt, yalnızca bu bilgisayarda dinlenen bir loopback
/// adresine (127.0.0.1, rastgele port) dönüyor. Uygulamanın içine hiçbir gizli
/// anahtar gömülmedi; kimlik bilgisi kullanıcının kendi Google Cloud projesinden
/// geliyor ve şifreli olarak kendi bilgisayarında duruyor.
///
/// Dinleme için HttpListener yerine ham TcpListener kullanılıyor: HttpListener
/// Windows'ta URL ayrımı (URL ACL) istiyor ve bu yönetici hakkı gerektirebiliyor.
/// Şartname yönetici hakkı istenmemesini şart koşuyor.
/// </summary>
internal sealed class GoogleAuthenticator : IDisposable
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private static readonly string[] Scopes =
    {
        "https://www.googleapis.com/auth/calendar.readonly",
        "https://www.googleapis.com/auth/tasks.readonly",
    };

    /// <summary>Tarayıcıda onay için tanınan süre.</summary>
    private static readonly TimeSpan ConsentTimeout = TimeSpan.FromMinutes(4);

    private readonly IOAuthConfig _config;
    private readonly TokenStore _store = new();
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StoredToken? _token;
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiry = DateTimeOffset.MinValue;

    public GoogleAuthenticator(IOAuthConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),

            // Takvim sağlayıcısıyla aynı: HTTP/2 istenir, olmazsa 1.1'e düşülür.
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
        _token = _store.Load();
    }

    /// <summary>Kayıtlı bir yetki var mı?</summary>
    public bool IsConnected => !string.IsNullOrEmpty(_token?.RefreshToken);

    public string? AccountEmail => _token?.AccountEmail;

    /// <summary>Bağlantı durumu değişti (bağlandı ya da koptu).</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Google kayıtlı izni yenilerken geçersiz saydı ve bağlantı kendiliğinden
    /// kesildi. Önceden bu sessizce oluyordu: etkinlikler takvimden kayboluyor,
    /// kullanıcı da bunu bağlantı sorunu sanıyordu.
    /// </summary>
    public event Action? YetkiDustu;

    // ------------------------------------------------------------- bağlanma

    public async Task<SignInResult> SignInAsync(CancellationToken cancel = default)
    {
        if (!_config.IsReady)
        {
            return new SignInResult(false, "Önce Google istemci kimliğini (Client ID) yapıştırın.");
        }

        string clientId = _config.ClientId!;

        try
        {
            string verifier = CreateRandomString(64);
            string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            string state = CreateRandomString(24);

            using var listener = new LoopbackListener();
            string redirectUri = listener.RedirectUri;

            string url = $"{AuthEndpoint}"
                + $"?client_id={Uri.EscapeDataString(clientId)}"
                + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
                + "&response_type=code"
                + $"&scope={Uri.EscapeDataString(string.Join(' ', Scopes))}"
                + $"&code_challenge={challenge}"
                + "&code_challenge_method=S256"
                + $"&state={state}"
                + "&access_type=offline"
                + "&prompt=consent";

            OpenBrowser(url);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(ConsentTimeout);

            CallbackResult callback = await listener.WaitAsync(timeout.Token).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(callback.Error))
            {
                return new SignInResult(false, callback.Error == "access_denied"
                    ? "Google onayı verilmedi."
                    : $"Google onay hatası: {callback.Error}");
            }

            if (callback.State != state)
            {
                return new SignInResult(false, "Yanıt doğrulanamadı; lütfen yeniden deneyin.");
            }

            if (string.IsNullOrEmpty(callback.Code))
            {
                return new SignInResult(false, "Google'dan yetki kodu gelmedi.");
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["code"] = callback.Code,
                ["code_verifier"] = verifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
            };

            if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                form["client_secret"] = _config.ClientSecret!;
            }

            TokenResponse? token = await PostTokenAsync(form, cancel).ConfigureAwait(false);

            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                return new SignInResult(false, "Google yetki alışverişi başarısız oldu.");
            }

            if (string.IsNullOrEmpty(token.RefreshToken))
            {
                return new SignInResult(false,
                    "Google kalıcı izin vermedi. Google Cloud'daki istemcinin türü "
                    + "\"Masaüstü uygulaması\" olmalı; öyleyse bağlantıyı kesip yeniden deneyin.");
            }

            _token = new StoredToken
            {
                RefreshToken = token.RefreshToken,
                ClientId = clientId,
                AccountEmail = _token?.AccountEmail,
            };

            _accessToken = token.AccessToken;
            _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn) - 60);
            _store.Save(_token);

            Log.Info("Google hesabına bağlanıldı.");
            StateChanged?.Invoke();

            return new SignInResult(true, "Google hesabınıza bağlanıldı.");
        }
        catch (OperationCanceledException)
        {
            return new SignInResult(false, cancel.IsCancellationRequested
                ? "Bağlanmaktan vazgeçildi."
                : "Tarayıcıda onay gelmedi; süre doldu. Yeniden deneyebilirsiniz.");
        }
        catch (Exception ex)
        {
            Log.Error("Google bağlantısı kurulamadı.", ex);
            return new SignInResult(false, "Bağlantı kurulamadı: " + ex.Message);
        }
    }

    public void SignOut()
    {
        _token = null;
        _accessToken = null;
        _accessTokenExpiry = DateTimeOffset.MinValue;
        _store.Clear();

        Log.Info("Google bağlantısı kesildi.");
        StateChanged?.Invoke();
    }

    public void UpdateAccountEmail(string? email)
    {
        if (_token is null || string.IsNullOrWhiteSpace(email) || _token.AccountEmail == email)
        {
            return;
        }

        _token.AccountEmail = email;
        _store.Save(_token);
        StateChanged?.Invoke();
    }

    // -------------------------------------------------------------- erişim

    /// <summary>Geçerli bir erişim anahtarı verir; gerekiyorsa yeniler.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancel = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiry)
        {
            return _accessToken;
        }

        await _gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiry)
            {
                return _accessToken;
            }

            if (_token?.RefreshToken is null || !_config.IsReady)
            {
                return null;
            }

            if (_token.ClientId is not null && _token.ClientId != _config.ClientId)
            {
                Log.Warn("İstemci kimliği değişmiş; kayıtlı Google yetkisi geçersiz.");
                SignOut();
                return null;
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"] = _config.ClientId!,
                ["refresh_token"] = _token.RefreshToken,
                ["grant_type"] = "refresh_token",
            };

            if (!string.IsNullOrWhiteSpace(_config.ClientSecret))
            {
                form["client_secret"] = _config.ClientSecret!;
            }

            TokenResponse? token = await PostTokenAsync(form, cancel).ConfigureAwait(false);

            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                return null;
            }

            _accessToken = token.AccessToken;
            _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn) - 60);

            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                _token.RefreshToken = token.RefreshToken;
                _store.Save(_token);
            }

            return _accessToken;
        }
        catch (Exception ex)
        {
            Log.Warn("Google erişim anahtarı yenilenemedi.", ex);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Yetki kalıcı olarak geçersizse (kullanıcı izni geri aldı) bağlantıyı düşürür.</summary>
    /// <param name="yenilenirken">
    /// Kayıtlı izin yenilenirken mi düştü? Öyleyse kullanıcıya haber verilir.
    /// Bağlanma sırasındaki bir hata zaten bağlanma ekranında gösteriliyor.
    /// </param>
    public void InvalidateAuthorization(bool yenilenirken)
    {
        if (yenilenirken)
        {
            // Hata olarak yazılıyor ki varsayılan günlük ayarında da görünsün.
            Log.Error("Google kayıtlı izni geçersiz saydı; bağlantı kesildi. Uygulama Google Cloud'da "
                + "\"Testing\" durumundaysa Google izni 7 günde bir düşürür.");
        }
        else
        {
            Log.Warn("Google yetkisi geçersiz; bağlantı kesiliyor.");
        }

        SignOut();

        if (yenilenirken)
        {
            YetkiDustu?.Invoke();
        }
    }

    private async Task<TokenResponse?> PostTokenAsync(Dictionary<string, string> form, CancellationToken cancel)
    {
        using var content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await _http.PostAsync(TokenEndpoint, content, cancel)
            .ConfigureAwait(false);

        string body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            Log.Warn($"Google token isteği başarısız ({(int)response.StatusCode}): {Kisalt(body)}");

            // invalid_grant: yetki iptal edilmiş ya da süresi dolmuş.
            if (body.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                InvalidateAuthorization(
                    yenilenirken: form.TryGetValue("grant_type", out string? tur) && tur == "refresh_token");
            }

            return null;
        }

        return JsonSerializer.Deserialize<TokenResponse>(body);
    }

    private static string Kisalt(string value) => value.Length <= 300 ? value : value[..300];

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Tarayıcı açılamadı.", ex);
            throw new InvalidOperationException("Tarayıcı açılamadı. Varsayılan tarayıcınızı denetleyin.", ex);
        }
    }

    private static string CreateRandomString(int byteCount)
        => Base64Url(RandomNumberGenerator.GetBytes(byteCount));

    private static string Base64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    // ------------------------------------------------------------ yardımcı

    private sealed class TokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed record CallbackResult(string? Code, string? State, string? Error);

    /// <summary>
    /// Onay sonrası tarayıcının yönlendirildiği tek seferlik yerel dinleyici.
    /// Kendi başına küçük bir HTTP yanıtı yazar; ek bir sunucu bileşeni yok.
    /// </summary>
    private sealed class LoopbackListener : IDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackListener()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();

            int port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            RedirectUri = $"http://127.0.0.1:{port}";
        }

        public string RedirectUri { get; }

        public async Task<CallbackResult> WaitAsync(CancellationToken cancel)
        {
            while (!cancel.IsCancellationRequested)
            {
                using TcpClient client = await _listener.AcceptTcpClientAsync(cancel).ConfigureAwait(false);
                using NetworkStream stream = client.GetStream();

                string requestLine = await ReadRequestLineAsync(stream, cancel).ConfigureAwait(false);
                string path = ExtractPath(requestLine);

                if (!path.Contains("code=", StringComparison.Ordinal)
                    && !path.Contains("error=", StringComparison.Ordinal))
                {
                    // Tarayıcının favicon gibi yan istekleri; kısa yanıt verip bekleyelim.
                    await WriteResponseAsync(stream, "Bekleniyor…", cancel).ConfigureAwait(false);
                    continue;
                }

                int q = path.IndexOf('?');
                Dictionary<string, string> query = ParseQuery(q >= 0 ? path[(q + 1)..] : string.Empty);

                query.TryGetValue("error", out string? error);
                query.TryGetValue("code", out string? code);
                query.TryGetValue("state", out string? callbackState);

                await WriteResponseAsync(stream, BuildPage(error), cancel).ConfigureAwait(false);

                return new CallbackResult(code, callbackState, error);
            }

            cancel.ThrowIfCancellationRequested();
            return new CallbackResult(null, null, null);
        }

        /// <summary>"code=abc&amp;state=xyz" -> sözlük. Küçük bir işi için ek bağımlılık istemedik.</summary>
        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                string key = Uri.UnescapeDataString(pair[..eq]);
                string value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                result[key] = value;
            }

            return result;
        }

        private static string BuildPage(string? error) => error is null
            ? "Kalender bağlandı. Bu sekmeyi kapatabilirsiniz."
            : "Kalender bağlanamadı. Uygulamaya dönüp yeniden deneyin.";

        private static async Task<string> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancel)
        {
            var buffer = new byte[4096];
            int read = await stream.ReadAsync(buffer, cancel).ConfigureAwait(false);
            string text = Encoding.ASCII.GetString(buffer, 0, read);

            int newline = text.IndexOf('\r');
            return newline > 0 ? text[..newline] : text;
        }

        private static string ExtractPath(string requestLine)
        {
            string[] parts = requestLine.Split(' ');
            return parts.Length >= 2 ? parts[1] : string.Empty;
        }

        private static async Task WriteResponseAsync(NetworkStream stream, string message, CancellationToken cancel)
        {
            string html = "<!doctype html><html lang=\"tr\"><head><meta charset=\"utf-8\">"
                + "<title>Kalender</title></head>"
                + "<body style=\"font-family:Segoe UI,sans-serif;background:#2E3238;color:#fff;"
                + "display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">"
                + $"<p style=\"font-size:18px\">{message}</p></body></html>";

            byte[] body = Encoding.UTF8.GetBytes(html);
            byte[] header = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n"
                + "Content-Type: text/html; charset=utf-8\r\n"
                + $"Content-Length: {body.Length}\r\n"
                + "Connection: close\r\n\r\n");

            await stream.WriteAsync(header, cancel).ConfigureAwait(false);
            await stream.WriteAsync(body, cancel).ConfigureAwait(false);
            await stream.FlushAsync(cancel).ConfigureAwait(false);
        }

        public void Dispose() => _listener.Stop();
    }
}
