using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Kalender / Kalender Lite güncelleme bildiriminin sınaması (yansımayla):
// açılıştan bir dakika sonra denetim, yeni sürüm varken pencere isteği,
// haftada bir hatırlatma kuralı, denetim kapalıyken ağa çıkılmaması, indirme
// ve doğrulama hataları, güncelleme penceresinin yazıları ve düğmeleri, eski
// ayar dosyasının yeni sürümde eksiksiz okunması, daha yeni bir sürümün
// yazdığı bilinmeyen ayar alanlarının korunması, indirmede toplam süre değil
// durgunluk sınırı, indirme ortasında kopan bağlantının disk hatası diye
// bildirilmemesi ve hata iletilerinin Türkçe olması.
//
// GitHub yerine bu programın açtığı yerel bir sunucuya sorulur. Kurucu hiç
// başlatılmaz; kurulu uygulamaya, ayar dosyasına, duvar kağıdına dokunulmaz.
// Pencere ekran dışında açılır ve resmi çıktı klasörüne yazılır
// (guncelleme-penceresi.png). Yalnızca %TEMP% altındaki güncelleme indirme
// klasörüne yazar ve sonunda siler.
// Çıkış kodu 0 = hepsi geçti. Kullanım: dotnet run -c Release
internal static class Program
{
    private const BindingFlags Hepsi =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static int _hata;
    private static int _gecen;

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

    private static object? Oku(object o, string ad) => o.GetType().GetProperty(ad, Hepsi)!.GetValue(o);

    private static void Yaz(object o, string ad, object? deger) => o.GetType().GetProperty(ad, Hepsi)!.SetValue(o, deger);

    private static object? DuraganOku(Type t, string ad)
        => t.GetProperty(ad, Hepsi)?.GetValue(null) ?? t.GetField(ad, Hepsi)?.GetValue(null);

    private static object? Cagir(object hedef, string ad, params object?[] arg)
    {
        Type t = hedef as Type ?? hedef.GetType();
        MethodInfo m = t.GetMethod(ad, Hepsi) ?? throw new MissingMethodException(t.Name, ad);
        return m.Invoke(hedef is Type ? null : hedef, arg);
    }

    private static void Denetle(string ad, bool sonuc, string ayrinti = "")
    {
        if (sonuc)
        {
            _gecen++;
            Console.WriteLine($"   GEÇTİ: {ad}{(ayrinti.Length > 0 ? " — " + ayrinti : "")}");
        }
        else
        {
            _hata++;
            Console.WriteLine($"   KALDI: {ad}{(ayrinti.Length > 0 ? " — " + ayrinti : "")}");
        }
    }

