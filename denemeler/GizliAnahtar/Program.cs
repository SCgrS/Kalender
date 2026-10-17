using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

// Google istemci gizli anahtarının (Client secret) ayar dosyasında şifreli
// durduğunun sınaması (yansımayla):
//  1. 1.10'un düz metin yazdığı eski ayar dosyası okununca değer kaybolmaz,
//     dosya bir kez şifreli hâliyle yeniden yazılır ve düz metin kalmaz;
//     öteki ayarlar olduğu gibi kalır.
//  2. Yeni değer kaydedilince dosyada yalnızca şifreli hâli durur; silinince
//     alan da dosyadan kalkar.
//  3. Çözülemeyen (başka hesaptan kopyalanmış) değer uygulamayı düşürmez.
//  4. Ayarlar penceresindeki kutu noktalarla gizli (PasswordBox) ve
//     görünümü metin kutusunun aynısı.
//
// Uygulamanın klasörü (Paths.RootFolder) %TEMP% altında geçici bir klasöre
// çevrilir: kurulu Kalender'in ayarlarına, Google yetkisine ve günlüğüne
// dokunulmaz. Geçici klasör sonunda silinir.
// Çıkış kodu 0 = hepsi geçti. Kullanım: dotnet run -c Release
internal static class Program
{
    private const BindingFlags Hepsi =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private const string EskiSir = "GOCSPX-deneme-duz-metin-1234567890ab";

    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static int _hata;
    private static int _gecen;

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

    private static object? Oku(object o, string ad) => o.GetType().GetProperty(ad, Hepsi)!.GetValue(o);

    private static void Yaz(object o, string ad, object? deger) => o.GetType().GetProperty(ad, Hepsi)!.SetValue(o, deger);

    private static void Denetle(string ad, bool sonuc, string ayrinti = "")
    {
        if (sonuc)
        {
            _gecen++;
        }
        else
        {
            _hata++;
        }

        Console.WriteLine($"   {(sonuc ? "GEÇTİ" : "KALDI")}: {ad}{(ayrinti.Length > 0 ? " — " + ayrinti : "")}");
    }

    /// <summary>Stil ayarının karşılaştırılacak hâli; DynamicResource ise kaynak adı.</summary>
    private static string? SetterDegeri(object? deger)
    {
        object? anahtar = deger?.GetType().GetProperty("ResourceKey", Hepsi)?.GetValue(deger);
        return anahtar is not null ? "kaynak:" + anahtar : deger?.ToString();
    }

    private static string AyarDosyasi => (string)Tip("Kalender.Core.Paths").GetProperty("SettingsFile", Hepsi)!.GetValue(null)!;

    private static object YeniDepo()
    {
        object depo = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"), nonPublic: true)!;
        depo.GetType().GetMethod("Load", Hepsi)!.Invoke(depo, null);
        return depo;
    }

    private static object Google(object depo) => Oku(Oku(depo, "Current")!, "Google")!;

    private static void Kaydet(object depo) => depo.GetType().GetMethod("Save", Hepsi)!.Invoke(depo, null);

