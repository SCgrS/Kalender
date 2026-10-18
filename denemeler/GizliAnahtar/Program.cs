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
//  5. Ayarlar penceresindeki kutu noktalarla gizli (PasswordBox) ve
//     görünümü metin kutusunun aynısı.
//  4. (1.17) İstemci kimliği, hesabın e-posta adresi ve seçili takvimler de
//     şifreli: 1.16'nın düz yazdığı dosya bir kez şifreliye çevrilir, yedekte
//     de düz metin kalmaz; yerinde değişen takvim listesi kaydedilir; liste
//     değişmedikçe dosya her kayıtta değişmez; çözülemeyen değerler düşürmez.
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
            Denetle("öteki ayarlar korundu", (string?)Oku(g, "ClientId") == "deneme.apps.googleusercontent.com"
                && gj.GetProperty("PollIntervalSeconds").GetInt32() == 300
                && (string?)Oku(g, "AccountEmail") == "hesap@deneme"
                && ((List<string>)Oku(g, "SelectedCalendarIds")!).SequenceEqual(new[] { "takvim@deneme" })
                && yeni.Contains("\"StartWithWindows\": false", StringComparison.Ordinal));
            Denetle("istemci kimliği, e-posta ve takvimler de şifreli (1.17)",
                !yeni.Contains("deneme.apps.googleusercontent.com", StringComparison.Ordinal)
                && !yeni.Contains("@deneme", StringComparison.Ordinal)
                && gj.TryGetProperty("ClientIdSifreli", out _) && gj.TryGetProperty("AccountEmailSifreli", out _)
                && gj.TryGetProperty("SelectedCalendarIdsSifreli", out _)
                && !gj.TryGetProperty("ClientId", out _) && !gj.TryGetProperty("AccountEmail", out _)
                && !gj.TryGetProperty("SelectedCalendarIds", out _));
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
        Console.WriteLine("4) 1.16'nın yazdığı dosya (gizli anahtar şifreli, öbürleri düz)");
        {
            string sifreliSir = (string)Tip("Kalender.Settings.GizliDeger").GetMethod("Sifrele", Hepsi)!
                .Invoke(null, new object?[] { "GOCSPX-116", "deneme" })!;
            string eski = """
                {
                  "Version": 1,
                  "Google": {
                    "ClientId": "kimlik-116.apps.googleusercontent.com",
                    "ClientSecretSifreli": "SIFRELI",
                    "PollIntervalSeconds": 900,
                    "SelectedCalendarIds": [ "ben@ornek.com", "ortak@group.calendar.google.com" ],
                    "SelectedTaskListIds": [ "liste1" ],
                    "TaskColor": "#123456",
                    "AccountEmail": "ben@ornek.com"
                  },
                  "StartWithWindows": false
                }
                """.Replace("SIFRELI", sifreliSir);
            File.WriteAllText(AyarDosyasi, eski, new UTF8Encoding(false));
            // Uygulamanın her kayıtta bıraktığı yedek de 1.16'nın düz metnini taşır.
            File.WriteAllText(AyarDosyasi + ".yedek", eski, new UTF8Encoding(false));

            object depo = YeniDepo();
            object g = Google(depo);
            string yeni = File.ReadAllText(AyarDosyasi, Encoding.UTF8);
            Denetle("değerler okundu", (string?)Oku(g, "ClientId") == "kimlik-116.apps.googleusercontent.com"
                && (string?)Oku(g, "ClientSecret") == "GOCSPX-116" && (string?)Oku(g, "AccountEmail") == "ben@ornek.com"
                && ((List<string>)Oku(g, "SelectedCalendarIds")!).SequenceEqual(new[] { "ben@ornek.com", "ortak@group.calendar.google.com" })
                && ((List<string>)Oku(g, "SelectedTaskListIds")!).SequenceEqual(new[] { "liste1" }));
            Denetle("dosya şifreliye çevrildi, düz metin kalmadı",
                !yeni.Contains("kimlik-116", StringComparison.Ordinal) && !yeni.Contains("ornek.com", StringComparison.Ordinal)
                && !yeni.Contains("group.calendar", StringComparison.Ordinal), yeni.Length.ToString());
            Denetle("düz metinli yedek silindi", !File.Exists(AyarDosyasi + ".yedek")
                || !File.ReadAllText(AyarDosyasi + ".yedek", Encoding.UTF8).Contains("ornek.com", StringComparison.Ordinal));
            Denetle("öteki ayarlar korundu", GoogleJson().GetProperty("PollIntervalSeconds").GetInt32() == 900
                && GoogleJson().GetProperty("TaskColor").GetString() == "#123456"
                && yeni.Contains("\"StartWithWindows\": false", StringComparison.Ordinal));
            Denetle("istemci yapılandırması aynı kimliği veriyor",
                (string?)Oku(Activator.CreateInstance(Tip("Kalender.Google.SettingsOAuthConfig"), Hepsi, null, new[] { depo }, null)!, "ClientId")
                == "kimlik-116.apps.googleusercontent.com");

            object depo2 = YeniDepo();
            Denetle("yeniden açılınca aynı değerler, dosya yeniden yazılmadı",
                (string?)Oku(Google(depo2), "AccountEmail") == "ben@ornek.com"
                && ((List<string>)Oku(Google(depo2), "SelectedCalendarIds")!).Count == 2
                && File.ReadAllText(AyarDosyasi, Encoding.UTF8) == yeni);

            // Uygulama listeyi yerinde değiştiriyor (Add, Remove, Clear): kayıt yine yansıtmalı.
            var liste = (List<string>)Oku(Google(depo2), "SelectedCalendarIds")!;
            liste.Remove("ortak@group.calendar.google.com");
            liste.Add("yeni@ornek.com");
            Kaydet(depo2);
            Denetle("yerinde değişen takvim listesi kaydedildi",
                ((List<string>)Oku(Google(YeniDepo()), "SelectedCalendarIds")!).SequenceEqual(new[] { "ben@ornek.com", "yeni@ornek.com" }));

            string ilk = File.ReadAllText(AyarDosyasi, Encoding.UTF8);
            Kaydet(depo2);
            Denetle("liste değişmeden kaydedilince dosya aynı kalıyor", File.ReadAllText(AyarDosyasi, Encoding.UTF8) == ilk);

            ((List<string>)Oku(Google(depo2), "SelectedCalendarIds")!).Clear();
            Yaz(Google(depo2), "AccountEmail", null);
            Kaydet(depo2);
            JsonElement gj = GoogleJson();
            Denetle("liste boşalınca ve e-posta silinince alanlar dosyadan kalktı",
                !gj.TryGetProperty("SelectedCalendarIdsSifreli", out _) && !gj.TryGetProperty("AccountEmailSifreli", out _)
                && ((List<string>)Oku(Google(YeniDepo()), "SelectedCalendarIds")!).Count == 0);

            // Başka hesaptan kopyalanmış (çözülemeyen) değerler.
            string bozuk = """
                { "Google": { "ClientIdSifreli": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAbozukbozukbozukbozuk==",
                  "AccountEmailSifreli": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAbozukbozukbozukbozuk==",
                  "SelectedCalendarIdsSifreli": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAAbozukbozukbozukbozuk==",
                  "PollIntervalSeconds": 300 }, "StartWithWindows": false }
                """;
            File.WriteAllText(AyarDosyasi, bozuk, new UTF8Encoding(false));
            object? depo3 = null;
            Exception? hata = null;
            try
            {
                depo3 = YeniDepo();
            }
            catch (Exception ex)
            {
                hata = ex;
            }

            Denetle("çözülemeyen değerler hata fırlatmıyor, boş geliyor (takvimlerin hepsi gösterilir)",
                hata is null && depo3 is not null && Oku(Google(depo3), "ClientId") is null
                && Oku(Google(depo3), "AccountEmail") is null
                && ((List<string>)Oku(Google(depo3), "SelectedCalendarIds")!).Count == 0
                && (int)Oku(Google(depo3), "PollIntervalSeconds")! == 300,
                hata?.GetBaseException().Message ?? "");
        }

        // ------------------------------------------------------------------
        Console.WriteLine("4b) 1.16'ya dönülüp yeniden 1.17'ye geçilince");
        {
            string Sifreli(string duz) => (string)Tip("Kalender.Settings.GizliDeger").GetMethod("Sifrele", Hepsi)!
                .Invoke(null, new object?[] { duz, "deneme" })!;

            // 1.16 şifreli alanları tanımaz, dosyanın sonuna olduğu gibi geri yazar; kendi
            // bildiklerini düz yazar. (a) 1.16'da başka kimlik girilip başka hesaba bağlanıldı.
            string dosya = $$"""
                { "Google": { "ClientId": "kimlik-B", "SelectedCalendarIds": [], "AccountEmail": "b@ornek.com",
                  "ClientIdSifreli": "{{Sifreli("kimlik-A")}}", "AccountEmailSifreli": "{{Sifreli("a@ornek.com")}}",
                  "SelectedCalendarIdsSifreli": "{{Sifreli("[\"a@ornek.com\"]")}}" }, "StartWithWindows": false }
                """;
            File.WriteAllText(AyarDosyasi, dosya, new UTF8Encoding(false));
            object g = Google(YeniDepo());
            Denetle("1.16'da girilen kimlik ve hesap kazanıyor, eski hesabın seçimi düşüyor",
                (string?)Oku(g, "ClientId") == "kimlik-B" && (string?)Oku(g, "AccountEmail") == "b@ornek.com"
                && ((List<string>)Oku(g, "SelectedCalendarIds")!).Count == 0,
                $"{Oku(g, "ClientId")} {Oku(g, "AccountEmail")} [{string.Join(", ", (List<string>)Oku(g, "SelectedCalendarIds")!)}]");
            Denetle("ve dosyada yine düz metin yok", !File.ReadAllText(AyarDosyasi, Encoding.UTF8).Contains("ornek.com", StringComparison.Ordinal)
                && !File.ReadAllText(AyarDosyasi, Encoding.UTF8).Contains("kimlik-", StringComparison.Ordinal));
            Denetle("yeniden açılınca aynı (eski değerler geri gelmiyor)",
                (string?)Oku(Google(YeniDepo()), "ClientId") == "kimlik-B"
                && (string?)Oku(Google(YeniDepo()), "AccountEmail") == "b@ornek.com");

            // (b) 1.16'da hiçbir şeye dokunulmadı: 1.16 yalnız boş seçimi düz yazdı.
            dosya = $$"""
                { "Google": { "SelectedCalendarIds": [],
                  "ClientIdSifreli": "{{Sifreli("kimlik-A")}}", "AccountEmailSifreli": "{{Sifreli("a@ornek.com")}}",
                  "SelectedCalendarIdsSifreli": "{{Sifreli("[\"a@ornek.com\"]")}}" }, "StartWithWindows": false }
                """;
            File.WriteAllText(AyarDosyasi, dosya, new UTF8Encoding(false));
            g = Google(YeniDepo());
            Denetle("dokunulmadan geri gelinince kimlik, hesap ve seçim korunuyor",
                (string?)Oku(g, "ClientId") == "kimlik-A" && (string?)Oku(g, "AccountEmail") == "a@ornek.com"
                && ((List<string>)Oku(g, "SelectedCalendarIds")!).SequenceEqual(new[] { "a@ornek.com" }));

            // (c) Aynı hesapla 1.16'da seçim değiştirildi: düz seçim kazanıyor.
            dosya = $$"""
                { "Google": { "SelectedCalendarIds": [ "a@ornek.com", "ek@group.calendar.google.com" ], "AccountEmail": "a@ornek.com",
                  "ClientIdSifreli": "{{Sifreli("kimlik-A")}}", "AccountEmailSifreli": "{{Sifreli("a@ornek.com")}}",
                  "SelectedCalendarIdsSifreli": "{{Sifreli("[\"a@ornek.com\"]")}}" }, "StartWithWindows": false }
                """;
            File.WriteAllText(AyarDosyasi, dosya, new UTF8Encoding(false));
            g = Google(YeniDepo());
            Denetle("1.16'da değiştirilen seçim kazanıyor",
                ((List<string>)Oku(g, "SelectedCalendarIds")!).SequenceEqual(new[] { "a@ornek.com", "ek@group.calendar.google.com" })
                && (string?)Oku(g, "ClientId") == "kimlik-A");
        }

        // ------------------------------------------------------------------
        Console.WriteLine("4c) Şifreleme yapılamazsa (DPAPI hatası) değer kaybolmuyor");
        {
            object depo = YeniDepo();
            object g = Google(depo);
            Yaz(g, "ClientId", "kimlik-C");
            Yaz(g, "AccountEmail", "c@ornek.com");
            var liste = (List<string>)Oku(g, "SelectedCalendarIds")!;
            liste.Clear();
            liste.Add("c@ornek.com");

            // DPAPI'nin hata verdiği durum: şifreli hâller boş (Sifrele null döndürmüş gibi).
            FieldInfo Alan(string ad) => g.GetType().GetField(ad, Hepsi)!;
            Alan("_clientIdSifreli").SetValue(g, null);
            Alan("_accountEmailSifreli").SetValue(g, null);
            Kaydet(depo);
            // Takvim listesi kayıt anında şifrelenir; o yoldaki hata burada taklit edilmiyor.
            JsonElement gj = GoogleJson();
            Denetle("şifrelenemeyen kimlik ve e-posta 1.16'daki gibi düz yazıldı (kaybolmadı)",
                gj.TryGetProperty("ClientId", out JsonElement k) && k.GetString() == "kimlik-C"
                && gj.TryGetProperty("AccountEmail", out JsonElement e) && e.GetString() == "c@ornek.com"
                && !gj.TryGetProperty("ClientIdSifreli", out _));

            object g2 = Google(YeniDepo());
            JsonElement gj2 = GoogleJson();
            Denetle("sonraki açılışta şifrelenip düz metin kalkıyor",
                (string?)Oku(g2, "ClientId") == "kimlik-C" && (string?)Oku(g2, "AccountEmail") == "c@ornek.com"
                && gj2.TryGetProperty("ClientIdSifreli", out _) && !gj2.TryGetProperty("ClientId", out _)
                && !gj2.TryGetProperty("AccountEmail", out _));
        }

        // ------------------------------------------------------------------
        Console.WriteLine("5) Ayarlar penceresindeki kutu");
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