    /// <summary>Çalışan dosyanın yukarısında src\Kalender.App'i içeren ilk klasör.</summary>
    private static string DepoKoku()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Kalender.App")))
            {
                return d.FullName;
            }
        }

        throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    // ------------------------------------------------------------ yerel sunucu

    /// <summary>GitHub'ın yerine geçen, tek iş parçacıklı küçük HTTP sunucusu.</summary>
    private sealed class Sunucu : IDisposable
    {
        private readonly TcpListener _dinleyici = new(IPAddress.Loopback, 0);
        private readonly Dictionary<string, (int Durum, byte[] Govde)> _yollar = new();
        private readonly Dictionary<string, (int Parca, TimeSpan Ara, int Dur, bool Sifirla)> _yavas = new();
        private readonly Dictionary<string, int> _sayac = new();

        public Sunucu()
        {
            _dinleyici.Start();
            _ = Task.Run(DonguAsync);
        }

        public string Adres => $"http://127.0.0.1:{((IPEndPoint)_dinleyici.LocalEndpoint).Port}";

        public void Koy(string yol, int durum, byte[] govde)
        {
            lock (_yollar)
            {
                _yollar[yol] = (durum, govde);
                _yavas.Remove(yol);
            }
        }

        /// <summary>
        /// Gövde <paramref name="parca"/> parçada, aralarında <paramref name="ara"/>
        /// bekleyerek gönderilir. <paramref name="dur"/> parçadan sonra hiç veri
        /// gönderilmez, bağlantı açık kalır (asılı kalmış sunucu gibi).
        /// <paramref name="sifirla"/> verilirse bağlantı o noktada açık bırakılmaz,
        /// sıfırlanır (Wi-Fi kopması, karşı tarafın bağlantıyı kesmesi gibi).
        /// </summary>
        public void YavasKoy(string yol, byte[] govde, int parca, TimeSpan ara, int dur = int.MaxValue,
            bool sifirla = false)
        {
            lock (_yollar)
            {
                _yollar[yol] = (200, govde);
                _yavas[yol] = (parca, ara, dur, sifirla);
            }
        }

        public int Istek(string yol)
        {
            lock (_yollar)
            {
                return _sayac.GetValueOrDefault(yol);
            }
        }

        private async Task DonguAsync()
        {
            while (true)
            {
                TcpClient istemci;
                try
                {
                    istemci = await _dinleyici.AcceptTcpClientAsync();
                }
                catch
                {
                    return; // Dinleyici kapandı.
                }

                // Her bağlantı ayrı yürür: asılı bırakılan bir yanıt sonrakileri bekletmesin.
                _ = Task.Run(() => YanitlaAsync(istemci));
            }
        }

        private async Task YanitlaAsync(TcpClient istemci)
        {
            try
            {
                using (istemci)
                {
                    istemci.NoDelay = true;
                    NetworkStream akis = istemci.GetStream();
                    var baslik = new StringBuilder();
                    byte[] tampon = new byte[4096];
                    while (!baslik.ToString().Contains("\r\n\r\n"))
                    {
                        int n = await akis.ReadAsync(tampon);
                        if (n <= 0)
                        {
                            break;
                        }

                        baslik.Append(Encoding.ASCII.GetString(tampon, 0, n));
                    }

                    string yol = baslik.ToString().Split(' ') is { Length: > 1 } p ? p[1] : "/";
                    (int Durum, byte[] Govde) yanit;
                    (int Parca, TimeSpan Ara, int Dur, bool Sifirla)? yavas;
                    lock (_yollar)
                    {
                        _sayac[yol] = _sayac.GetValueOrDefault(yol) + 1;
                        yanit = _yollar.TryGetValue(yol, out var y) ? y : (404, Array.Empty<byte>());
                        yavas = _yavas.TryGetValue(yol, out var v) ? v : null;
                    }

                    byte[] ust = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 {yanit.Durum} X\r\nContent-Length: {yanit.Govde.Length}\r\n"
                        + "Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n");
                    await akis.WriteAsync(ust);

                    if (yavas is not (int parca, TimeSpan ara, int dur, bool sifirla))
                    {
                        await akis.WriteAsync(yanit.Govde);
                        return;
                    }

                    int boy = (yanit.Govde.Length + parca - 1) / parca;
                    for (int i = 0; i < parca; i++)
                    {
                        if (i >= dur && sifirla)
                        {
                            // Bekleme süresi sıfır: soket düzgün kapanmaz, bağlantı sıfırlanır (RST).
                            // Akışı kapatmak önce düzgün kapatırdı (istemci yalnız gövdenin
                            // erken bittiğini görürdü); bu yüzden soketin kendisi kapatılır.
                            istemci.Client.LingerState = new LingerOption(true, 0);
                            istemci.Client.Close();
                            return;
                        }

                        if (i >= dur)
                        {
                            // Veri yok, bağlantı açık: istemci kesene dek (en çok 30 sn) bekle.
                            using var sinir = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            while (await akis.ReadAsync(tampon, sinir.Token) > 0)
                            {
                            }

                            return;
                        }

                        if (i > 0)
                        {
                            await Task.Delay(ara);
                        }

                        int bas = i * boy;
                        await akis.WriteAsync(yanit.Govde.AsMemory(bas, Math.Min(boy, yanit.Govde.Length - bas)));
                        await akis.FlushAsync();
                    }
                }
            }
            catch
            {
                // İstemci bağlantıyı kesti; sınama sonucu zaten bunu gösterir.
            }
        }

        public void Dispose() => _dinleyici.Stop();
    }

    // -------------------------------------------------------------- yardımcılar

    private static readonly Type Hizmet = Tip("Kalender.Core.UpdateService");

    private static string AssetAdi => (string)Hizmet.GetField("AssetName", Hepsi)!.GetValue(null)!;

    private static Version KuruluSurum => (Version)DuraganOku(Hizmet, "CurrentVersion")!;

    private static byte[] SurumYaniti(string etiket, string indirmeAdresi = "https://example.invalid/kurulum.exe",
        long boy = 0, string? sha = null)
    {
        var varlik = new Dictionary<string, object?>
        {
            ["name"] = AssetAdi,
            ["browser_download_url"] = indirmeAdresi,
            ["size"] = boy,
        };
        if (sha is not null)
        {
            varlik["digest"] = "sha256:" + sha;
        }

        return JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["tag_name"] = etiket,
            ["html_url"] = "https://example.invalid/surum",
            ["assets"] = new[] { varlik },
        });
    }

    /// <summary>Ayar dosyasına dokunmayan, bellekte duran ayarlarla bir güncelleme hizmeti.</summary>
    private static (object Hizmet, object Ayarlar, Func<int> Istek) YeniHizmet()
    {
        object store = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"), nonPublic: true)!;
        object hizmet = Activator.CreateInstance(Hizmet, Hepsi, null, new[] { store }, null)!;

        int istek = 0;
        Hizmet.GetEvent("PromptRequested", Hepsi)!.AddEventHandler(hizmet, new Action(() => istek++));
        return (hizmet, Oku(store, "Current")!, () => istek);
    }

    private static Task DenetleAsync(object hizmet, bool hatirlat, bool elle = false)
        => (Task)Cagir(hizmet, "CheckAsync", hatirlat, elle)!;

    /// <summary>Sürümün kullanıcıya gösterilen yazılışı (iki bölümlü; uygulamanın kendi işleviyle).</summary>
    private static string Kisa(Version surum)
        => (string)Cagir(Tip("Kalender.Core.Installer"), "VersionText", surum)!;

    private static string Durum(object hizmet) => Oku(hizmet, "State")!.ToString()!;

    private static object SurumBilgisi(Version surum, string adres, long boy, string? sha)
        => Activator.CreateInstance(Tip("Kalender.Core.UpdateInfo"), Hepsi, null,
            new object?[] { surum, adres, boy, sha, "https://example.invalid/surum" }, null)!;

    private static T Bul<T>(Window pencere, string ad) where T : class => (T)pencere.FindName(ad);

    private static string Metin(Window pencere, string ad) => Bul<TextBlock>(pencere, ad).Text;

    // ------------------------------------------------------------------ sınama

    /// <summary>
    /// Uygulamanın duvar kâğıdı fabrikasını (DuvarKagidiKayitlari.Ac) hata veren
    /// bir fabrikayla değiştirir: bu sınama Windows'un duvar kâğıdı arayüzünü hiç
    /// kullanmıyor; bir yol kazara açmaya kalkarsa gerçek duvar kâğıdına dokunulmaz.
    /// </summary>
    private static void DuvarKagidiniKapat()
    {
        const BindingFlags Her = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        Type arayuz = Uyg.GetType("Kalender.Interop.IDesktopWallpaper", throwOnError: true)!;
        Func<object> hata = () => throw new InvalidOperationException("gerçek arayüz");
        Uyg.GetType("Kalender.Desktop.DuvarKagidiKayitlari", throwOnError: true)!.GetProperty("Ac", Her)!.SetValue(
            null,
            Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(arayuz), hata,
                typeof(Program).GetMethod(nameof(Uret), Her)!.MakeGenericMethod(arayuz)));
    }

    private static T Uret<T>(Func<object> kaynak) => (T)kaynak();

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Duvar kâğıdı arayüzü bu sınamada kullanılmıyor; açılmaya kalkılırsa hata verir.
        DuvarKagidiniKapat();

        var cerceve = new DispatcherFrame();
        Exception? beklenmeyen = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try
            {
                await CalistirAsync();
            }
            catch (Exception ex)
            {
                beklenmeyen = ex;
            }
            finally
            {
                cerceve.Continue = false;
            }
        });
        Dispatcher.PushFrame(cerceve);

        if (beklenmeyen is not null)
        {
            Console.WriteLine("BEKLENMEYEN HATA: " + beklenmeyen);
            return 2;
        }

        Console.WriteLine();
        Console.WriteLine($"{_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    private static async Task CalistirAsync()
    {
        string urun = Uyg.GetCustomAttribute<AssemblyProductAttribute>()!.Product;
        Version kurulu = KuruluSurum;
        Console.WriteLine($"{urun} {Kisa(kurulu)} güncelleme sınaması");

        Console.WriteLine("0) Sürüm numarasının yazılışı");
        Denetle("sürüm iki bölümlü yazılır", Kisa(new Version(1, 9, 0)) == "1.9" && Kisa(new Version(1, 9)) == "1.9"
            && Kisa(new Version(1, 10, 0, 0)) == "1.10");
        Denetle("eski üç bölümlü sürüm olduğu gibi yazılır", Kisa(new Version(1, 6, 4)) == "1.6.4");
        Denetle("bu sürümün numarası iki bölümlü", Kisa(kurulu).Count(c => c == '.') == 1
            && (string)DuraganOku(Tip("Kalender.Core.Installer"), "Version")! == Kisa(kurulu), Kisa(kurulu));

        using var sunucu = new Sunucu();
        const string yol = "/releases/latest";
        Hizmet.GetProperty("LatestReleaseApi", Hepsi)!.SetValue(null, sunucu.Adres + yol);

        // ------------------------------------------------------------ zamanlama
        Console.WriteLine("1) Zamanlama ve hatırlatma kuralı");

        Denetle("ilk denetim açılıştan 1 dakika sonra",
            (TimeSpan)DuraganOku(Hizmet, "FirstCheckDelay")! == TimeSpan.FromMinutes(1));
        Denetle("hatırlatma aralığı 7 gün",
            (TimeSpan)DuraganOku(Hizmet, "ReminderInterval")! == TimeSpan.FromDays(7));

        DateTime simdi = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);
        bool Zamani(DateTime? son) => (bool)Cagir(Hizmet, "ReminderDue", son, simdi)!;
        Denetle("hiç gösterilmediyse gösterilir", Zamani(null));
        Denetle("1 gün önce gösterildiyse gösterilmez", !Zamani(simdi.AddDays(-1)));
        Denetle("6 gün 23 saat önce gösterildiyse gösterilmez", !Zamani(simdi.AddDays(-7).AddHours(1)));
        Denetle("tam 7 gün önce gösterildiyse gösterilir", Zamani(simdi.AddDays(-7)));
        Denetle("30 gün önce gösterildiyse gösterilir", Zamani(simdi.AddDays(-30)));
        Denetle("kayıt gelecekteyse (saat geri alınmış) gösterilir", Zamani(simdi.AddDays(3)));

        // --------------------------------------------------------------- denetim
        Console.WriteLine("2) Denetim ve pencere isteği");

        sunucu.Koy(yol, 200, SurumYaniti("v99.0"));

        {
            var (h, _, istek) = YeniHizmet();
            await DenetleAsync(h, true);
            Denetle("iki bölümlü etiketli yeni sürüm bulunur", Durum(h) == "Available"
                && Oku(Oku(h, "Available")!, "Version")!.ToString() == "99.0.0", Durum(h));
            Denetle("zamanlanmış denetim pencere ister", istek() == 1, $"{istek()} istek");
            ((IDisposable)h).Dispose();
        }

        {
            var (h, _, istek) = YeniHizmet();
            await DenetleAsync(h, false);
            Denetle("ayarlar penceresinin denetimi pencere istemez", Durum(h) == "Available" && istek() == 0);
            ((IDisposable)h).Dispose();
        }

        {
            var (h, ayar, istek) = YeniHizmet();
            Yaz(ayar, "UpdatePromptShownUtc", DateTime.UtcNow.AddDays(-3));
            await DenetleAsync(h, true);
            Denetle("3 gün önce gösterildiyse yeniden istenmez", Durum(h) == "Available" && istek() == 0);

            Yaz(ayar, "UpdatePromptShownUtc", DateTime.UtcNow.AddDays(-8));
            await DenetleAsync(h, true);
            Denetle("8 gün önce gösterildiyse yeniden istenir", istek() == 1, $"{istek()} istek");
            ((IDisposable)h).Dispose();
        }

        {
            var (h, ayar, istek) = YeniHizmet();
            Yaz(ayar, "CheckForUpdates", false);
            int once = sunucu.Istek(yol);
            await DenetleAsync(h, true);
            Denetle("denetim kapalıyken ağa çıkılmaz, pencere istenmez",
                sunucu.Istek(yol) == once && istek() == 0 && Durum(h) == "Idle");
            ((IDisposable)h).Dispose();
        }

        {
            // "Şimdi denetle": otomatik denetim kapalıyken de sorar, pencere istemez.
            var (h, ayar, istek) = YeniHizmet();
            Yaz(ayar, "CheckForUpdates", false);
            int once = sunucu.Istek(yol);
            await DenetleAsync(h, false, elle: true);
            Denetle("Şimdi denetle, otomatik denetim kapalıyken de yeni sürümü bulur",
                sunucu.Istek(yol) == once + 1 && Durum(h) == "Available" && (bool)Oku(h, "ElleDenetlendi")!
                && !(bool)Oku(h, "Checking")! && istek() == 0, Durum(h));
            Denetle("Şimdi denetle ayarı değiştirmez", (bool)Oku(ayar, "CheckForUpdates")! == false);
            ((IDisposable)h).Dispose();
        }

        {
            sunucu.Koy(yol, 200, SurumYaniti("v" + kurulu.ToString(3)));
            var (h, _, istek) = YeniHizmet();
            await DenetleAsync(h, true);
            Denetle("kurulu sürüm en son sürümse pencere istenmez",
                Durum(h) == "Idle" && istek() == 0 && Oku(h, "Available") is null);

            sunucu.Koy(yol, 404, Array.Empty<byte>());
            await DenetleAsync(h, true);
            Denetle("sürüm bilgisi alınamazsa sessizce geçilir",
                (bool)Oku(h, "LastCheckFailed")! && istek() == 0 && Durum(h) == "Idle");
            ((IDisposable)h).Dispose();
        }

        {
            using JsonDocument belge = JsonDocument.Parse(SurumYaniti("v99.1.0", boy: 42, sha: "AB12"));
            object? bilgi = Cagir(Hizmet, "Parse", belge.RootElement);
            Denetle("sürüm yanıtı çözülür", bilgi is not null
                && Oku(bilgi, "Version")!.ToString() == "99.1.0"
                && (long)Oku(bilgi, "Size")! == 42 && (string?)Oku(bilgi, "Sha256") == "AB12");

            using JsonDocument duz = JsonDocument.Parse(SurumYaniti("v99.1.0", indirmeAdresi: "http://example.invalid/x"));
            Denetle("şifresiz (http) indirme adresi kabul edilmez", Cagir(Hizmet, "Parse", duz.RootElement) is null);
        }

        // -------------------------------------------------------------- pencere
        Console.WriteLine("3) Güncelleme penceresi");

        byte[] cop = RandomNumberGenerator.GetBytes(300_000);
        string copSha = Convert.ToHexString(SHA256.HashData(cop));
        sunucu.Koy("/kurulum.exe", 200, cop);
        sunucu.Koy(yol, 200, SurumYaniti("v99.0"));

        {
            var (h, _, _) = YeniHizmet();
            await DenetleAsync(h, true);

            var durumlar = new List<string>();
            Hizmet.GetEvent("Changed", Hepsi)!.AddEventHandler(h, new Action(() => durumlar.Add(Durum(h))));

            var pencere = (Window)Activator.CreateInstance(
                Tip("Kalender.Ui.SetupWindow"), Hepsi, null, new[] { h }, null)!;

            Button guncelle = Bul<Button>(pencere, "PrimaryButton");
            Button sonra = Bul<Button>(pencere, "CancelButton");

            Denetle("başlık ürünün adını taşır", pencere.Title == urun + " güncellemesi", pencere.Title);
            Denetle("yeni sürüm çıktığını söyler", Metin(pencere, "HeadingText").EndsWith("yeni sürümü çıktı"),
                Metin(pencere, "HeadingText"));
            Denetle("kurulu ve yeni sürümü iki bölümlü yazar",
                Metin(pencere, "BodyText").Contains($"{Kisa(kurulu)} sürümü kurulu")
                && Metin(pencere, "BodyText").Contains("yeni sürüm 99.0. "), Metin(pencere, "BodyText"));
            Denetle("ayarlara dokunulmayacağını söyler", Metin(pencere, "BodyText").Contains("Ayarlarınıza dokunulmaz"));
            Denetle("haftalık hatırlatmayı söyler", Metin(pencere, "NoteText").Contains("bir hafta sonra"));
            Denetle("düğmeler: Güncelle ve Sonra", (string)guncelle.Content == "Güncelle"
                && (string)sonra.Content == "Sonra" && guncelle.IsEnabled && sonra.IsEnabled);
            Denetle("klavye odağını almaz, öndedir", !pencere.ShowActivated && pencere.Topmost);
            Denetle("kurulum klasörü kutusu gizli",
                Bul<FrameworkElement>(pencere, "PathBox").Visibility == Visibility.Collapsed);

            // Ekran dışında aç, resmini al.
            pencere.WindowStartupLocation = WindowStartupLocation.Manual;
            pencere.Left = -32000;
            pencere.Top = -32000;
            pencere.ShowInTaskbar = false;
            pencere.Show();
            pencere.UpdateLayout();
            string resim = Path.Combine(AppContext.BaseDirectory, "guncelleme-penceresi.png");
            ResimAl(pencere, resim);
            Denetle("pencere açılır ve çizilir", pencere.IsVisible && new FileInfo(resim).Length > 2000, resim);

            // İndirme başarılı ama dosya bu uygulama değil: kurucu başlatılmaz, hata gösterilir.
            Yaz(h, "Available", SurumBilgisi(new Version(99, 0, 0), sunucu.Adres + "/kurulum.exe", cop.Length, copSha));
            guncelle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await BekleAsync(() => Durum(h) == "Failed");

            Denetle("Güncelle'ye basınca indirme başlar", durumlar.Contains("Downloading")
                && sunucu.Istek("/kurulum.exe") == 1, string.Join(" > ", durumlar.Distinct()));
            Denetle("yabancı dosya kurulmaz", Durum(h) == "Failed"
                && ((string)Oku(h, "Error")!).Contains("beklenen " + urun), (string?)Oku(h, "Error") ?? "");
            Denetle("pencere hatayı gösterir", Metin(pencere, "HeadingText") == "Güncelleme yapılamadı"
                && (string)guncelle.Content == "Yeniden dene" && (string)sonra.Content == "Kapat"
                && guncelle.IsEnabled && sonra.IsEnabled);

            // Özeti tutmayan dosya.
            Yaz(h, "Available", SurumBilgisi(new Version(99, 0, 0), sunucu.Adres + "/kurulum.exe", cop.Length,
                new string('0', 64)));
            guncelle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await BekleAsync(() => Durum(h) == "Failed" && sunucu.Istek("/kurulum.exe") == 2);
            Denetle("SHA-256 özeti tutmayan dosya kurulmaz", ((string)Oku(h, "Error")!).Contains("SHA-256"),
                (string?)Oku(h, "Error") ?? "");

            // Yeni sürüm artık yok: pencere kendiliğinden kapanır.
            bool kapandi = false;
            pencere.Closed += (_, _) => kapandi = true;
            sunucu.Koy(yol, 200, SurumYaniti("v" + kurulu.ToString(3)));
            await DenetleAsync(h, true);
            Denetle("yeni sürüm kalmayınca pencere kapanır", kapandi);
            if (!kapandi)
            {
                pencere.Close();
            }

            ((IDisposable)h).Dispose();
        }

        // ------------------------------------------------------------ doğrulama
        Console.WriteLine("4) İndirilen dosyanın doğrulanması");

        string exe = Path.Combine(DepoKoku(), "src", "Kalender.App", "bin", "Release", "net10.0-windows",
            Uyg.GetName().Name + ".exe");
        if (File.Exists(exe))
        {
            bool Dogrula(Version surum)
            {
                try
                {
                    Cagir(Hizmet, "Verify", exe, SurumBilgisi(surum, "https://example.invalid/x", 0, null));
                    return true;
                }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
                {
                    return false;
                }
            }

            Denetle("aynı sürümün kendi dosyası kabul edilir", Dogrula(kurulu));
            Denetle("başka sürüm numaralı dosya reddedilir", !Dogrula(new Version(99, 0, 0)));
        }
        else
        {
            Console.WriteLine("   ATLANDI: derlenmiş exe yok: " + exe);
        }

        // --------------------------------------------------------------- ayarlar
        Console.WriteLine("5) Ayarların korunması");

        {
            Type ayarTipi = Tip("Kalender.Settings.AppSettings");
            var secenek = (JsonSerializerOptions)Tip("Kalender.Settings.SettingsStore")
                .GetField("JsonOptions", Hepsi)!.GetValue(null)!;

            // Önceki sürümün yazdığı gibi: yeni alanlar yok, kullanıcının seçimleri var.
            const string eski = """
                {
                  "Appearance": { "SolidColor": "#123456" },
                  "StartWithWindows": false,
                  "CalendarHidden": true,
                  "LogLevel": 1,
                  "FirstRunDone": true,
                  "DarkSettingsWindow": true,
                  "Monitors": { "EKRAN-1": { } }
                }
                """;

            object a = JsonSerializer.Deserialize(eski, ayarTipi, secenek)!;
            bool Ayni(object x) => (string)Oku(Oku(x, "Appearance")!, "SolidColor")! == "#123456"
                && !(bool)Oku(x, "StartWithWindows")! && (bool)Oku(x, "CalendarHidden")!
                && (int)Oku(x, "LogLevel")! == 1 && (bool)Oku(x, "FirstRunDone")!
                && (bool?)Oku(x, "DarkSettingsWindow") == true
                && ((System.Collections.IDictionary)Oku(x, "Monitors")!).Contains("EKRAN-1");

            Denetle("eski ayar dosyası eksiksiz okunur", Ayni(a));
            Denetle("yeni alanlar varsayılanla gelir: denetim açık, hatırlatma hiç gösterilmemiş",
                (bool)Oku(a, "CheckForUpdates")! && Oku(a, "UpdatePromptShownUtc") is null);

            DateTime an = new(2026, 10, 1, 8, 5, 0, DateTimeKind.Utc);
            Yaz(a, "UpdatePromptShownUtc", an);
            string yeni = JsonSerializer.Serialize(a, ayarTipi, secenek);
            object b = JsonSerializer.Deserialize(yeni, ayarTipi, secenek)!;
            Denetle("hatırlatma zamanı yazılıp okunur, öteki ayarlar değişmez",
                Ayni(b) && (DateTime?)Oku(b, "UpdatePromptShownUtc") == an);

            // Daha yeni bir sürümün yazdığı, bu sürümün tanımadığı alanlar: dosya
            // yeniden yazılırken silinmemeli (eskiye dönülse bile). Lite'ta "Google"
            // bütünüyle bilinmeyen bir alan, Kalender'de içindeki alan bilinmiyor.
            const string gelecek = """
                {
                  "GelecekAlan": { "Liste": [1, 2, 3], "Yazi": "ğüşıöç" },
                  "Appearance": {
                    "SolidColor": "#123456",
                    "GelecekRenk": "#ABCDEF",
                    "Colors": { "Weekday": "#111111", "GelecekYazi": "#010203" }
                  },
                  "Holidays": { "Enabled": false, "GelecekTatil": 7 },
                  "Google": { "GelecekGoogle": true },
                  "Monitors": { "EKRAN-1": { "ScalePercent": 120, "GelecekEkran": "x" } },
                  "StartWithWindows": false
                }
                """;

            object c = JsonSerializer.Deserialize(gelecek, ayarTipi, secenek)!;
            string yazilan = JsonSerializer.Serialize(c, ayarTipi, secenek);
            using JsonDocument okunan = JsonDocument.Parse(yazilan);
            JsonElement k = okunan.RootElement;
            JsonElement gorunum = k.GetProperty("Appearance");
            bool korundu = k.GetProperty("GelecekAlan").GetProperty("Yazi").GetString() == "ğüşıöç"
                && k.GetProperty("GelecekAlan").GetProperty("Liste").GetArrayLength() == 3
                && gorunum.GetProperty("GelecekRenk").GetString() == "#ABCDEF"
                && gorunum.GetProperty("Colors").GetProperty("GelecekYazi").GetString() == "#010203"
                && k.GetProperty("Holidays").GetProperty("GelecekTatil").GetInt32() == 7
                && k.GetProperty("Google").GetProperty("GelecekGoogle").GetBoolean()
                && k.GetProperty("Monitors").GetProperty("EKRAN-1").GetProperty("GelecekEkran").GetString() == "x";
            Denetle("bu sürümün tanımadığı alanlar (her düzeyde) yeniden yazılırken korunur", korundu,
                korundu ? "" : yazilan);
            Denetle("tanınan alanlar yine okunur ve yazılır",
                (string)Oku(Oku(c, "Appearance")!, "SolidColor")! == "#123456"
                && (string?)Oku(Oku(Oku(c, "Appearance")!, "Colors")!, "Weekday") == "#111111"
                && !(bool)Oku(c, "StartWithWindows")! && !(bool)Oku(Oku(c, "Holidays")!, "Enabled")!
                && gorunum.GetProperty("SolidColor").GetString() == "#123456" && !k.GetProperty("StartWithWindows").GetBoolean());
            Denetle("bilinmeyen alan yoksa dosyaya fazladan bir şey yazılmaz",
                !JsonSerializer.Serialize(Activator.CreateInstance(ayarTipi, true), ayarTipi, secenek).Contains("Bilinmeyen")
                && !yazilan.Contains("Bilinmeyen"));
        }

        // -------------------------------------------------------------- indirme
        Console.WriteLine("6) İndirme: toplam süre değil durgunluk sınırı; Türkçe hata iletileri");

        {
            PropertyInfo durgunluk = Hizmet.GetProperty("DownloadStallTimeout", Hepsi)!;
            Denetle("durgunluk sınırı 60 saniye, toplam süre sınırı yok",
                (TimeSpan)durgunluk.GetValue(null)! == TimeSpan.FromSeconds(60)
                && Hizmet.GetField("DownloadTimeout", Hepsi) is null);

            durgunluk.SetValue(null, TimeSpan.FromSeconds(2));
            try
            {
                byte[] govde = RandomNumberGenerator.GetBytes(350_000);
                string sha = Convert.ToHexString(SHA256.HashData(govde));
                var (h, _, _) = YeniHizmet();

                // Yavaş ama ilerleyen bağlantı: 7 parça, aralarında yarım saniye;
                // toplam ~3 sn, yani durgunluk sınırından uzun.
                sunucu.YavasKoy("/yavas.exe", govde, parca: 7, ara: TimeSpan.FromMilliseconds(500));
                Yaz(h, "Available", SurumBilgisi(new Version(99, 0, 0), sunucu.Adres + "/yavas.exe", govde.Length, sha));
                var saat = System.Diagnostics.Stopwatch.StartNew();
                await (Task)Cagir(h, "InstallAsync", false)!;
                string hata = (string?)Oku(h, "Error") ?? "";

                // İndirme bitip özet tuttuysa sıra sürüm denetimine gelir; rastgele
                // baytlar uygulama olmadığı için orada durur, kurucu başlatılmaz.
                Denetle("yavaş ama ilerleyen indirme, durgunluk sınırından uzun sürse de tamamlanır",
                    saat.Elapsed > TimeSpan.FromSeconds(2.5) && hata.Contains("beklenen " + urun),
                    $"{saat.Elapsed.TotalSeconds:0.0} sn; {hata}");

                // Asılı kalan bağlantı: ilk parçadan sonra hiç veri gelmiyor.
                sunucu.YavasKoy("/asili.exe", govde, parca: 7, ara: TimeSpan.Zero, dur: 1);
                Yaz(h, "Available", SurumBilgisi(new Version(99, 0, 0), sunucu.Adres + "/asili.exe", govde.Length, sha));
                saat.Restart();
                await (Task)Cagir(h, "InstallAsync", false)!;
                hata = (string?)Oku(h, "Error") ?? "";
                Denetle("hiç veri gelmeyen indirme, durgunluk sınırı dolunca kesilir",
                    Durum(h) == "Failed" && saat.Elapsed >= TimeSpan.FromSeconds(1.5) && saat.Elapsed < TimeSpan.FromSeconds(10),
                    $"{saat.Elapsed.TotalSeconds:0.0} sn");
                Denetle("kesilen indirmenin iletisi Türkçe ve sade",
                    hata.Contains("2 saniye boyunca hiç ilerlemediği için durduruldu")
                    && !hata.Contains("cancel", StringComparison.OrdinalIgnoreCase), hata);

                // Bağlantı indirmenin ortasında sıfırlanıyor (Wi-Fi kopması gibi). .NET
                // bunu HttpIOException olarak değil, düz IOException olarak verir.
                // Parçalar arasındaki bekleme, kopmanın başlıklar alındıktan sonra,
                // gövde okunurken olmasını sağlar (ilerleme sıfırdan büyük).
                sunucu.YavasKoy("/kopan.exe", govde, parca: 7, ara: TimeSpan.FromMilliseconds(300), dur: 2, sifirla: true);
                Yaz(h, "Available", SurumBilgisi(new Version(99, 0, 0), sunucu.Adres + "/kopan.exe", govde.Length, sha));
                await (Task)Cagir(h, "InstallAsync", false)!;
                hata = (string?)Oku(h, "Error") ?? "";
                Denetle("indirmenin ortasında sıfırlanan bağlantı disk hatası değil, kopma olarak bildirilir",
                    Durum(h) == "Failed" && (int)Oku(h, "Progress")! > 0 && hata.Contains("koptu") && !hata.Contains("disk"),
                    $"%{Oku(h, "Progress")}; {hata}");

                // Gerçek disk hatası yine disk hatası: indirilen dosyanın yerinde aynı adlı bir klasör var.
                sunucu.Koy("/tam.exe", 200, govde);
                string engel = Path.Combine((string)DuraganOku(Hizmet, "DownloadFolder")!,
                    $"{Path.GetFileNameWithoutExtension(AssetAdi)}-{Kisa(new Version(99, 0, 0))}.exe.indiriliyor");
                File.Delete(engel); // Yarıda kalan indirmenin dosyası.
                Directory.CreateDirectory(engel);
                Yaz(h, "Available", SurumBilgisi(new Version(99, 0, 0), sunucu.Adres + "/tam.exe", govde.Length, sha));
                await (Task)Cagir(h, "InstallAsync", false)!;
                hata = (string?)Oku(h, "Error") ?? "";
                Directory.Delete(engel);
                Denetle("diske yazılamayan indirme disk hatası olarak bildirilir",
                    Durum(h) == "Failed" && hata == "İndirilen dosya diske yazılamadı.", hata);
                ((IDisposable)h).Dispose();
            }
            finally
            {
                durgunluk.SetValue(null, TimeSpan.FromSeconds(60));
            }

            string Ileti(Exception ex) => (string)Cagir(Hizmet, "UserMessage", ex)!;
            Denetle("sunucu hatasında HTTP kodu Türkçe iletiyle verilir",
                Ileti(new HttpRequestException("Response status code does not indicate success: 404 (Not Found).",
                    null, HttpStatusCode.NotFound)) == "Yeni sürüm indirilemedi: sunucu dosyayı vermedi (HTTP 404).");
            Denetle("bağlanamama ve kopma Türkçe",
                Ileti(new HttpRequestException("No such host is known.")).Contains("bağlanılamadı")
                && Ileti(new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.")).Contains("koptu"));
            Denetle("disk hatası Türkçe", Ileti(new IOException("There is not enough space on the disk.")) == "İndirilen dosya diske yazılamadı.");
            Denetle("uygulamanın kendi Türkçe iletisi olduğu gibi kalır",
                Ileti(new InvalidDataException("İndirilen dosya eksik (1 / 2 bayt).")) == "İndirilen dosya eksik (1 / 2 bayt).");
            Denetle("bilinmeyen hatada İngilizce ileti gösterilmez",
                Ileti(new NotSupportedException("Specified method is not supported.")) == "Yeni sürüm indirilemedi.");
        }

        // Sınamanın indirdiği dosyalar kalmasın.
        string indirme = (string)DuraganOku(Hizmet, "DownloadFolder")!;
        if (Directory.Exists(indirme))
        {
            Directory.Delete(indirme, recursive: true);
        }
    }

    private static async Task BekleAsync(Func<bool> kosul)
    {
        for (int i = 0; i < 200 && !kosul(); i++)
        {
            await Task.Delay(50);
        }
    }

    /// <summary>Pencerenin içeriğini beyaz zemin üzerine PNG olarak yazar.</summary>
    private static void ResimAl(Window pencere, string dosya)
    {
        var kok = (FrameworkElement)pencere.Content;
        int en = (int)Math.Ceiling(kok.ActualWidth);
        int boy = (int)Math.Ceiling(kok.ActualHeight);
        if (en <= 0 || boy <= 0)
        {
            return;
        }

        var cizim = new DrawingVisual();
        using (DrawingContext dc = cizim.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, en, boy));
            dc.DrawRectangle(new VisualBrush(kok), null, new Rect(0, 0, en, boy));
        }

        var bit = new RenderTargetBitmap(en * 2, boy * 2, 192, 192, PixelFormats.Pbgra32);
        bit.Render(cizim);

        var yazici = new PngBitmapEncoder();
        yazici.Frames.Add(BitmapFrame.Create(bit));
        using FileStream f = File.Create(dosya);
        yazici.Save(f);
    }
}