    private static JsonElement GoogleJson()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(AyarDosyasi, Encoding.UTF8));
        return doc.RootElement.GetProperty("Google").Clone();
    }

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

        Type yollar = Tip("Kalender.Core.Paths");
        PropertyInfo kok = yollar.GetProperty("RootFolder", Hepsi)!;
        string gercekKok = (string)kok.GetValue(null)!;
        string gecici = Path.Combine(Path.GetTempPath(), "KalenderGizliAnahtarSinama-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(gecici);
        kok.SetValue(null, gecici);

        try
        {
            Denetle("uygulama klasörü geçici klasöre çevrildi", AyarDosyasi.StartsWith(gecici, StringComparison.OrdinalIgnoreCase)
                && !AyarDosyasi.StartsWith(gercekKok, StringComparison.OrdinalIgnoreCase), AyarDosyasi);
            Sina();
        }
        finally
        {
            kok.SetValue(null, gercekKok);
            Directory.Delete(gecici, recursive: true);
        }

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    private static void Sina()
    {
        // ------------------------------------------------------------------
        Console.WriteLine("1) 1.10'un düz metin yazdığı ayar dosyası");
        {
            string eski = """
                {
                  "Version": 1,
                  "Google": {
                    "ClientId": "deneme.apps.googleusercontent.com",
                    "ClientSecret": "GIZLI",
                    "PollIntervalSeconds": 300,
                    "AccountEmail": "hesap@deneme",
                    "SelectedCalendarIds": [ "takvim@deneme" ]
                  },
                  "StartWithWindows": false,
                  "CheckForUpdates": true
                }
                """.Replace("GIZLI", EskiSir);
            File.WriteAllText(AyarDosyasi, eski, new UTF8Encoding(false));

            object depo = YeniDepo();
            object g = Google(depo);
            Denetle("değer okundu", (string?)Oku(g, "ClientSecret") == EskiSir);
            Denetle("istemci yapılandırması aynı değeri veriyor",
                (string?)Oku(Activator.CreateInstance(Tip("Kalender.Google.SettingsOAuthConfig"), Hepsi, null, new[] { depo }, null)!, "ClientSecret") == EskiSir);

            string yeni = File.ReadAllText(AyarDosyasi, Encoding.UTF8);
            JsonElement gj = GoogleJson();
            Denetle("dosyada düz metin kalmadı", !yeni.Contains(EskiSir, StringComparison.Ordinal)
                && !gj.TryGetProperty("ClientSecret", out _));
            Denetle("dosyada şifreli hâli var", gj.TryGetProperty("ClientSecretSifreli", out JsonElement s)
                && s.GetString() is { Length: > 100 } b64 && Convert.TryFromBase64String(b64, new byte[b64.Length], out _));
            Denetle("öteki ayarlar korundu", gj.GetProperty("ClientId").GetString() == "deneme.apps.googleusercontent.com"
                && gj.GetProperty("PollIntervalSeconds").GetInt32() == 300
                && gj.GetProperty("AccountEmail").GetString() == "hesap@deneme"
                && gj.GetProperty("SelectedCalendarIds")[0].GetString() == "takvim@deneme"
                && yeni.Contains("\"StartWithWindows\": false", StringComparison.Ordinal));
            Denetle("dönüştürme bayrağı indi", !(bool)Oku(g, "DuzMetinOkundu")!);

            DateTime once = File.GetLastWriteTimeUtc(AyarDosyasi);
            Thread.Sleep(50);
            object depo2 = YeniDepo();
            Denetle("yeniden açılınca aynı değer", (string?)Oku(Google(depo2), "ClientSecret") == EskiSir);
            Denetle("ikinci açılışta dosya yeniden yazılmadı", File.GetLastWriteTimeUtc(AyarDosyasi) == once
                && File.ReadAllText(AyarDosyasi, Encoding.UTF8) == yeni);
        }

        // ------------------------------------------------------------------
        Console.WriteLine("2) Yeni değer kaydedilir, sonra silinir");
        {
            object depo = YeniDepo();
            Yaz(Google(depo), "ClientSecret", "GOCSPX-yeni-deger");
            Kaydet(depo);
            string metin = File.ReadAllText(AyarDosyasi, Encoding.UTF8);
            Denetle("yeni değer dosyada düz metin değil", !metin.Contains("GOCSPX-yeni-deger", StringComparison.Ordinal));
            Denetle("yeniden okununca yeni değer", (string?)Oku(Google(YeniDepo()), "ClientSecret") == "GOCSPX-yeni-deger");

            object depo2 = YeniDepo();
            Yaz(Google(depo2), "ClientSecret", null);
            Kaydet(depo2);
            JsonElement gj = GoogleJson();
            Denetle("silinince alan dosyadan kalktı", !gj.TryGetProperty("ClientSecretSifreli", out _) && !gj.TryGetProperty("ClientSecret", out _));
            Denetle("yeniden okununca boş", Oku(Google(YeniDepo()), "ClientSecret") is null);
        }

        // ------------------------------------------------------------------
        Console.WriteLine("3) Çözülemeyen değer (başka hesaptan kopyalanmış dosya)");
        {
            string bozuk = """
                { "Google": { "ClientId": "deneme", "ClientSecretSifreli": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAbozukbozukbozukbozuk==" }, "StartWithWindows": false }
                """;
            File.WriteAllText(AyarDosyasi, bozuk, new UTF8Encoding(false));
            object? depo = null;
            Exception? hata = null;
            try
            {
                depo = YeniDepo();
            }
            catch (Exception ex)
            {
                hata = ex;
            }

            Denetle("ayarlar okunurken hata fırlamadı", hata is null, hata?.GetBaseException().Message ?? "");
            if (depo is not null)
            {
                Denetle("gizli anahtar boş (yeniden girilir)", Oku(Google(depo), "ClientSecret") is null);
                Denetle("öteki ayarlar okundu", (string?)Oku(Google(depo), "ClientId") == "deneme"
                    && !(bool)Oku(Oku(depo, "Current")!, "StartWithWindows")!);
            }
        }

        // ------------------------------------------------------------------
        Console.WriteLine("4) Ayarlar penceresindeki kutu");
        {
            FieldInfo? alan = Tip("Kalender.Ui.SettingsWindow").GetField("ClientSecretBox", Hepsi);
            Denetle("kutu noktalarla gizli (PasswordBox)", alan?.FieldType == typeof(PasswordBox), alan?.FieldType.Name ?? "yok");

            _ = new Application();
            var sozluk = (ResourceDictionary)Application.LoadComponent(
                new Uri("/Kalender;component/Ui/Denetimler.xaml", UriKind.Relative));
            var stil = sozluk[typeof(PasswordBox)] as Style;
            Denetle("PasswordBox için stil var", stil?.TargetType == typeof(PasswordBox));

            var metinStili = sozluk[typeof(TextBox)] as Style;
            Denetle("stil metin kutusununkiyle aynı ayarlar", stil is not null && metinStili is not null
                && metinStili.Setters.OfType<Setter>().Where(s => s.Property.Name != "Template")
                    .All(m => stil.Setters.OfType<Setter>().Any(p => p.Property.Name == m.Property.Name
                        && SetterDegeri(p.Value) == SetterDegeri(m.Value))));

            // Ayarlar penceresinin yaptığı gibi: kutu bir sayfanın içinde, değer
            // yalnızca değiştiyse yazılır; sayfa gizliyken de yeniden yüklenir.
            // Pencere ekranın dışında açılır, odağı almaz.
            var pencere = new Window
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                Width = 400,
                Height = 200,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };
            pencere.Resources.MergedDictionaries.Add(sozluk);
            var sayfa = new StackPanel();
            var kutu = new PasswordBox();
            sayfa.Children.Add(kutu);
            pencere.Content = sayfa;

            Denetle("pencere kurulurken değer yazılıyor", Ata(kutu, "GOCSPX-ilk") is null);
            pencere.Show();
            pencere.UpdateLayout();
            Denetle("kutuya stil uygulandı, içerik alanı var", kutu.Style == stil
                && kutu.Template?.FindName("PART_ContentHost", kutu) is ScrollViewer);
            Denetle("değer kutuda", kutu.Password == "GOCSPX-ilk");
            Denetle("görünürken aynı değerle yeniden yükleme", Ata(kutu, "GOCSPX-ilk") is null);
            Denetle("görünürken yeni değer", Ata(kutu, "GOCSPX-yeni") is null && kutu.Password == "GOCSPX-yeni");

            sayfa.Visibility = Visibility.Collapsed;
            pencere.UpdateLayout();
            Denetle("sayfa gizliyken aynı değerle yeniden yükleme", Ata(kutu, "GOCSPX-yeni") is null);
            Exception? gizliyken = Ata(kutu, "GOCSPX-gizli");
            Denetle("sayfa gizliyken yeni değer", gizliyken is null && kutu.Password == "GOCSPX-gizli",
                gizliyken?.GetType().Name ?? "");

            sayfa.Visibility = Visibility.Visible;
            pencere.UpdateLayout();
            Denetle("sayfa yeniden görününce değer yerinde", kutu.Password == "GOCSPX-gizli");
            pencere.Close();
        }
    }

    /// <summary>SettingsWindow.LoadFromSettings'teki atamanın aynısı; hata olursa onu verir.</summary>
    private static Exception? Ata(PasswordBox kutu, string deger)
    {
        try
        {
            if (kutu.Password != deger)
            {
                kutu.Password = deger;
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
