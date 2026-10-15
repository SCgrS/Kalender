using System.Collections;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DBitmap = System.Drawing.Bitmap;
using DColor = System.Drawing.Color;
using DGraphics = System.Drawing.Graphics;
using DPointF = System.Drawing.PointF;
using DRectangle = System.Drawing.Rectangle;
using DRectangleF = System.Drawing.RectangleF;
using DSize = System.Drawing.Size;
using DSizeF = System.Drawing.SizeF;
using Ifade = System.Linq.Expressions.Expression;

// Kalender'de ikinci genel kod incelemesinin (2026-10-15) düzeltmelerinin
// sınaması (yansımayla). Madde numaraları kalender.md'deki listeden:
//   1. Ramazan ve Kurban bayramları, tablonun kapsadığı yıllarda (2026–2035)
//      Diyanet'in tarihleriyle: 2027–2029 Ramazan Bayramı ve arifeleri doğru
//      günde, Google'ın fazladan günü tatil değil. Akıştaki bayram kayıtları
//      (bayrama eklenen idari izin günleri dâhil) atılır; sabit tatiller hiç
//      atılmaz; akışın kapsamadığı yıllara bayram eklenmez. 1.13'ün ön belleği
//      yeniden indirilmeden düzelir; yeni dosyanın biçimi aynı, 1.13 okuyabilir.
//   2. İmlecin altındaki liste ancak kökü masaüstüyse (Progman ya da WorkerW)
//      masaüstü sayılır: takvimin üstünde duran başka bir programın listesi
//      (düz ya da SHELLDLL_DefView içinde) sayılmaz.
//   3. Duvar kâğıdı çizilirken fare kancası sökülü: ay değişiminde olduğu gibi
//      veri değişince (Google, tatil) ve gün dönünce de. Çizim sürerken araya
//      giren yoklama kancayı takmaz. Kanca yuttuğu sağ basışın bırakışını
//      bekliyorsa çizim bırakıştan sonraya kalır (bırakış kabuğa gitmez, menü
//      açılır); bu aya dönüş süresi de o sırada dolarsa ay değişmez.
//   4. Tepsi menüsü açıkken takvime sağ tık ya da ok tıklaması, takvimin menüsü
//      açıkkenki gibi: açık menü kapatılır; kapanınca takvimin menüsü tıklanan
//      noktada açılır ya da ay değişir. Menü açıkken fare kancası takılı kalır.
//   7. Duvar kâğıdından kaldırılırken önceki duvar kâğıdı geri konamayan
//      ekrana takvimsiz zemin yazılır; takvimin durduğu ekranlar bu sürecin ne
//      yazdığına değil ekrana bakılarak bulunur (açılışta eski sürümden donmuş
//      kalan takvim de). Takvimsiz zeminin adı ekli: yeniden yazılmaz, her
//      açılışta boşuna yazım olmaz. Windows'ta düz renk seçiliyse ekranda
//      takvim yok sayılır.
//   8. "Takvimi taşı" ekranı takvimi masaüstündeki çizimle aynı sınırda tutar:
//      ekran kenarından 12 × ölçek piksel içeride (sürüklerken, yapışırken ve
//      boyut değişince). Kaydedilen yer yeniden açılınca ve masaüstünde aynı;
//      kenar yakınındaki yapışma çizgileri (ekranın %6'sı) eskisi gibi.
//   9. "Takvimi taşı" ekranında fotoğraf zemininde takvimin arkasındaki
//      karartma çizilir: masaüstündekiyle aynı boyut ve renk, takvimi ortalar,
//      takvimle birlikte taşınır, boyutla büyür. Düz renk zeminde ve %0'da yok.
//  10. Düz zeminde yazı rengi elle seçilmiş olsa da zeminin açık mı koyu mu
//      olduğu zeminden çıkarılır: koyu zeminde orta tonlu (yeşil, mavi,
//      turuncu) yazıyla bugünün dairesi zeminden açık, tatil koyu zeminin
//      kırmızısı. Fotoğrafta yazıdan çıkarım sürer, ama 11'deki eşikle.
//  11. Açık/koyu kararı siyah ve beyaz yazının karşıtlığına göre (eşik bağıl
//      parlaklık ~0,179, önceden 0,42): orta tonlu zeminde koyu yazı; koyu
//      zeminde soluk günler zemine doğru karıştırılır (zemin koyu griden
//      açıksa). Hazır zemin renkleri ve hazır fotoğraflar eski hesabın
//      kopyasıyla karşılaştırılır: renkleri birebir aynı.
//  12. Kapla ve Sığdır'da fotoğrafın kenar sırası fotoğrafın renginde; zemin
//      rengi kenardan sızmaz.
//  13. Fotoğraf yalnız bazı ekranlardaysa ayarlar penceresindeki renkler
//      (yazı, hafta günleri, tatil, soluk gün, bugünün dairesi) fotoğrafsız
//      ekranda çizilenler; "Otomatik" kapatılınca orada görünüm aynı kalır,
//      bugünün rakamı iki ekranda da okunur. Renk çarkından seçilen yazı rengi
//      fotoğrafsız ekranda da kullanılır. Tam fotoğraf ve düz renk kipinde
//      pencerenin renkleri eskisi gibi.
//  14. Ekrana ayrı fotoğraf varken Arka plan sayfasında hazır renk ya da
//      kendi rengi seçilince o ekran fotoğrafını korur, öbürü yeni rengi alır;
//      genel fotoğraf varsa bırakılır. Seçili hazır renk işaretli; Boyut ve
//      Konum sayfası çizilen fotoğrafı gösterir.
//  15. Düz renge geçerken genel fotoğrafın yolu silinir ("Seçili fotoğraf"
//      satırı boşalır); sonra bir ekrana fotoğraf verilince öbür ekran düz
//      renkte kalır, fotoğraf kaldırılınca düz renge dönülür. Eski sürümün düz
//      renkte bıraktığı yol gösterilmez, ekrana fotoğraf verilince silinir.
//  16. Renk çarkından zemin rengi seçilince hazır fotoğrafın işareti kalkar.
//  17. Ayarlarda takvim ya da görev listesi anahtarı değişince bellekteki
//      aylar kalır, tam yenileme zamanları silinir: o aya geçilince ay baştan
//      alınır, yeni açılan takvimin ve listenin kayıtları gelir. Seçim
//      değişmedikçe bellekteki ay eskisi gibi artımlı tazelenir. Kapatılan
//      takvimin ve listenin kayıtları bellekten gelen ayda (çevrim dışıyken de)
//      çizilmez. Eşitleme sürerken seçim değişirse o ayın sıradaki eşitlemesi
//      tam. Ayarlar penceresi anahtar değişince önce bunu, sonra zorlanmış
//      yenilemeyi ister.
//  18. "Yeniden bağlan"da yeni yetki kaydına önceki hesabın e-postası yazılmaz.
//      Bağlantı yeniden kurulunca ay belleği ve yerel kopya silinir. Yeni
//      hesabın e-postası öncekinden farklıysa takvim ve görev listesi seçimi
//      "hepsi"ne döner ve yeniden eşitlenir; aynı hesapsa seçim korunur.
//      Ayarlar penceresindeki anahtarlar, satırlar aynı kalsa da seçime eşitlenir.
//  19. Bağlıyken istemci kimliği değiştirilip yeniden bağlanılmazsa erişim
//      anahtarı yenilenirken bağlantı kesilir, ama izni düşen bağlantıdaki gibi
//      haber verilir (YetkiDustu, günlükte hata). Ayarlar penceresinin durum
//      satırı kimlik değişir değişmez "Yeniden bağlan"a basılmasını söyler.
//  20. Tablonun dışındaki yıllarda İngilizce ya da yanlış yazılmış bayram
//      adları Türkçe ("Kurban Bayrami Day 2" → "Kurban Bayramı 2. gün",
//      "Sacrifice Feast Holiday" → "Kurban Bayramı"); "(kesin değil)" eki kalır.
//  21. Aynı güne düşen iki tatil de saklanır ve gün kutusunda ayrı satırlarda
//      görünür (tam gün kırmızı, arife olağan renkte); gün, tam gün bir tatil
//      varsa kırmızı.
//  22. Kurulum ve kaldırma hatasında .NET'in İngilizce iletisi yerine hata
//      türüne göre sade Türkçe ileti: dosya kullanımda (kapatıp yeniden
//      deneyin), yazma izni yok, disk dolu, öbürleri için genel ileti. Ayrıntı
//      yine günlüğe yazılır.
//  23. Güncellemelerden kalan eski açılım klasörleri (%TEMP%\.net) silinir:
//      bu sürecin kendi klasörü korunur; kullanımdaki (yüklü kütüphanesi ya da
//      açık dosyası olan) klasör hiç silinmez; Kalender Lite'ın ve başka
//      uygulamaların klasörlerine dokunulmaz. Yalnız kurulu kopyada, ilk
//      güncelleme denetimiyle birlikte arka planda.
//  24. Kurulum penceresi Ayarlar'dan yalnız açılışta başlamanın
//      kapatılabileceğini söyler (kısayol için ayar yok).
//  25. Ayarlar penceresi yalnız gizli anahtarın şifreli saklandığını söyler.
//  26. "Client ID nasıl alınır?" 5. adımı README'deki gibi iki kutuya da depo
//      adresini yazdırır (kaldırılan README bölümüne bağlantı yok).
//  28. Güncelleme denetimi sürerken indirme başlarsa denetim bitince durum ve
//      bulunan sürüm değişmez; indirme sürerken ikinci tıklama ikinci bir
//      indirme başlatmaz.
//  29. Duvar kâğıdı yedeği geçici dosyaya yazılıp diske boşaltılır, sonra
//      yerine taşınır. Okunamayan (boş, yarım, bozuk) yedek "bozuk-<tarih>"
//      adıyla kenara alınır: geri yükleme yedek yokmuş gibi davranır, yazma
//      açılınca taze yedek alınır.
//  30. Tam ekran ya da sunum kipi takvimi yalnız ön plandaki pencere imlecin
//      ekranındaysa susturur; hiçbir ekranda durmayan pencere ya da ön planda
//      pencere olmaması susturmaz.
//  31. Ayarlar penceresi açıkken ekran düzeni değişince istemci kimliği ve gizli
//      anahtar kutularına yazılıp kaydedilmemiş değer silinmez; ayarlardan
//      yükleme (bağlanınca, bağlantıyı kesince) kutuları yine doldurur.
//  32. Ekranlarda yalnız takvimin görüntüsünün yolu görünse de Windows'ta
//      resim kipi kapalıysa (düz renk) yedek düz renk olarak alınır; Windows
//      yolu boş verince ya da resim kipi açıkken eskisi gibi.
//  33. "Masaüstü simgelerini göster" kapalıyken (liste gizli) gizli simgenin
//      yeri simge sayılmaz; tıklama takvime gider. Windows'un erişilebilirlik
//      arayüzü gizli listedeki simgeye de isabet veriyor (ölçülür).
//
// 2, 30 ve 33. maddelerin pencereleri görünmeyen ayrı bir Windows masaüstünde
// kurulur: kullanıcının ekranında görünmez, gerçek masaüstüne bakılmaz. 3 ve 4.
// maddelerde fare kancası takılmaz, gerçek fareye dokunulmaz, menü açılmaz
// (KodIncelemesi'ndeki gibi); katmanın ekranı yok, duvar kâğıdına yazılmaz.
// 8 ve 9. maddelerde "Takvimi taşı" penceresi kurulur ama gösterilmez (ekrana
// çıkmaz), ekranı hayalidir; 9'un fotoğrafı geçici klasörde üretilip silinir.
// 13–16. maddelerde ayarlar penceresi kurucusu çalıştırılmadan kurulur
// (KodIncelemesi'ndeki gibi): ekranda açılmaz, adlı denetimleri boş kurulur,
// uygulamaya açılan kapısı sahtedir (duvar kâğıdına yazılmaz, ayar
// kaydedilmez), iki ekranı hayalidir. Dosya seçme penceresi açılmaz; ekrana
// fotoğraf verme, seçimden sonraki adımlarıyla çağrılır. Deneme fotoğrafları
// geçici klasörde üretilip silinir.
// 17, 18 ve 19. maddelerde Google yerine bu programdaki sahtesi yanıt verir
// (GoogleEsitleme'deki gibi), ağa çıkılmaz, gerçek hesaba dokunulmaz. Bu
// maddelerde ve 31. maddede ayarlar penceresi 13–16. maddelerdeki gibi
// kurulur (ekranda açılmaz). 18. maddede tarayıcı açılmaz:
// onayı bu program yerel dinleyiciye (127.0.0.1) gönderir; uygulama kurucusu
// çalıştırılmadan kurulur (tepsisi, masaüstü katmanı yok, duvar kâğıdına
// yazılmaz) ve bağlanma yolu bu programın dağıtıcısında çalışır. Yetki dosyası,
// Google'ın yerel kopyası ve günlük geçici klasöre yazılır. 25 ve 26. maddelerde
// pencerenin derlenmiş metnine ve README'ye bakılır.
//
// Google'ın tatil akışı yerine bu programdaki sahtesi kullanılır, ağa çıkılmaz;
// GitHub yerine de bu programın açtığı yerel sunucuya sorulur. Uygulamanın
// klasörü (Paths.RootFolder) %TEMP% altında geçici bir klasöre çevrilir:
// kurulu Kalender'in ayarlarına ve tatil ön belleğine dokunulmaz (sonda
// denetlenir). 22, 23 ve 28. maddelerde %TEMP% de (TMP, TEMP) geçici bir
// klasöre çevrilir: açılım klasörleri ve güncelleme indirmeleri oraya gider,
// gerçek Temp'teki .net ve indirme klasörlerine dokunulmaz (denetlenir). Gün
// kutusu ve kurulum pencereleri kurulur ama gösterilmez. Kurucu ve kurulu
// uygulama başlatılmaz; kurulum ve kaldırma çağrılmaz (çalışan Kalender'i
// kapatırdı), sırası derlenmiş koddan okunur. 7, 29 ve 32. maddelerde
// Windows'un duvar kâğıdı arayüzü yerine KodIncelemesi'ndeki gibi sahtesi
// kullanılır; Windows'un duvar kâğıdı ayarı (SPI_GETDESKWALLPAPER) bir kez
// yalnız okunur, öbür denetimlerde bu program verir. Duvar kâğıdına yazan
// geri yükleme çağrılmaz; yalnız yedeksiz dalı çağrılır, orada gerçek ekran
// yalnız okunur. Geçici klasörler sonunda silinir.
// Çıkış kodu 0 = hepsi geçti.
// Kullanım: dotnet run -c Release
internal static class Program
{
    private const BindingFlags Hepsi =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static readonly Assembly Uyg = typeof(Kalender.App).Assembly;
    private static int _hata;
    private static int _gecen;

    private static Type Tip(string ad) => Uyg.GetType(ad, throwOnError: true)!;

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

    // ------------------------------------------------------ yansıma yardımcıları

    private static object? Oku(object o, string ad) => o.GetType().GetProperty(ad, Hepsi)!.GetValue(o);

    private static object AlanOku(object o, string ad) => o.GetType().GetField(ad, Hepsi)!.GetValue(o)!;

    private static void AlanAta(object o, string ad, object? deger) => o.GetType().GetField(ad, Hepsi)!.SetValue(o, deger);

    private static object? Cagir(object o, string ad, params object?[] a) => o.GetType().GetMethod(ad, Hepsi)!.Invoke(o, a);

    private static object? Statik(Type t, string ad, params object?[] a) => t.GetMethod(ad, Hepsi)!.Invoke(null, a);

    private static string? DosyaOku(string yol) => File.Exists(yol) ? Convert.ToHexString(File.ReadAllBytes(yol)) : null;

    /// <summary>Bir bölümü çalıştırır; beklenmeyen hata öbür bölümleri durdurmaz.</summary>
    private static void Bolum(Action bolum)
    {
        try
        {
            bolum();
        }
        catch (Exception ex)
        {
            _hata++;
            Console.WriteLine("   BEKLENMEYEN HATA: " + ex);
        }
    }

    [STAThread]
    private static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        PropertyInfo kok = Tip("Kalender.Core.Paths").GetProperty("RootFolder", Hepsi)!;
        string gercekKok = (string)kok.GetValue(null)!;
        string ayarDosyasi = Path.Combine(gercekKok, "ayarlar.json");
        string tatilDosyasi = Path.Combine(gercekKok, "cache", "tatiller.json");
        string? ayarOnce = DosyaOku(ayarDosyasi);
        string? tatilOnce = DosyaOku(tatilDosyasi);

        string gecici = Path.Combine(Path.GetTempPath(), "Kalender İkinci İnceleme ğüşıöç " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gecici);
        kok.SetValue(null, gecici);

        try
        {
            Bolum(Madde1);
            Bolum(Madde20);
            Bolum(Madde21);
            Bolum(Madde22);
            Bolum(Madde23);
            Bolum(Madde24);
            Bolum(Madde28);
            Bolum(Madde7);
            Bolum(Madde29);
            Bolum(Madde32);
            Bolum(Madde8);
            Bolum(Madde9);
            Bolum(Madde10);
            Bolum(Madde11);
            Bolum(Madde12);
            Bolum(Madde13);
            Bolum(Madde14);
            Bolum(Madde15);
            Bolum(Madde16);
            Bolum(Madde2);
            Bolum(Madde3);
            Bolum(Madde4);
            Bolum(Madde30);
            Bolum(Madde33);
            Bolum(Madde17);
            Bolum(Madde18);
            Bolum(Madde19);
            Bolum(Madde25);
            Bolum(Madde26);
            Bolum(Madde31);
        }
        finally
        {
            kok.SetValue(null, gercekKok);
            try
            {
                Directory.Delete(gecici, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   (deneme klasörü silinemedi: " + ex.Message + ")");
            }
        }

        Denetle("kurulu Kalender'in ayar dosyasına ve tatil ön belleğine dokunulmadı",
            DosyaOku(ayarDosyasi) == ayarOnce && DosyaOku(tatilDosyasi) == tatilOnce);

        Console.WriteLine();
        Console.WriteLine($"Sonuç: {_gecen} geçti, {_hata} kaldı.");
        return _hata == 0 ? 0 : 1;
    }

    // ------------------------------------------------- tatiller (1, 20, 21. maddeler)

    /// <summary>
    /// Google'ın tatil akışının sahtesi: gövdeyi ya da "değişmedi" (304)
    /// yanıtını bu program verir, ağa çıkılmaz. İsteklerin koşul başlıkları
    /// kaydedilir (KodIncelemesi'nin 18. maddesindeki gibi).
    /// </summary>
    private sealed class SahteTatilAkisi : HttpMessageHandler
    {
        public string Govde = string.Empty;
        public readonly List<string> Kosullar = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken iptal)
        {
            Kosullar.Add(string.Join(";", istek.Headers
                .Where(b => b.Key is "If-None-Match" or "If-Modified-Since")
                .Select(b => b.Key + "=" + string.Join(",", b.Value))));

            var yanit = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Govde, Encoding.UTF8, "text/calendar"),
            };
            yanit.Headers.TryAddWithoutValidation("ETag", "\"yeni\"");
            return Task.FromResult(yanit);
        }
    }

    /// <summary>1.13'ün tatil ön belleğinin biçimi (yazmak ve yeni dosyayı 1.13 gibi okumak için).</summary>
    private sealed class EskiOnbellek
    {
        public DateTimeOffset FetchedUtc { get; set; }

        public string? ETag { get; set; }

        public string? LastModified { get; set; }

        public bool FullFeed { get; set; }

        public List<EskiTatil> Days { get; set; } = new();
    }

    private sealed class EskiTatil
    {
        public DateOnly Date { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = string.Empty;

        public bool DateUncertain { get; set; }
    }

    // Açıklamalar Google'ın akışındaki gibi (ICS kaçışlarıyla).
    private const string Tam = "Resmi tatil";
    private const string Yarim = @"Resmi tatil\nBu\, yarım günlük bir tatildir.";
    private const string TamBelirsiz = @"Resmi tatil\nTarih\, geçicidir ve değişebilir.";
    private const string YarimBelirsiz = @"Resmi tatil\nBu\, yarım günlük bir tatildir.\nTarih\, geçicidir ve değişebilir.";

    /// <summary>Tüm gün kayıtlardan bir ICS metni (bitiş günü dâhil değil).</summary>
    private static string TatilAkisi(params (string Bas, string Son, string Ad, string Aciklama)[] kayitlar)
    {
        var sb = new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n");
        foreach ((string bas, string son, string ad, string aciklama) in kayitlar)
        {
            sb.Append("BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:").Append(bas)
                .Append("\r\nDTEND;VALUE=DATE:").Append(son)
                .Append("\r\nSUMMARY:").Append(ad)
                .Append("\r\nDESCRIPTION:").Append(aciklama)
                .Append("\r\nEND:VEVENT\r\n");
        }

        return sb.Append("END:VCALENDAR\r\n").ToString();
    }

    /// <summary>
    /// Google'ın Türkiye akışının 2026-10'daki kayıtlarından bir seçki, adları
    /// ve tarihleriyle: 2027–2029 Ramazan Bayramı bir gün geç ve "(kesin
    /// değil)" ekli, Kurban Bayramı'nın 2.–4. günleri "Kurban Bayrami Day N",
    /// aynı güne iki tatil düşen günler (23 Nisan 2023, 19 Mayıs 2027, 23 Nisan
    /// 2029). 2036 tablonun dışında bir yıl (uydurma kayıt).
    /// </summary>
    private static string GoogleAkisi() => TatilAkisi(
        // 2023 (tablonun dışında)
        ("20230420", "20230421", "Ramazan Bayramı Arifesi (yarım gün)", Yarim),
        ("20230421", "20230424", "Ramazan Bayramı", Tam),
        ("20230423", "20230424", "Ulusal Egemenlik ve Çocuk Bayramı", Tam),
        ("20230626", "20230627", "Kurban Bayrami", Tam),
        ("20230627", "20230628", "Kurban Bayramı Arifesi (yarım gün)", Yarim),
        ("20230628", "20230629", "Kurban Bayramı", Tam),
        ("20230629", "20230630", "Kurban Bayrami Day 2", Tam),
        ("20230630", "20230701", "Kurban Bayrami Day 3", Tam),
        ("20230701", "20230702", "Kurban Bayrami Day 4", Tam),
        // 2024–2025 (tablonun dışında)
        ("20240620", "20240621", "Sacrifice Feast Holiday", Tam),
        ("20240621", "20240622", "Eid al Adha Public Sector Holiday", Tam),
        ("20250607", "20250608", "Kurban Bayrami Day 2", Tam),
        // 2026
        ("20260525", "20260526", "Sacrifice Feast Holiday", Tam),
        ("20260526", "20260527", "Kurban Bayramı Arifesi (yarım gün)", Yarim),
        ("20260527", "20260528", "Kurban Bayramı", Tam),
        ("20260528", "20260529", "Kurban Bayrami Day 2", Tam),
        ("20261028", "20261029", "Cumhuriyet Bayramı Arifesi (yarım gün)", Yarim),
        ("20261029", "20261030", "Cumhuriyet Bayramı", Tam),
        // 2027
        ("20270101", "20270102", "Yılbaşı", Tam),
        ("20270309", "20270310", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20270310", "20270313", "Ramazan Bayramı (kesin değil)", TamBelirsiz),
        ("20270423", "20270424", "Ulusal Egemenlik ve Çocuk Bayramı", Tam),
        ("20270501", "20270502", "Emek ve Dayanışma Günü", Tam),
        ("20270509", "20270510", "Anneler Günü", @"Kutlama\nKutlama günlerini gizlemek için Google Takvim Ayarları"),
        ("20270515", "20270516", "Kurban Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20270516", "20270517", "Kurban Bayramı (kesin değil)", TamBelirsiz),
        ("20270517", "20270518", "Kurban Bayrami Day 2 (kesin değil)", TamBelirsiz),
        ("20270518", "20270519", "Kurban Bayrami Day 3 (kesin değil)", TamBelirsiz),
        ("20270519", "20270520", "Atatürk’ü Anma, Gençlik ve Spor Bayramı", Tam),
        ("20270519", "20270520", "Kurban Bayrami Day 4 (kesin değil)", TamBelirsiz),
        ("20270715", "20270716", "Demokrasi ve Millî Birlik Günü", Tam),
        ("20270830", "20270831", "Zafer Bayramı", Tam),
        ("20271028", "20271029", "Cumhuriyet Bayramı Arifesi (yarım gün)", Yarim),
        ("20271029", "20271030", "Cumhuriyet Bayramı", Tam),
        // 2028
        ("20280226", "20280227", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20280227", "20280301", "Ramazan Bayramı (kesin değil)", TamBelirsiz),
        ("20280519", "20280520", "Atatürk’ü Anma, Gençlik ve Spor Bayramı", Tam),
        // 2029
        ("20290214", "20290215", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20290215", "20290218", "Ramazan Bayramı (kesin değil)", TamBelirsiz),
        ("20290423", "20290424", "Ulusal Egemenlik ve Çocuk Bayramı", Tam),
        ("20290423", "20290424", "Kurban Bayramı Arifesi (yarım gün) (kesin değil)", YarimBelirsiz),
        ("20290424", "20290425", "Kurban Bayramı (kesin değil)", TamBelirsiz),
        ("20290425", "20290426", "Kurban Bayrami Day 2 (kesin değil)", TamBelirsiz),
        // 2036 (tablonun dışında)
        ("20360101", "20360102", "Yılbaşı", Tam),
        ("20360208", "20360209", "Kurban Bayrami Day 2 (kesin değil)", TamBelirsiz));

    private static string OnbellekYolu
        => Path.Combine((string)Tip("Kalender.Core.Paths").GetProperty("CacheFolder", Hepsi)!.GetValue(null)!, "tatiller.json");

    private static void OnbellegiSil()
    {
        if (File.Exists(OnbellekYolu))
        {
            File.Delete(OnbellekYolu);
        }
    }

    /// <summary>Ağa sahte akıştan çıkan bir tatil servisi.</summary>
    private static object TatilServisi(SahteTatilAkisi sahte)
    {
        object servis = Activator.CreateInstance(Tip("Kalender.Calendar.Holidays.HolidayService"), true)!;
        ((IDisposable)AlanOku(servis, "_http")).Dispose();
        AlanAta(servis, "_http", new HttpClient(sahte));
        return servis;
    }

    private static void Yenile(object servis, bool zorla)
        => ((Task)Cagir(servis, "RefreshAsync", zorla, CancellationToken.None)!).GetAwaiter().GetResult();

    /// <summary>Servisin o güne koyduğu tatiller: ad, yarım gün mü, tarihi kesin değil mi.</summary>
    private static List<(string Ad, bool Yarim, bool Belirsiz)> GunTatilleri(object servis, DateOnly gun)
    {
        var gunler = (IDictionary)Oku(servis, "Days")!;
        if (!gunler.Contains(gun))
        {
            return new();
        }

        return ((IEnumerable)gunler[gun]!).Cast<object>()
            .Select(t => ((string)Oku(t, "Name")!, Oku(t, "Kind")!.ToString() == "HalfDay", (bool)Oku(t, "DateUncertain")!))
            .ToList();
    }

    /// <summary>O günde tek tatil var, adı ve türü bu, tarihi kesin.</summary>
    private static bool Yalniz(object servis, DateOnly gun, string ad, bool yarim)
        => GunTatilleri(servis, gun) is [var t] && t.Ad == ad && t.Yarim == yarim && !t.Belirsiz;

    private static string Yazi(object servis, DateOnly gun)
        => $"{gun:yyyy-MM-dd}: " + string.Join(" | ", GunTatilleri(servis, gun)
            .Select(t => t.Ad + (t.Yarim ? " [yarım]" : "") + (t.Belirsiz ? " [belirsiz]" : "")));

    /// <summary>Takvimin o gün için gördüğü içerik (görünen ay o gün; tatil yoksa null).</summary>
    private static object? GunIcerigi(object servis, DateOnly gun)
    {
        object depo = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"), true)!;
        object veri = Activator.CreateInstance(Tip("Kalender.Calendar.CalendarDataService"), servis, null, depo)!;
        Cagir(veri, "SetViewMonth", new DateOnly(gun.Year, gun.Month, 1));
        return Cagir(Oku(veri, "Current")!, "For", gun);
    }

    private static bool Kirmizi(object? icerik) => icerik is not null && (bool)Oku(icerik, "IsHoliday")!;

    private static bool TablodakiBayramMi(string ad, DateOnly gun)
        => (bool)Statik(Tip("Kalender.Calendar.Holidays.DiniBayramlar"), "TablodakiBayramMi", ad, gun)!;

    private static string AdiDuzelt(string ad)
        => (string)Statik(Tip("Kalender.Calendar.Holidays.DiniBayramlar"), "AdiDuzelt", ad)!;

    // ------------------------------------------------------------------ 1. madde

    private static void Madde1()
    {
        Console.WriteLine("1) Ramazan ve Kurban bayramları Diyanet'in tarihleriyle; sabit tatiller korunuyor");
        OnbellegiSil();
        var sahte = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis = TatilServisi(sahte);
        try
        {
            Yenile(servis, true);
            Denetle("sınama düzeni: sahte akış indirildi", sahte.Kosullar.Count == 1 && (int)Oku(servis, "Count")! > 0);

            // Diyanet: 2027'de 9–11 Mart (arife 8 Mart), 2028'de 26–28 Şubat
            // (arife 25 Şubat), 2029'da 14–16 Şubat (arife 13 Şubat). Google bir gün geç.
            foreach (DateOnly ilk in new[] { new DateOnly(2027, 3, 9), new DateOnly(2028, 2, 26), new DateOnly(2029, 2, 14) })
            {
                DateOnly arife = ilk.AddDays(-1);
                DateOnly fazla = ilk.AddDays(3);
                Denetle($"{ilk.Year}: arife {arife:yyyy-MM-dd} yarım gün, Diyanet'e göre",
                    Yalniz(servis, arife, "Ramazan Bayramı Arifesi (yarım gün)", yarim: true), Yazi(servis, arife));
                Denetle($"{ilk.Year}: Ramazan Bayramı {ilk:yyyy-MM-dd} – {ilk.AddDays(2):yyyy-MM-dd} tam gün, eksiz",
                    Enumerable.Range(0, 3).All(i => Yalniz(servis, ilk.AddDays(i), "Ramazan Bayramı", yarim: false)),
                    string.Join(" ; ", Enumerable.Range(0, 3).Select(i => Yazi(servis, ilk.AddDays(i)))));
                Denetle($"{ilk.Year}: Google'ın fazladan günü ({fazla:yyyy-MM-dd}) tatil değil",
                    GunTatilleri(servis, fazla).Count == 0, Yazi(servis, fazla));
            }

            Denetle("2027 Kurban Bayramı da tablodan: arife 15 Mayıs, 16 Mayıs, 17 Mayıs 2. gün, 18 Mayıs 3. gün",
                Yalniz(servis, new DateOnly(2027, 5, 15), "Kurban Bayramı Arifesi (yarım gün)", yarim: true)
                && Yalniz(servis, new DateOnly(2027, 5, 16), "Kurban Bayramı", yarim: false)
                && Yalniz(servis, new DateOnly(2027, 5, 17), "Kurban Bayramı 2. gün", yarim: false)
                && Yalniz(servis, new DateOnly(2027, 5, 18), "Kurban Bayramı 3. gün", yarim: false),
                Yazi(servis, new DateOnly(2027, 5, 17)));

            var bayramlar = Enumerable.Range(0, 3 * 366).Select(i => new DateOnly(2027, 1, 1).AddDays(i))
                .Where(g => g.Year <= 2029)
                .SelectMany(g => GunTatilleri(servis, g))
                .Where(t => t.Ad.Contains("Ramazan") || t.Ad.Contains("Kurban"))
                .ToList();
            Denetle("tablodan gelenlerde \"(kesin değil)\" yok, tarihleri kesin",
                bayramlar.Count > 0 && bayramlar.All(t => !t.Ad.Contains("kesin değil") && !t.Belirsiz),
                bayramlar.Count + " bayram günü");

            // Sabit tatiller hep akıştan: bayramın içine düşenler de (19 Mayıs 2027, 23 Nisan 2029).
            var sabitler = new (DateOnly Gun, string Ad, bool Yarim)[]
            {
                (new(2027, 1, 1), "Yılbaşı", false),
                (new(2027, 4, 23), "Ulusal Egemenlik ve Çocuk Bayramı", false),
                (new(2027, 5, 1), "Emek ve Dayanışma Günü", false),
                (new(2027, 5, 19), "Atatürk’ü Anma, Gençlik ve Spor Bayramı", false),
                (new(2027, 7, 15), "Demokrasi ve Millî Birlik Günü", false),
                (new(2027, 8, 30), "Zafer Bayramı", false),
                (new(2027, 10, 28), "Cumhuriyet Bayramı Arifesi (yarım gün)", true),
                (new(2027, 10, 29), "Cumhuriyet Bayramı", false),
                (new(2028, 5, 19), "Atatürk’ü Anma, Gençlik ve Spor Bayramı", false),
                (new(2029, 4, 23), "Ulusal Egemenlik ve Çocuk Bayramı", false),
                (new(2026, 10, 28), "Cumhuriyet Bayramı Arifesi (yarım gün)", true),
            };
            var eksik = sabitler.Where(s => !GunTatilleri(servis, s.Gun).Contains((s.Ad, s.Yarim, false))).ToList();
            Denetle("sabit tatillerin hepsi adıyla ve türüyle yerinde", eksik.Count == 0,
                string.Join(" ; ", eksik.Select(s => Yazi(servis, s.Gun))));

            Denetle("tablonun yılında bayrama eklenen idari izin günü (25 Mayıs 2026) de tabloya bırakılıyor",
                GunTatilleri(servis, new DateOnly(2026, 5, 25)).Count == 0
                && Yalniz(servis, new DateOnly(2026, 5, 26), "Kurban Bayramı Arifesi (yarım gün)", yarim: true)
                && Yalniz(servis, new DateOnly(2026, 5, 30), "Kurban Bayramı 4. gün", yarim: false),
                Yazi(servis, new DateOnly(2026, 5, 25)));

            Denetle("tablonun dışındaki yılda (2023) bayram akıştan, akıştaki günlerinde",
                Yalniz(servis, new DateOnly(2023, 4, 20), "Ramazan Bayramı Arifesi (yarım gün)", yarim: true)
                && Yalniz(servis, new DateOnly(2023, 4, 21), "Ramazan Bayramı", yarim: false)
                && GunTatilleri(servis, new DateOnly(2023, 4, 23)).Contains(("Ramazan Bayramı", false, false))
                && Yalniz(servis, new DateOnly(2023, 6, 28), "Kurban Bayramı", yarim: false),
                Yazi(servis, new DateOnly(2023, 4, 23)));

            var gunler = (IDictionary)Oku(servis, "Days")!;
            Denetle("akışın kapsamadığı yıllara (2030, 2032) bayram eklenmiyor",
                !gunler.Contains(new DateOnly(2030, 2, 4)) && !gunler.Contains(new DateOnly(2030, 4, 13))
                && !gunler.Contains(new DateOnly(2032, 1, 14)));

            // Adla ve tarihle tanıma: sabit tatiller bayramın içine düşse de hiç atılmaz.
            var tanima = new (string Ad, DateOnly Gun, bool Beklenen)[]
            {
                ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", new(2027, 5, 19), false),
                ("Ulusal Egemenlik ve Çocuk Bayramı", new(2029, 4, 23), false),
                ("Cumhuriyet Bayramı Arifesi (yarım gün)", new(2027, 10, 28), false),
                ("Zafer Bayramı", new(2033, 3, 12), false),
                ("Yılbaşı", new(2033, 1, 1), false),
                ("Kurban Bayramı", new(2027, 8, 1), false),
                ("Ramazan Bayramı", new(2025, 3, 30), false),
                ("Ramazan Bayramı (kesin değil)", new(2027, 3, 12), true),
                ("RAMAZAN BAYRAMI", new(2027, 3, 10), true),
                ("Şeker Bayramı", new(2027, 3, 10), true),
                ("Eid al Fitr", new(2027, 3, 12), true),
                ("Kurban Bayrami Day 4 (kesin değil)", new(2027, 5, 19), true),
                ("Sacrifice Feast Holiday", new(2026, 5, 25), true),
                ("Eid al Adha Public Sector Holiday", new(2027, 5, 21), true),
            };
            var yanlis = tanima.Where(t => TablodakiBayramMi(t.Ad, t.Gun) != t.Beklenen).ToList();
            Denetle("akıştaki bayram kaydı adla ve tarihle tanınıyor, sabit tatiller hiç tanınmıyor", yanlis.Count == 0,
                string.Join(" ; ", yanlis.Select(t => $"{t.Ad} {t.Gun:yyyy-MM-dd}")));

            // Takvimde: 9 Mart 2027 Salı kırmızı, 12 Mart Cuma değil (süre hesabı).
            Denetle("takvimde 9 Mart 2027 kırmızı, 12 Mart 2027 değil",
                Kirmizi(GunIcerigi(servis, new DateOnly(2027, 3, 9))) && !Kirmizi(GunIcerigi(servis, new DateOnly(2027, 3, 12))));
            object? arifeIcerigi = GunIcerigi(servis, new DateOnly(2027, 3, 8));
            Denetle("8 Mart 2027 arife: kırmızı değil, gün kutusunda adı var",
                arifeIcerigi is not null && !Kirmizi(arifeIcerigi) && (bool)Oku(arifeIcerigi, "HasAnything")!);
            Denetle("29 Şubat 2028 kırmızı değil, 14 Şubat 2029 kırmızı",
                !Kirmizi(GunIcerigi(servis, new DateOnly(2028, 2, 29))) && Kirmizi(GunIcerigi(servis, new DateOnly(2029, 2, 14))));

            // Yeni ön bellek: biçimi 1.13'ünkiyle aynı, kayıtlar akıştaki gibi; 1.13 okuyabiliyor.
            string yazilan = File.ReadAllText(OnbellekYolu);
            using (JsonDocument belge = JsonDocument.Parse(yazilan))
            {
                var ustAlanlar = belge.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();
                var gunAlanlari = belge.RootElement.GetProperty("Days").EnumerateArray()
                    .SelectMany(g => g.EnumerateObject().Select(p => p.Name)).ToHashSet();
                Denetle("ön bellek dosyasının biçimi 1.13'ünkiyle aynı (yeni alan yok)",
                    ustAlanlar.IsSubsetOf(new[] { "FetchedUtc", "ETag", "LastModified", "FullFeed", "Days" })
                    && gunAlanlari.IsSubsetOf(new[] { "Date", "Name", "Kind", "DateUncertain" }),
                    string.Join(",", ustAlanlar) + " / " + string.Join(",", gunAlanlari));
            }

            EskiOnbellek? eski = JsonSerializer.Deserialize<EskiOnbellek>(yazilan);
            Denetle("dosyada akışın kendisi duruyor (düzeltme okurken yapılıyor)",
                eski is { FullFeed: true }
                && eski.Days.Any(d => d.Date == new DateOnly(2027, 3, 12) && d.Name == "Ramazan Bayramı (kesin değil)")
                && eski.Days.Any(d => d.Date == new DateOnly(2027, 5, 17) && d.Name == "Kurban Bayrami Day 2 (kesin değil)"));

            // 1.13 aynı güne düşen kayıtlardan sonuncuyu tutar: tam gün olan sonda, gün kırmızı kalır.
            EskiTatil? yirmiUcNisan = eski?.Days.LastOrDefault(d => d.Date == new DateOnly(2029, 4, 23));
            Denetle("1.13 yeni dosyayı okursa 23 Nisan 2029 yine tam gün",
                yirmiUcNisan is { Kind: "Full", Name: "Ulusal Egemenlik ve Çocuk Bayramı" }
                && eski!.Days.Count(d => d.Date == new DateOnly(2029, 4, 23)) == 2,
                yirmiUcNisan?.Name ?? "yok");
        }
        finally
        {
            ((IDisposable)servis).Dispose();
        }

        // 1.13'ün yazdığı ön bellek (Google'ın kaymış tarihleri, her güne tek kayıt):
        // düzeltme yeniden indirmeyi beklemiyor, dosyaya da dokunulmuyor.
        OnbellegiSil();
        Directory.CreateDirectory(Path.GetDirectoryName(OnbellekYolu)!);
        var onbellek113 = new EskiOnbellek
        {
            FetchedUtc = DateTimeOffset.UtcNow.AddDays(-3),
            ETag = "\"eski\"",
            FullFeed = true,
            Days =
            {
                new() { Date = new(2025, 6, 7), Name = "Kurban Bayrami Day 2", Kind = "Full" },
                new() { Date = new(2027, 1, 1), Name = "Yılbaşı", Kind = "Full" },
                new() { Date = new(2027, 3, 9), Name = "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", Kind = "HalfDay", DateUncertain = true },
                new() { Date = new(2027, 3, 10), Name = "Ramazan Bayramı (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 3, 11), Name = "Ramazan Bayramı (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 3, 12), Name = "Ramazan Bayramı (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 5, 18), Name = "Kurban Bayrami Day 3 (kesin değil)", Kind = "Full", DateUncertain = true },
                new() { Date = new(2027, 5, 19), Name = "Atatürk’ü Anma, Gençlik ve Spor Bayramı", Kind = "Full" },
            },
        };
        File.WriteAllText(OnbellekYolu, JsonSerializer.Serialize(onbellek113), new UTF8Encoding(false));
        string? dosyaOnce = DosyaOku(OnbellekYolu);

        var sahte2 = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis2 = TatilServisi(sahte2);
        try
        {
            Cagir(servis2, "LoadCache");
            Yenile(servis2, false);
            Denetle("sınama düzeni: 1.13'ün ön belleği 30 gün dolmadan yeniden indirilmiyor", sahte2.Kosullar.Count == 0,
                sahte2.Kosullar.Count + " istek");
            Denetle("1.13'ün ön belleğinden okununca 2027 Ramazan Bayramı Diyanet'e göre",
                Yalniz(servis2, new DateOnly(2027, 3, 8), "Ramazan Bayramı Arifesi (yarım gün)", yarim: true)
                && Enumerable.Range(9, 3).All(g => Yalniz(servis2, new DateOnly(2027, 3, g), "Ramazan Bayramı", yarim: false))
                && GunTatilleri(servis2, new DateOnly(2027, 3, 12)).Count == 0,
                Yazi(servis2, new DateOnly(2027, 3, 12)));
            Denetle("1.13'ün ön belleğinde düşmüş Kurban Bayramı'nın 4. günü de 19 Mayıs 2027'de",
                GunTatilleri(servis2, new DateOnly(2027, 5, 19)).Select(t => t.Ad)
                    .SequenceEqual(new[] { "Atatürk’ü Anma, Gençlik ve Spor Bayramı", "Kurban Bayramı 4. gün" }),
                Yazi(servis2, new DateOnly(2027, 5, 19)));
            Denetle("1.13'ün ön belleğindeki tablo dışı ad da Türkçe",
                Yalniz(servis2, new DateOnly(2025, 6, 7), "Kurban Bayramı 2. gün", yarim: false), Yazi(servis2, new DateOnly(2025, 6, 7)));
            Denetle("1.13'ün ön bellek dosyasına dokunulmadı", DosyaOku(OnbellekYolu) == dosyaOnce);
        }
        finally
        {
            ((IDisposable)servis2).Dispose();
        }
    }

    // ----------------------------------------------------------------- 20. madde

    private static void Madde20()
    {
        Console.WriteLine("20) Tablonun dışındaki yıllarda bayram adları Türkçe; \"(kesin değil)\" eki kalıyor");
        OnbellegiSil();
        var sahte = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis = TatilServisi(sahte);
        try
        {
            Yenile(servis, true);

            var beklenen = new (DateOnly Gun, string Ad)[]
            {
                (new(2023, 6, 26), "Kurban Bayramı"),
                (new(2023, 6, 29), "Kurban Bayramı 2. gün"),
                (new(2023, 6, 30), "Kurban Bayramı 3. gün"),
                (new(2023, 7, 1), "Kurban Bayramı 4. gün"),
                (new(2024, 6, 20), "Kurban Bayramı"),
                (new(2024, 6, 21), "Kurban Bayramı (idari izin)"),
                (new(2025, 6, 7), "Kurban Bayramı 2. gün"),
            };
            var yanlis = beklenen.Where(b => !Yalniz(servis, b.Gun, b.Ad, yarim: false)).ToList();
            Denetle("akıştaki İngilizce ve yanlış yazılmış Kurban Bayramı adları Türkçe", yanlis.Count == 0,
                string.Join(" ; ", yanlis.Select(b => Yazi(servis, b.Gun))));

            Denetle("\"(kesin değil)\" eki ve belirsizlik korunuyor (2036)",
                GunTatilleri(servis, new DateOnly(2036, 2, 8)) is [("Kurban Bayramı 2. gün (kesin değil)", false, true)],
                Yazi(servis, new DateOnly(2036, 2, 8)));

            var gunler = (IDictionary)Oku(servis, "Days")!;
            var adlar = gunler.Keys.Cast<DateOnly>().SelectMany(g => GunTatilleri(servis, g)).Select(t => t.Ad).ToList();
            var kalan = adlar.Where(a => a.Contains("Bayrami") || a.Contains("Day ") || a.Contains("Sacrifice") || a.Contains("Eid"))
                .Distinct().ToList();
            Denetle("hiçbir tatil adında İngilizce ya da \"Bayrami\" kalmıyor", adlar.Count > 0 && kalan.Count == 0,
                string.Join(" ; ", kalan));

            var ornekler = new (string Akistaki, string Beklenen)[]
            {
                ("Kurban Bayrami Day 3 (kesin değil)", "Kurban Bayramı 3. gün (kesin değil)"),
                ("Kurban Bayrami", "Kurban Bayramı"),
                ("Ramazan Bayrami", "Ramazan Bayramı"),
                ("Sacrifice Feast Holiday", "Kurban Bayramı"),
                ("Sacrifice Feast Holiday (kesin değil)", "Kurban Bayramı (kesin değil)"),
                ("Eid al Adha Public Sector Holiday", "Kurban Bayramı (idari izin)"),
                ("Eid al Fitr", "Ramazan Bayramı"),
                ("Ramadan Feast Day 2", "Ramazan Bayramı 2. gün"),
                ("Eve of Sacrifice Feast (half day)", "Kurban Bayramı Arifesi (yarım gün)"),
                // Doğru adlar ve sabit tatiller olduğu gibi kalır.
                ("Ramazan Bayramı Arifesi (yarım gün) (kesin değil)", "Ramazan Bayramı Arifesi (yarım gün) (kesin değil)"),
                ("Kurban Bayramı (kesin değil)", "Kurban Bayramı (kesin değil)"),
                ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", "Atatürk’ü Anma, Gençlik ve Spor Bayramı"),
                ("Cumhuriyet Bayramı Arifesi (yarım gün)", "Cumhuriyet Bayramı Arifesi (yarım gün)"),
                ("Yılbaşı", "Yılbaşı"),
            };
            var tutmayan = ornekler.Select(o => (o.Akistaki, o.Beklenen, Cikan: AdiDuzelt(o.Akistaki)))
                .Where(o => o.Cikan != o.Beklenen).ToList();
            Denetle("ad düzeltmesi örnekleri (doğru adlar ve sabit tatiller değişmiyor)", tutmayan.Count == 0,
                string.Join(" ; ", tutmayan.Select(o => $"{o.Akistaki} → {o.Cikan}")));
        }
        finally
        {
            ((IDisposable)servis).Dispose();
        }
    }

    // ----------------------------------------------------------------- 21. madde

    private static void Madde21()
    {
        Console.WriteLine("21) Aynı güne düşen iki tatil de saklanıyor, gün kutusunda ikisi de görünüyor");
        Type servisTipi = Tip("Kalender.Calendar.Holidays.HolidayService");
        Type ayristirici = Tip("Kalender.Calendar.Holidays.IcsParser");

        List<object> Ayikla(string akis)
            => ((IEnumerable)Statik(servisTipi, "Extract", Statik(ayristirici, "Parse", akis))!).Cast<object>().ToList();

        List<object> kayitlar = Ayikla(GoogleAkisi());
        List<string> GunKayitlari(DateOnly gun)
            => kayitlar.Where(k => (DateOnly)Oku(k, "Date")! == gun).Select(k => (string)Oku(k, "Name")!).ToList();

        Denetle("akıştan ayıklanınca 19 Mayıs 2027'deki iki kayıt da kalıyor",
            GunKayitlari(new DateOnly(2027, 5, 19)).ToHashSet()
                .SetEquals(new[] { "Atatürk’ü Anma, Gençlik ve Spor Bayramı", "Kurban Bayrami Day 4 (kesin değil)" }),
            string.Join(" | ", GunKayitlari(new DateOnly(2027, 5, 19))));
        Denetle("23 Nisan 2023 ve 23 Nisan 2029'daki iki kayıt da kalıyor",
            GunKayitlari(new DateOnly(2023, 4, 23)).Count == 2 && GunKayitlari(new DateOnly(2029, 4, 23)).Count == 2);

        List<object> ayni = Ayikla(TatilAkisi(
            ("20300101", "20300102", "Yılbaşı", Yarim),
            ("20300101", "20300102", "Yılbaşı", Tam),
            ("20300830", "20300831", "Zafer Bayramı", Tam),
            ("20300830", "20300831", "Zafer Bayramı", Tam)));
        Denetle("aynı ad aynı güne iki kez gelirse bir kez, tam gün olarak kalıyor",
            ayni.Count == 2 && ayni.All(k => Oku(k, "Kind")!.ToString() == "Full"), ayni.Count + " kayıt");

        OnbellegiSil();
        var sahte = new SahteTatilAkisi { Govde = GoogleAkisi() };
        object servis = TatilServisi(sahte);
        try
        {
            Yenile(servis, true);
            var mayis19 = GunTatilleri(servis, new DateOnly(2027, 5, 19));
            Denetle("19 Mayıs 2027'de iki tatil: 19 Mayıs ve Kurban Bayramı'nın 4. günü",
                mayis19.SequenceEqual(new[]
                {
                    ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", false, false),
                    ("Kurban Bayramı 4. gün", false, false),
                }), Yazi(servis, new DateOnly(2027, 5, 19)));
            Denetle("23 Nisan 2029'da iki tatil, tam gün önce: 23 Nisan ve Kurban Bayramı arifesi",
                GunTatilleri(servis, new DateOnly(2029, 4, 23)).SequenceEqual(new[]
                {
                    ("Ulusal Egemenlik ve Çocuk Bayramı", false, false),
                    ("Kurban Bayramı Arifesi (yarım gün)", true, false),
                }), Yazi(servis, new DateOnly(2029, 4, 23)));
            Denetle("tablonun dışındaki yılda da (23 Nisan 2023) iki tatil",
                GunTatilleri(servis, new DateOnly(2023, 4, 23)).Select(t => t.Ad).ToHashSet()
                    .SetEquals(new[] { "Ulusal Egemenlik ve Çocuk Bayramı", "Ramazan Bayramı" }),
                Yazi(servis, new DateOnly(2023, 4, 23)));

            // Takvimin gördüğü gün içeriği ve renk kuralı (tam gün yarım güne üstün).
            object? icerik19 = GunIcerigi(servis, new DateOnly(2027, 5, 19));
            object? icerik23 = GunIcerigi(servis, new DateOnly(2029, 4, 23));
            object? icerikArife = GunIcerigi(servis, new DateOnly(2029, 2, 13));

            static List<(string Baslik, bool YarimGun)> TatilSatirlari(object? icerik)
                => icerik is null
                    ? new()
                    : ((IEnumerable)Oku(icerik, "Entries")!).Cast<object>()
                        .Where(e => Oku(e, "Kind")!.ToString() == "Holiday")
                        .Select(e => ((string)Oku(e, "Title")!, (bool)Oku(e, "YarimGun")!))
                        .ToList();

            Denetle("19 Mayıs 2027 kırmızı; gün kutusu için iki tatil satırı, ikisi de tam gün",
                Kirmizi(icerik19) && TatilSatirlari(icerik19).SequenceEqual(new[]
                {
                    ("Atatürk’ü Anma, Gençlik ve Spor Bayramı", false),
                    ("Kurban Bayramı 4. gün", false),
                }), string.Join(" | ", TatilSatirlari(icerik19)));
            Denetle("23 Nisan 2029 kırmızı (tam gün arifeye üstün); arife satırı yarım gün işaretli",
                Kirmizi(icerik23) && TatilSatirlari(icerik23).SequenceEqual(new[]
                {
                    ("Ulusal Egemenlik ve Çocuk Bayramı", false),
                    ("Kurban Bayramı Arifesi (yarım gün)", true),
                }), string.Join(" | ", TatilSatirlari(icerik23)));
            Denetle("yalnız arife olan gün (13 Şubat 2029) kırmızı değil",
                icerikArife is not null && !Kirmizi(icerikArife) && TatilSatirlari(icerikArife).Count == 1);

            // Gün kutusu: pencere kurulur ama gösterilmez; satırlar ve renkleri okunur.
            Type pencereTipi = Tip("Kalender.Ui.DayPopupWindow");
            var pencere = (Window)Activator.CreateInstance(pencereTipi)!;
            try
            {
                object kirmizi = pencereTipi.GetField("HolidayBrush", Hepsi)!.GetValue(null)!;
                object olagan = pencereTipi.GetField("TitleBrush", Hepsi)!.GetValue(null)!;

                List<UIElement> Satirlar(object? icerik)
                {
                    Cagir(pencere, "BuildLines", icerik);
                    return ((Panel)AlanOku(pencere, "Lines")).Children.Cast<UIElement>().ToList();
                }

                List<UIElement> s19 = Satirlar(icerik19);
                var y19 = s19.OfType<TextBlock>().ToList();
                Denetle("gün kutusunda 19 Mayıs 2027'nin iki tatili iki satırda, arada ayraç, ikisi de kırmızı",
                    s19.Count == 3 && s19[1] is Border && y19.Count == 2
                    && y19[0].Text == "Atatürk’ü Anma, Gençlik ve Spor Bayramı" && y19[1].Text == "Kurban Bayramı 4. gün"
                    && y19.All(y => ReferenceEquals(y.Foreground, kirmizi)),
                    string.Join(" | ", y19.Select(y => y.Text)));

                List<UIElement> s23 = Satirlar(icerik23);
                var y23 = s23.OfType<TextBlock>().ToList();
                Denetle("gün kutusunda 23 Nisan 2029: tam gün tatil kırmızı, arife olağan renkte",
                    s23.Count == 3 && y23.Count == 2
                    && y23[0].Text == "Ulusal Egemenlik ve Çocuk Bayramı" && ReferenceEquals(y23[0].Foreground, kirmizi)
                    && y23[1].Text == "Kurban Bayramı Arifesi (yarım gün)" && ReferenceEquals(y23[1].Foreground, olagan),
                    string.Join(" | ", y23.Select(y => y.Text)));
            }
            finally
            {
                pencere.Close();
            }
        }
        finally
        {
            ((IDisposable)servis).Dispose();
        }
    }

    // ------------------------------------------------- derlenmiş koddaki komutlar

    /// <summary>Bir IL komutu: yeri, işlem kodu, varsa çağrılan yöntem ya da alan, dallanıyorsa hedefi.</summary>
    private sealed record Komut(int Yer, OpCode Kod, MemberInfo? Uye, int Hedef);

    private static readonly Dictionary<short, OpCode> IslemKodlari = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .GroupBy(k => k.Value)
        .ToDictionary(g => g.Key, g => g.First());

    /// <summary>Yöntemin gövdesini komut komut çözer (KodIncelemesi'ndeki gibi).</summary>
    private static List<Komut> Komutlar(MethodBase yontem)
    {
        byte[] il = yontem.GetMethodBody()!.GetILAsByteArray()!;
        var sonuc = new List<Komut>();
        int i = 0;
        while (i < il.Length)
        {
            int yer = i;
            short deger = il[i] == 0xFE ? unchecked((short)(0xFE00 | il[i + 1])) : il[i];
            OpCode kod = IslemKodlari[deger];
            i += kod.Size;

            MemberInfo? uye = null;
            try
            {
                uye = kod.OperandType switch
                {
                    OperandType.InlineMethod => yontem.Module.ResolveMethod(BitConverter.ToInt32(il, i)),
                    OperandType.InlineField => yontem.Module.ResolveField(BitConverter.ToInt32(il, i)),
                    _ => null,
                };
            }
            catch (ArgumentException)
            {
                // Genel türün açık örneği; bu sınamada aranan bir şey değil.
            }

            int uzunluk = kod.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, i)),
                _ => 4,
            };

            int hedef = kod.OperandType switch
            {
                OperandType.ShortInlineBrTarget => i + 1 + unchecked((sbyte)il[i]),
                OperandType.InlineBrTarget => i + 4 + BitConverter.ToInt32(il, i),
                _ => -1,
            };

            i += uzunluk;
            sonuc.Add(new Komut(yer, kod, uye, hedef));
        }

        return sonuc;
    }

    private static bool Cagri(Komut k, Type tip, string ad)
        => k.Uye is MethodBase m && m.DeclaringType == tip && m.Name == ad;

    private static int Bul(List<Komut> komutlar, Func<Komut, bool> kosul, int bas = 0)
    {
        for (int i = Math.Max(0, bas); i < komutlar.Count; i++)
        {
            if (kosul(komutlar[i]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Türün iç türlerindeki (lambda ve async gövdeleri) yöntemlerin komutları.</summary>
    private static List<List<Komut>> IcYontemler(Type tip)
        => tip.GetNestedTypes(Hepsi)
            .SelectMany(t => t.GetMethods(Hepsi | BindingFlags.DeclaredOnly))
            .Where(m => m.GetMethodBody() is not null)
            .Select(Komutlar)
            .ToList();

    // ------------------------------------------- 22, 23, 28: ortak yardımcılar

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string yol, IntPtr dosya, uint bayraklar);

    [DllImport("kernel32.dll")]
    private static extern bool FreeLibrary(IntPtr modul);

    /// <summary>
    /// LOAD_LIBRARY_AS_IMAGE_RESOURCE: kütüphane çalışan bir uygulamadaki gibi
    /// görüntü olarak eşlenir (dosyası kilitlenir), kodu çalıştırılmaz.
    /// </summary>
    private const uint GoruntuOlarak = 0x20;

    /// <summary>Kopyası "yüklü kütüphane" olarak eşlenecek küçük bir Windows kütüphanesi.</summary>
    private static string SistemKutuphanesi => Path.Combine(Environment.SystemDirectory, "version.dll");

    /// <summary>Ürünün adı: "Kalender" ya da "Kalender Lite".</summary>
    private static string Urun => Uyg.GetCustomAttribute<AssemblyProductAttribute>()!.Product;

    /// <summary>Kurulu exe'nin adı (uzantısız): açılım klasörü bu adla açılır.</summary>
    private static string KendiAdi
        => Path.GetFileNameWithoutExtension((string)Tip("Kalender.Core.Installer").GetProperty("InstalledExe", Hepsi)!.GetValue(null)!);

    /// <summary>Kurucunun adı (uzantısız; Kalender-Kurulum ya da Kalender-Lite-Kurulum).</summary>
    private static string KurucuAdi
        => Path.GetFileNameWithoutExtension((string)Tip("Kalender.Core.UpdateService").GetField("AssetName", Hepsi)!.GetValue(null)!);

    private static Exception? Yakala(Action islem)
    {
        try
        {
            islem();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    /// <summary>Klasördeki her şeyin göreli yolu ve boyu (yoksa "yok"): dokunulmadığını denetlemek için.</summary>
    private static string KlasorOzeti(string klasor)
    {
        if (!Directory.Exists(klasor))
        {
            return "yok";
        }

        return string.Join("|", Directory.EnumerateFileSystemEntries(klasor, "*", SearchOption.AllDirectories)
            .Select(y => Path.GetRelativePath(klasor, y) + (File.Exists(y) ? ":" + new FileInfo(y).Length : ""))
            .Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// İşi, %TEMP%'i (TMP ve TEMP) geçici bir klasöre çevirerek çalıştırır:
    /// açılım klasörleri (%TEMP%\.net) ve güncelleme indirmeleri oraya gider.
    /// Sonunda eski değerler geri konur, klasör silinir.
    /// </summary>
    private static void GeciciTempte(Action<string> islem)
    {
        string? tmp = Environment.GetEnvironmentVariable("TMP");
        string? temp = Environment.GetEnvironmentVariable("TEMP");
        string klasor = Path.Combine(Path.GetTempPath(), "Kalender İnceleme Temp ğüşıöç " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        Environment.SetEnvironmentVariable("TMP", klasor);
        Environment.SetEnvironmentVariable("TEMP", klasor);
        try
        {
            if (!string.Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\'), klasor, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Temp klasörü çevrilemedi: " + Path.GetTempPath());
            }

            islem(klasor);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TMP", tmp);
            Environment.SetEnvironmentVariable("TEMP", temp);
            try
            {
                Directory.Delete(klasor, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("   (geçici Temp klasörü silinemedi: " + ex.Message + ")");
            }
        }
    }

    // ----------------------------------------------------------------- 22. madde

    private static void Madde22()
    {
        Console.WriteLine("22) Kurulum ve kaldırma hatasında sade Türkçe ileti; ayrıntı günlükte");
        Type kurucu = Tip("Kalender.Core.Installer");
        string urun = Urun;
        string Ileti(Exception ex) => (string)Statik(kurucu, "HataAciklamasi", ex)!;
        string gunlukKlasoru = (string)Tip("Kalender.Core.Paths").GetProperty("LogFolder", Hepsi)!.GetValue(null)!;

        bool KullanimdaMi(string ileti) => ileti.StartsWith(urun + "'") && ileti.Contains("şu an kullanımda")
            && ileti.Contains("açık olan " + urun + " kapatılamamış olabilir") && ileti.EndsWith("kapatıp yeniden deneyin.");

        GeciciTempte(temp =>
        {
            string kaynak = Path.Combine(temp, "yeni.exe");
            File.WriteAllBytes(kaynak, new byte[] { 0x4D, 0x5A, 1, 2, 3 });

            // Çalışan exe gibi: kurulu dosya bir süreçte yüklü (görüntü olarak eşlenmiş).
            string calisan = Path.Combine(temp, "calisan.exe");
            File.Copy(SistemKutuphanesi, calisan);
            IntPtr modul = LoadLibraryExW(calisan, IntPtr.Zero, GoruntuOlarak);
            Exception? yuklu = Yakala(() => File.Copy(kaynak, calisan, overwrite: true));
            FreeLibrary(modul);
            Denetle("sınama düzeni: yüklü exe'nin üzerine yazılamadı (IOException)", modul != IntPtr.Zero && yuklu is IOException,
                yuklu?.GetType().Name ?? "hata yok");
            if (yuklu is not null)
            {
                Denetle("kurulu exe çalışıyorken: dosya kullanımda, kapatıp yeniden deneyin", KullanimdaMi(Ileti(yuklu)), Ileti(yuklu));
                Denetle(".NET'in İngilizce iletisi gösterilmiyor", !Ileti(yuklu).Contains(yuklu.Message), yuklu.Message);
            }

            // Başka bir programın (virüs koruması gibi) açık tuttuğu dosya.
            string acik = Path.Combine(temp, "acik.exe");
            File.WriteAllBytes(acik, new byte[] { 1 });
            Exception? tutulan;
            using (new FileStream(acik, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                tutulan = Yakala(() => File.Copy(kaynak, acik, overwrite: true));
            }

            Denetle("başka programın açık tuttuğu dosya: kullanımda, kapatıp yeniden deneyin",
                tutulan is IOException && KullanimdaMi(Ileti(tutulan)), tutulan is null ? "hata yok" : Ileti(tutulan));

            // Yazma izni yok: salt okunur hedef.
            string saltOkunur = Path.Combine(temp, "salt-okunur.exe");
            File.WriteAllBytes(saltOkunur, new byte[] { 1 });
            File.SetAttributes(saltOkunur, FileAttributes.ReadOnly);
            Exception? izin = Yakala(() => File.Copy(kaynak, saltOkunur, overwrite: true));
            File.SetAttributes(saltOkunur, FileAttributes.Normal);
            Denetle("yazma izni yokken: klasöre yazma izni yok",
                izin is UnauthorizedAccessException && Ileti(izin) == urun + (urun.EndsWith("Lite") ? "'ın" : "'in") + " klasörüne yazma izni yok.",
                izin is null ? "hata yok" : Ileti(izin));
        });

        // Disk dolu (ERROR_DISK_FULL, ERROR_HANDLE_DISK_FULL): .NET'in verdiği biçimde kurulur.
        const string dolu = "Diskte yeterli boş yer yok. Biraz yer açıp yeniden deneyin.";
        Denetle("disk doluyken: yer açıp yeniden deneyin",
            Ileti(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070))) == dolu
            && Ileti(new IOException("The disk is full.", unchecked((int)0x80070027))) == dolu);

        var ornekler = new Exception[]
        {
            new IOException("Unknown I/O error."),
            new InvalidOperationException("Operation is not valid due to the current state of the object."),
            new IOException("The network path was not found.", unchecked((int)0x80070035)),
        };
        var tutmayan = ornekler.Where(e => !(Ileti(e).StartsWith("Beklenmeyen bir sorun çıktı.")
            && Ileti(e).Contains("günlük dosyasına yazıldı") && Ileti(e).Contains(gunlukKlasoru) && !Ileti(e).Contains(e.Message))).ToList();
        Denetle("öbür hatalarda genel Türkçe ileti, günlüğün yeriyle; İngilizce ileti yok", tutmayan.Count == 0,
            string.Join(" ; ", tutmayan.Select(Ileti)));

        // Kurulum ve kaldırma (çağrılmaz; çalışan uygulamayı kapatırdı): derlenmiş koddan.
        Type gunluk = Tip("Kalender.Core.Log");
        foreach (string ad in new[] { "Install", "Uninstall" })
        {
            List<Komut> k = Komutlar(kurucu.GetMethod(ad, Hepsi)!);
            int yaz = Bul(k, x => Cagri(x, gunluk, "Error"));
            int aciklama = Bul(k, x => Cagri(x, kurucu, "HataAciklamasi"));
            Denetle($"{ad}: hata önce günlüğe yazılıyor, kullanıcıya Türkçe açıklama gidiyor; .NET'in iletisi kullanılmıyor",
                yaz >= 0 && aciklama > yaz && !k.Any(x => x.Uye is MethodBase { Name: "get_Message" }), $"{yaz} / {aciklama}");
        }
    }

    // ----------------------------------------------------------------- 23. madde

    private static void Madde23()
    {
        Console.WriteLine("23) Eski açılım klasörleri siliniyor; kendi, kullanımdaki ve öbür uygulamanınki kalıyor");
        Type kurucu = Tip("Kalender.Core.Installer");
        Type hizmetTipi = Tip("Kalender.Core.UpdateService");
        string kendiAdi = KendiAdi;
        string kurucuAdi = KurucuAdi;
        bool lite = kendiAdi == "KalenderLite";
        string oburAdi = lite ? "Kalender" : "KalenderLite";
        string oburKurucu = lite ? "Kalender-Kurulum" : "Kalender-Lite-Kurulum";
        Console.WriteLine($"   ({kendiAdi}, {kurucuAdi}*; dokunulmayacak: {oburAdi}, {oburKurucu}*)");

        MethodInfo silYontemi = kurucu.GetMethod("EskiAcilimKlasorleriniSil", Hepsi, new[] { typeof(string), typeof(string) })!;
        int Sil(string? kendi) => (int)silYontemi.Invoke(null, new object?[] { kendiAdi, kendi })!;

        string gercekNet = Path.Combine(Path.GetTempPath(), ".net");
        string gercekOnce = KlasorOzeti(gercekNet);

        GeciciTempte(temp =>
        {
            string net = Path.Combine(temp, ".net");
            string Kur(string goreli, params string[] dosyalar)
            {
                string k = Path.Combine(net, goreli);
                Directory.CreateDirectory(k);
                foreach (string d in dosyalar)
                {
                    File.WriteAllBytes(Path.Combine(k, d), new byte[] { 0x4D, 0x5A, 1, 2 });
                }

                return k;
            }

            bool Var(string goreli) => Directory.Exists(Path.Combine(net, goreli));
            int Dosyalar(string goreli) => Var(goreli)
                ? Directory.GetFiles(Path.Combine(net, goreli), "*", SearchOption.AllDirectories).Length
                : -1;

            // Uygulamanın kendi adındaki klasör: iki eski sürüm, bu sürecin sürümü,
            // kütüphanesi bir süreçte yüklü olan bir sürüm.
            Kur(kendiAdi + @"\eskiSurum1", "wpfgfx_cor3.dll", "vcruntime140_cor3.dll");
            Kur(kendiAdi + @"\eskiSurum2\alt", "PenImc_cor3.dll");
            Kur(kendiAdi + @"\kendiSurum", "wpfgfx_cor3.dll", "PresentationNative_cor3.dll");
            string yukluSurum = Kur(kendiAdi + @"\yukluSurum", "vcruntime140_cor3.dll");
            File.Copy(SistemKutuphanesi, Path.Combine(yukluSurum, "PresentationNative_cor3.dll"));

            // Kurucunun klasörleri: biri kullanılmıyor, biri tarayıcıdan "(1)" ekiyle
            // inmiş, biri çalışan bir kurucunun (dosyası açık).
            Kur(kurucuAdi + @"\paket1", "wpfgfx_cor3.dll");
            Kur(kurucuAdi + @" (1)\paket1", "wpfgfx_cor3.dll");
            string calisanKurucu = Kur(kurucuAdi + @"-9.9\paket2", "wpfgfx_cor3.dll", "vcruntime140_cor3.dll");

            // Öbür uygulamanın ve başka bir uygulamanın klasörleri.
            Kur(oburAdi + @"\paket3", "wpfgfx_cor3.dll");
            Kur(oburKurucu + @"\paket3", "wpfgfx_cor3.dll");
            Kur(oburKurucu + @"-9.9\paket3", "wpfgfx_cor3.dll");
            Kur(@"BaskaUygulama\paket4", "wpfgfx_cor3.dll");

            IntPtr modul = LoadLibraryExW(Path.Combine(yukluSurum, "PresentationNative_cor3.dll"), IntPtr.Zero, GoruntuOlarak);
            Denetle("sınama düzeni: kütüphane görüntü olarak yüklendi", modul != IntPtr.Zero);
            int silinen;
            try
            {
                using (new FileStream(Path.Combine(calisanKurucu, "wpfgfx_cor3.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    silinen = Sil("kendiSurum");
                }
            }
            finally
            {
                FreeLibrary(modul);
            }

            Denetle("eski sürümlerin açılım klasörleri silindi", !Var(kendiAdi + @"\eskiSurum1") && !Var(kendiAdi + @"\eskiSurum2"));
            Denetle("bu sürecin kendi açılım klasörü dosyalarıyla duruyor", Dosyalar(kendiAdi + @"\kendiSurum") == 2);
            Denetle("kütüphanesi yüklü klasör atlandı, öbür dosyası da silinmedi (yarım silme yok)",
                Dosyalar(kendiAdi + @"\yukluSurum") == 2, Dosyalar(kendiAdi + @"\yukluSurum") + " dosya");
            Denetle("kullanılmayan kurucu klasörleri bütünüyle silindi", !Var(kurucuAdi) && !Var(kurucuAdi + " (1)"));
            Denetle("çalışan kurucunun (dosyası açık) klasörü eksiksiz duruyor", Dosyalar(kurucuAdi + @"-9.9\paket2") == 2);
            Denetle("öbür uygulamanın ve başka uygulamaların klasörlerine dokunulmadı",
                Dosyalar(oburAdi + @"\paket3") == 1 && Dosyalar(oburKurucu + @"\paket3") == 1
                && Dosyalar(oburKurucu + @"-9.9\paket3") == 1 && Dosyalar(@"BaskaUygulama\paket4") == 1);
            Denetle("silinen klasör sayısı 4", silinen == 4, silinen.ToString());

            int ikinci = Sil("kendiSurum");
            Denetle("kilit kalkınca sonraki çalıştırmada onlar da siliniyor, kendi klasörü yine duruyor",
                ikinci == 2 && !Var(kendiAdi + @"\yukluSurum") && !Var(kurucuAdi + "-9.9")
                && Dosyalar(kendiAdi + @"\kendiSurum") == 2 && Dosyalar(oburAdi + @"\paket3") == 1, ikinci.ToString());

            Kur(kendiAdi + @"\eskiSurum3", "wpfgfx_cor3.dll");
            Kur(kurucuAdi + @"\paket5", "wpfgfx_cor3.dll");
            int ucuncu = Sil(null);
            Denetle("kendi klasörü bulunamazsa uygulamanın klasörünün içine hiç dokunulmuyor; kurucununki yine siliniyor",
                ucuncu == 1 && Var(kendiAdi + @"\eskiSurum3") && Dosyalar(kendiAdi + @"\kendiSurum") == 2 && !Var(kurucuAdi),
                ucuncu.ToString());

            // Yalnız kurulu kopyada: bu sınama kurulu kopya değil; ilk denetimin temizliği hiçbir şey silmemeli.
            string bekleyen = Kur(kurucuAdi + @"\paket6", "wpfgfx_cor3.dll");
            object hizmet = YeniHizmet();
            try
            {
                Cagir(hizmet, "EskiAcilimKlasorleriniTemizle");
                Thread.Sleep(500);
                Denetle("kurulu olmayan kopyada (bu sınama) ilk denetimin temizliği başlamıyor",
                    Directory.Exists(bekleyen) && !(bool)AlanOku(hizmet, "_acilimTemizligiBasladi"));
            }
            finally
            {
                ((IDisposable)hizmet).Dispose();
            }
        });

        Denetle("gerçek Temp'teki açılım klasörlerine dokunulmadı", KlasorOzeti(gercekNet) == gercekOnce);

        // Bu sürecin açılım klasörü, yüklü kütüphanelerinin yolundan.
        MethodInfo adYontemi = kurucu.GetMethod("AcilimKlasoruAdi", Hepsi)!;
        string? Ad(params string?[] yollar) => (string?)adYontemi.Invoke(null, new object[] { kendiAdi, yollar });
        Denetle("kendi açılım klasörü yüklü kütüphanenin yolundan bulunuyor (Temp'in kısa adlı yolu dâhil)",
            Ad(@"C:\Windows\System32\ntdll.dll",
                @"C:\Users\KULLAN~1\AppData\Local\Temp\.net\" + kendiAdi + @"\0ehY4UDA8bTx\PresentationNative_cor3.dll") == "0ehY4UDA8bTx"
            && Ad(@"C:\Temp\.NET\" + kendiAdi.ToLowerInvariant() + @"\Abc_1-x\wpfgfx_cor3.dll") == "Abc_1-x");
        Denetle("öbür uygulamanın, kurucunun ya da kurulu exe'nin yolu kendi klasörü sayılmıyor",
            Ad(@"C:\T\.net\" + oburAdi + @"\X\wpfgfx_cor3.dll", @"C:\T\.net\" + kurucuAdi + @"\Y\wpfgfx_cor3.dll",
                @"C:\Users\u\AppData\Local\Kalender\app\Kalender.exe", @"C:\T\.net\" + kendiAdi + @"\yalnizKlasor", null) is null);

        // Kuruluş: ilk denetimle birlikte, yalnız kurulu kopyada, arka planda.
        List<Komut> temizle = Komutlar(hizmetTipi.GetMethod("EskiAcilimKlasorleriniTemizle", Hepsi)!);
        int kuruluMu = Bul(temizle, x => Cagri(x, kurucu, "get_RunningInstalled"));
        int arkaPlan = Bul(temizle, x => x.Uye is MethodInfo { Name: "Run" } m && m.DeclaringType == typeof(Task));
        Denetle("temizlik önce kurulu kopya mı diye bakıyor, sonra arka planda başlıyor",
            kuruluMu >= 0 && arkaPlan > kuruluMu, $"{kuruluMu} / {arkaPlan}");
        List<List<Komut>> ic = IcYontemler(hizmetTipi);
        Denetle("arka planda kurucunun eski açılım klasörü temizliği çalışıyor",
            ic.Any(k => k.Any(x => Cagri(x, kurucu, "EskiAcilimKlasorleriniSil"))));
        Denetle("zamanlayıcıda ilk denetimle birlikte (denetimden önce) başlatılıyor",
            ic.Any(k => Bul(k, x => Cagri(x, hizmetTipi, "EskiAcilimKlasorleriniTemizle")) is int t && t >= 0
                && Bul(k, x => Cagri(x, hizmetTipi, "CheckAsync")) > t));
    }

    // ----------------------------------------------------------------- 24. madde

    private static void Madde24()
    {
        Console.WriteLine("24) Kurulum penceresi: Ayarlar'dan yalnız açılışta başlama kapatılabiliyor");
        object kip = Enum.Parse(Tip("Kalender.Ui.SetupMode"), "Install");
        var pencere = (Window)Activator.CreateInstance(Tip("Kalender.Ui.SetupWindow"), Hepsi, null, new[] { kip }, null)!;
        try
        {
            string not = ((TextBlock)pencere.FindName("NoteText")).Text;
            Denetle("kurulum notu kısayolun da kapatılabileceğini söylemiyor, yalnız açılışta başlamayı anlatıyor",
                !not.Contains("İkisini") && not.EndsWith("Windows açılışında kendiliğinden başlar. Bunu Ayarlar'dan kapatabilirsiniz."),
                not);
        }
        finally
        {
            pencere.Close();
        }
    }

    // ----------------------------------------------------------------- 28. madde

    /// <summary>GitHub'ın yerine geçen küçük HTTP sunucusu (Guncelleme sınamasındaki gibi).</summary>
    private sealed class Sunucu : IDisposable
    {
        private readonly TcpListener _dinleyici = new(IPAddress.Loopback, 0);
        private readonly Dictionary<string, (byte[] Govde, int Parca, TimeSpan Ara)> _yollar = new();
        private readonly Dictionary<string, int> _sayac = new();

        public Sunucu()
        {
            _dinleyici.Start();
            _ = Task.Run(DonguAsync);
        }

        public string Adres => $"http://127.0.0.1:{((IPEndPoint)_dinleyici.LocalEndpoint).Port}";

        /// <summary>Gövde <paramref name="parca"/> parçada, aralarında <paramref name="ara"/> bekleyerek gönderilir.</summary>
        public void Koy(string yol, byte[] govde, int parca = 1, TimeSpan ara = default)
        {
            lock (_yollar)
            {
                _yollar[yol] = (govde, parca, ara);
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
                    (byte[] Govde, int Parca, TimeSpan Ara)? yanit;
                    lock (_yollar)
                    {
                        _sayac[yol] = _sayac.GetValueOrDefault(yol) + 1;
                        yanit = _yollar.TryGetValue(yol, out var y) ? y : null;
                    }

                    if (yanit is not (byte[] govde, int parca, TimeSpan ara))
                    {
                        await akis.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 404 X\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
                        return;
                    }

                    await akis.WriteAsync(Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 X\r\nContent-Length: {govde.Length}\r\n"
                        + "Content-Type: application/octet-stream\r\nConnection: close\r\n\r\n"));
                    int boy = (govde.Length + parca - 1) / parca;
                    for (int i = 0; i < parca; i++)
                    {
                        if (i > 0)
                        {
                            await Task.Delay(ara);
                        }

                        int bas = i * boy;
                        await akis.WriteAsync(govde.AsMemory(bas, Math.Min(boy, govde.Length - bas)));
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

    /// <summary>Ayar dosyasına dokunmayan, bellekte duran ayarlarla bir güncelleme hizmeti.</summary>
    private static object YeniHizmet()
    {
        object depo = Activator.CreateInstance(Tip("Kalender.Settings.SettingsStore"), nonPublic: true)!;
        return Activator.CreateInstance(Tip("Kalender.Core.UpdateService"), Hepsi, null, new[] { depo }, null)!;
    }

    /// <summary>GitHub'ın "en son sürüm" yanıtı; indirme adresi https olmalı, bu yüzden uydurma bir adres.</summary>
    private static byte[] SurumYaniti(string etiket) => JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
    {
        ["tag_name"] = etiket,
        ["html_url"] = "https://example.invalid/surum",
        ["assets"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["name"] = (string)Tip("Kalender.Core.UpdateService").GetField("AssetName", Hepsi)!.GetValue(null)!,
                ["browser_download_url"] = "https://example.invalid/kurulum.exe",
                ["size"] = 0,
            },
        },
    });

    /// <summary>İşi arayüz iş parçacığındaki gibi bir Dispatcher döngüsünde çalıştırır (await'ler oraya döner).</summary>
    private static void DispatcherdaCalistir(Func<Task> islem)
    {
        var cerceve = new DispatcherFrame();
        Exception? hata = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try
            {
                await islem();
            }
            catch (Exception ex)
            {
                hata = ex;
            }
            finally
            {
                cerceve.Continue = false;
            }
        });
        Dispatcher.PushFrame(cerceve);
        if (hata is not null)
        {
            ExceptionDispatchInfo.Capture(hata).Throw();
        }
    }

    private static void Madde28()
    {
        Console.WriteLine("28) Denetim sürerken başlayan indirme: durum ve bulunan sürüm değişmiyor, ikinci tıklama bir şey yapmıyor");
        string gercekIndirme = (string)Tip("Kalender.Core.UpdateService").GetProperty("DownloadFolder", Hepsi)!.GetValue(null)!;
        string indirmeOnce = KlasorOzeti(gercekIndirme);

        GeciciTempte(_ => DispatcherdaCalistir(Madde28Async));

        Denetle("gerçek güncelleme indirme klasörüne dokunulmadı", KlasorOzeti(gercekIndirme) == indirmeOnce);
    }

    private static async Task Madde28Async()
    {
        Type hizmetTipi = Tip("Kalender.Core.UpdateService");
        PropertyInfo api = hizmetTipi.GetProperty("LatestReleaseApi", Hepsi)!;
        string gercekApi = (string)api.GetValue(null)!;
        Version kurulu = (Version)hizmetTipi.GetProperty("CurrentVersion", Hepsi)!.GetValue(null)!;
        string urun = Urun;

        using var sunucu = new Sunucu();
        api.SetValue(null, sunucu.Adres + "/latest");
        object h = YeniHizmet();
        try
        {
            Task Denetim() => (Task)Cagir(h, "CheckAsync", false)!;
            Task Kur() => (Task)Cagir(h, "InstallAsync", false)!;
            string Durum() => Oku(h, "State")!.ToString()!;
            int Indirme() => sunucu.Istek("/indir.exe");

            sunucu.Koy("/latest", SurumYaniti("v99.0"));
            await Denetim();
            Denetle("sınama düzeni: yeni sürüm bulundu", Durum() == "Available", Durum());

            // Kurulum dosyası yerel sunucudan, yavaş (~4 sn). Rastgele baytlar
            // uygulama olmadığı için indirme doğrulamada durur, kurucu başlatılmaz.
            byte[] govde = RandomNumberGenerator.GetBytes(400_000);
            object bilgi = Activator.CreateInstance(Tip("Kalender.Core.UpdateInfo"), Hepsi, null, new object?[]
            {
                new Version(99, 0, 0), sunucu.Adres + "/indir.exe", (long)govde.Length,
                Convert.ToHexString(SHA256.HashData(govde)), "https://example.invalid/surum",
            }, null)!;
            sunucu.Koy("/indir.exe", govde, parca: 10, ara: TimeSpan.FromMilliseconds(400));

            // Ayarlar penceresi açılırken başlayan denetimin yanıtı yavaş (~1,5 sn);
            // kullanıcı bu arada bildirime tıklıyor.
            sunucu.Koy("/latest", SurumYaniti("v99.0"), parca: 2, ara: TimeSpan.FromMilliseconds(1500));
            hizmetTipi.GetProperty("Available", Hepsi)!.SetValue(h, bilgi);
            Task denetim = Denetim();
            Task kurulum = Kur();
            Denetle("sınama düzeni: denetim sürerken indirme başladı", Durum() == "Downloading" && !denetim.IsCompleted, Durum());

            await denetim;
            Denetle("denetim bitince durum yine 'indiriliyor', bulunan sürüm aynı",
                Durum() == "Downloading" && ReferenceEquals(Oku(h, "Available"), bilgi) && !kurulum.IsCompleted, Durum());

            Task ikinci = Kur();
            Denetle("indirme sürerken ikinci tıklama hiçbir şey yapmıyor", ikinci.IsCompleted && Indirme() == 1,
                $"{Indirme()} indirme");
            await ikinci;
            await kurulum;
            string hata = (string?)Oku(h, "Error") ?? "";
            Denetle("indirme bir kez yapıldı, \"diske yazılamadı\" hatası çıkmadı (dosya doğrulamada durdu)",
                Indirme() == 1 && Durum() == "Failed" && hata.Contains("beklenen " + urun) && !hata.Contains("diske"),
                $"{Indirme()} indirme; {hata}");

            // Sürüm geri çekilmiş: denetim yeni sürüm bulamıyor, indirme sürüyor.
            hizmetTipi.GetProperty("Available", Hepsi)!.SetValue(h, bilgi);
            sunucu.Koy("/latest", SurumYaniti("v" + kurulu.ToString(3)), parca: 2, ara: TimeSpan.FromMilliseconds(1500));
            Task denetim2 = Denetim();
            Task kurulum2 = Kur();
            await denetim2;
            Denetle("denetim yeni sürüm bulamasa da indirme sürerken durum ve bulunan sürüm yerinde",
                Durum() == "Downloading" && ReferenceEquals(Oku(h, "Available"), bilgi), Durum());
            await kurulum2;

            // Durum başka bir yoldan "yeni sürüm var"a dönse bile indirme sürerken ikinci tıklama bir şey yapmaz.
            int once = Indirme();
            Task kurulum3 = Kur();
            await Task.Delay(200);
            hizmetTipi.GetProperty("State", Hepsi)!.SetValue(h, Enum.Parse(Tip("Kalender.Core.UpdateState"), "Available"));
            Task ucuncu = Kur();
            Denetle("indirme sürerken durum ne gösterirse göstersin ikinci indirme başlamıyor",
                ucuncu.IsCompleted && Indirme() == once + 1, $"{Indirme() - once} indirme");
            await ucuncu;
            await kurulum3;

            // İndirme başarısız olunca "Yeniden dene" yine çalışıyor.
            string onceki = Durum();
            Task yeniden = Kur();
            Denetle("başarısız indirmeden sonra yeniden denenebiliyor",
                onceki == "Failed" && Durum() == "Downloading" && !yeniden.IsCompleted, $"{onceki} > {Durum()}");
            await yeniden;
        }
        finally
        {
            ((IDisposable)h).Dispose();
            api.SetValue(null, gercekApi);
        }
    }

    // ------------------------------------- duvar kâğıdı: ortak yardımcılar

    private static void Yaz(object o, string ad, object? deger) => o.GetType().GetProperty(ad, Hepsi)!.SetValue(o, deger);

    /// <summary>Resim kipi açıkken Windows'un verdiği türden bir yol.</summary>
    private const string ResimAcik = @"C:\Users\deneme\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper";

    /// <summary>Windows'un duvar kâğıdı arayüzünün sahtesi; aygıt yolları verilir, ekranlar boş başlar.</summary>
    private static SahteDuvarKagidi Sahte(params string[] aygitlar)
    {
        var s = (SahteDuvarKagidi)DispatchProxy.Create(Tip("Kalender.Interop.IDesktopWallpaper"), typeof(SahteDuvarKagidi));
        s.Aygitlar.AddRange(aygitlar);
        foreach (string a in aygitlar)
        {
            s.Ekranda[a] = string.Empty;
        }

        return s;
    }

    /// <summary>Yan yana hayali ekranlar; kimlikleri aygıt yollarıyla aynı.</summary>
    private static IList Ekranlar(params string[] aygitlar)
    {
        Type ekranTipi = Tip("Kalender.Interop.MonitorTarget");
        var liste = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(ekranTipi))!;
        for (int i = 0; i < aygitlar.Length; i++)
        {
            object m = Activator.CreateInstance(ekranTipi, true)!;
            Yaz(m, "Handle", IntPtr.Zero);
            Yaz(m, "DeviceName", aygitlar[i]);
            Yaz(m, "StableId", aygitlar[i]);
            Yaz(m, "Bounds", new System.Drawing.Rectangle(i * 1920, 0, 1920, 1080));
            Yaz(m, "WorkArea", new System.Drawing.Rectangle(i * 1920, 0, 1920, 1032));
            Yaz(m, "Dpi", 96u);
            Yaz(m, "IsPrimary", i == 0);
            liste.Add(m);
        }

        return liste;
    }

    /// <summary>
    /// Windows'un duvar kâğıdı ayarını (SPI_GETDESKWALLPAPER) okuyan kancayı
    /// verilen değerle değiştirip işi çalıştırır; gerçek ayar okunmaz. Boş
    /// metin düz renk (resim kipi kapalı), null okunamadı demek.
    /// </summary>
    private static void SistemDuvarKagidiyla(string? deger, Action islem)
    {
        PropertyInfo okuyucu = Tip("Kalender.Desktop.WallpaperService").GetProperty("SistemDuvarKagidiOkuyucu", Hepsi)!;
        object? gercek = okuyucu.GetValue(null);
        okuyucu.SetValue(null, new Func<string?>(() => deger));
        try
        {
            islem();
        }
        finally
        {
            okuyucu.SetValue(null, gercek);
        }
    }

    /// <summary>Yedek dosyasını okur; yoksa ya da okunamıyorsa null.</summary>
    private static object? YedekOku(string dosya, Type yedekTipi)
    {
        try
        {
            return File.Exists(dosya) ? JsonSerializer.Deserialize(File.ReadAllText(dosya), yedekTipi) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------- 7. madde

    private static void Madde7()
    {
        Console.WriteLine("7) Açılışta ekranda kalmış donmuş takvim, önceki duvar kâğıdı geri konamazsa kaldırılıyor");
        Type tuvalTipi = Tip("Kalender.Desktop.WallpaperCanvas");
        Type servis = Tip("Kalender.Desktop.WallpaperService");
        Type yoneticiTipi = Tip("Kalender.Desktop.DesktopLayerManager");
        string zemin = (string)Tip("Kalender.Core.Paths").GetProperty("CanvasFolder")!.GetValue(null)!;
        string takvimli = Path.Combine(zemin, "zemin-aaaaaaaa-20261015-101010-123.bmp");
        string takvimsiz = Path.Combine(zemin, "zemin-aaaaaaaa-20261015-101011-456-takvimsiz.bmp");
        string baskaKlasorde = Path.Combine(Path.GetTempPath(), "zemin-aaaaaaaa-20261015-101010-123.bmp");
        string foto = @"C:\Users\deneme\Pictures\deniz.jpg";

        // Ekrana bakış: Windows'un duvar kâğıdı arayüzünün sahtesiyle.
        MethodInfo? gosterenler = tuvalTipi.GetMethod("TakvimGosterenler", Hepsi);
        Denetle("takvimin durduğu ekranları ekrandan okuyan yöntem var", gosterenler is not null);
        if (gosterenler is null)
        {
            return;
        }

        HashSet<string> Gosterenler(IList ekranlar, SahteDuvarKagidi t, string? sistem)
        {
            HashSet<string> sonuc = new();
            SistemDuvarKagidiyla(sistem, () => sonuc = (HashSet<string>)gosterenler!.Invoke(null, new object?[] { ekranlar, t })!);
            return sonuc;
        }

        SahteDuvarKagidi s = Sahte("A", "B", "C", "D", "E");
        s.Ekranda["A"] = takvimli;
        s.Ekranda["B"] = foto;
        s.Ekranda["C"] = takvimsiz;
        s.Ekranda["D"] = baskaKlasorde;
        IList bes = Ekranlar("A", "B", "C", "D", "E");
        HashSet<string> bulunan = Gosterenler(bes, s, ResimAcik);
        Denetle("takvimli zeminin durduğu ekran bulunuyor (bu süreç hiç yazmamış olsa da)",
            bulunan.Contains("A"), string.Join(", ", bulunan));
        Denetle("kullanıcının fotoğrafı, başka klasördeki dosya ve boş ekran sayılmıyor",
            !bulunan.Contains("B") && !bulunan.Contains("D") && !bulunan.Contains("E"));
        Denetle("takvimsiz zemin sayılmıyor (her açılışta yeniden yazılmıyor)", !bulunan.Contains("C"));
        Denetle("Windows'ta düz renk seçiliyken (resim kipi kapalı) ekranda takvim yok sayılıyor",
            Gosterenler(bes, s, string.Empty).Count == 0);
        s.OkumadaHata = true;
        Denetle("ekrandakiler okunamazsa hiçbir ekran sayılmıyor", Gosterenler(bes, s, ResimAcik).Count == 0);

        SahteDuvarKagidi tek = Sahte("X");
        tek.Ortak = takvimli;
        Denetle("tek ekranda aygıt yolu eşleşmezse ortak yola bakılıyor (yazımdaki gibi)",
            Gosterenler(Ekranlar("M"), tek, ResimAcik).SetEquals(new[] { "M" }));
        Denetle("birden çok ekranda aygıt yolu eşleşmeyen ekran sayılmıyor",
            Gosterenler(Ekranlar("M", "N"), tek, ResimAcik).Count == 0);

        // Takvimsiz zemin dosya adından tanınıyor; takvim kaldırılırken yazılan zemin o adla yazılıyor.
        MethodInfo yol = tuvalTipi.GetMethod("DosyaYolu", Hepsi)!;
        bool Takvimsiz(string? y) => (bool)Statik(tuvalTipi, "TakvimsizZeminMi", y)!;
        bool ZeminMi(string? y) => (bool)Statik(servis, "ZeminDosyasiMi", y)!;
        string takvimsizYolu = (string)yol.Invoke(null, new object[] { "MON-A", true })!;
        string takvimliYolu = (string)yol.Invoke(null, new object[] { "MON-A", false })!;
        Denetle("takvimsiz zeminin adı ekli ve yine takvimin kendi dosyası sayılıyor",
            Takvimsiz(takvimsizYolu) && ZeminMi(takvimsizYolu)
            && Path.GetFileName(takvimsizYolu).EndsWith("-takvimsiz.bmp", StringComparison.Ordinal),
            Path.GetFileName(takvimsizYolu));
        Denetle("takvimli görüntünün adı eksiz",
            !Takvimsiz(takvimliYolu) && ZeminMi(takvimliYolu) && !Takvimsiz(takvimli) && Takvimsiz(takvimsiz) && !Takvimsiz(null));

        List<Komut> uygula = Komutlar(tuvalTipi.GetMethod("Apply", Hepsi)!);
        int dosyaAdi = Bul(uygula, x => Cagri(x, tuvalTipi, "DosyaYolu"));
        Denetle("takvim kaldırılırken (kapanış, geri yükleme) yazılan zeminin adına ek geliyor",
            dosyaAdi > 0 && uygula[dosyaAdi - 1].Kod == OpCodes.Ldarg_2);

        // Geri yükleme eksik kalınca: takvimin durduğu ekranlar, bu süreç yazmış
        // olsun olmasın, ekrandan okunuyor (derlenmiş koddan; gerçek duvar kâğıdına dokunmamak için).
        List<Komut> k = Komutlar(yoneticiTipi.GetMethod("DuvarKagidiniGeriYukle", Hepsi)!);
        int restore = Bul(k, x => Cagri(x, servis, "Restore"));
        int ekrana = Bul(k, x => Cagri(x, tuvalTipi, "TakvimGosterenler"), restore);
        int zeminYaz = Bul(k, x => Cagri(x, yoneticiTipi, "PaintPlainBackground"), ekrana);
        Denetle("geri yükleme eksik kalınca takvimsiz zemin ekrana bakılarak yazılıyor, bu sürecin yazmış olması aranmıyor",
            restore >= 0 && ekrana > restore && zeminYaz > ekrana && Bul(k, x => Cagri(x, tuvalTipi, "get_HasPainted")) < 0,
            $"{restore} / {ekrana} / {zeminYaz}");
    }

    // ------------------------------------------------------------------ 29. madde

    /// <summary>Yedeğin yazımı ve okunamayan yedeğin kenara alınması, verilen klasördeki dosyalarla.</summary>
    private static void YedekDosyasiDenetimleri(Type servis, Type yedekTipi, string klasor)
    {
        string dosya = Path.Combine(klasor, "onceki-duvar-kagidi.json");
        string foto = Path.Combine(klasor, "deniz.jpg");
        string[] Bozuklar() => Directory.GetFiles(klasor, "onceki-duvar-kagidi.bozuk-*.json");
        void Temizle()
        {
            foreach (string f in Directory.GetFiles(klasor))
            {
                File.Delete(f);
            }
        }

        // Yazım: geçici dosya, diske boşaltma, yerine taşıma.
        MethodInfo? yaz = servis.GetMethod("YedegiYaz", Hepsi);
        object yedek = Activator.CreateInstance(yedekTipi, true)!;
        Yaz(yedek, "Wallpaper", foto);
        yaz?.Invoke(null, new[] { dosya, yedek });
        object? okunan = YedekOku(dosya, yedekTipi);
        Denetle("yedek yazılıyor ve okunabiliyor, geçici dosya kalmıyor",
            yaz is not null && okunan is not null && (string?)Oku(okunan, "Wallpaper") == foto && !File.Exists(dosya + ".tmp"));

        List<Komut> k = yaz is null ? new() : Komutlar(yaz);
        int akis = Bul(k, x => x.Kod == OpCodes.Newobj && x.Uye?.DeclaringType == typeof(FileStream));
        int bosalt = Bul(k, x => x.Uye is MethodInfo m && m.DeclaringType == typeof(FileStream) && m.Name == "Flush"
            && m.GetParameters().Length == 1, akis);
        int tasi = Bul(k, x => Cagri(x, typeof(File), "Move"), bosalt);
        Denetle("önce geçici dosyaya yazılıp diske boşaltılıyor (Flush(true)), sonra yerine taşınıyor",
            akis >= 0 && bosalt > akis && k[bosalt - 1].Kod == OpCodes.Ldc_I4_1 && tasi > bosalt, $"{akis} / {bosalt} / {tasi}");

        // Okunamayan yedekler: boş, yarım, sıfır baytlarla dolu (elektrik kesintisinden sonra görülen), "null".
        MethodInfo? kenara = servis.GetMethod("OkunamayanYedegiKenaraAl", Hepsi);
        void KenaraAl() => kenara?.Invoke(null, new object[] { dosya });
        var bozukIcerikler = new (string Ad, byte[] Icerik)[]
        {
            ("boş", Array.Empty<byte>()),
            ("yarım", Encoding.UTF8.GetBytes("{\r\n  \"CreatedUtc\": \"2026-10-15T10:00:00+00:00\",\r\n  \"Wallpaper\": \"C:")),
            ("sıfır baytlarla dolu", new byte[64]),
            ("null", Encoding.UTF8.GetBytes("null")),
        };
        foreach ((string ad, byte[] icerik) in bozukIcerikler)
        {
            Temizle();
            File.WriteAllBytes(dosya, icerik);
            KenaraAl();
            string[] bozuk = Bozuklar();
            Denetle($"okunamayan yedek ({ad}) \"bozuk-<tarih>\" adıyla kenara alınıyor, içeriği korunuyor",
                kenara is not null && !File.Exists(dosya) && bozuk.Length == 1
                && File.ReadAllBytes(bozuk[0]).SequenceEqual(icerik)
                && System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(bozuk[0]), @"^onceki-duvar-kagidi\.bozuk-\d{4}-\d{2}-\d{2}-\d{6}\.json$"),
                bozuk.Length == 1 ? Path.GetFileName(bozuk[0]) : $"{bozuk.Length} dosya");
        }

        Temizle();
        File.WriteAllText(dosya, "{");
        KenaraAl();
        File.WriteAllText(dosya, "{{");
        KenaraAl();
        Denetle("ikinci okunamayan yedek öncekini ezmiyor", kenara is not null && !File.Exists(dosya) && Bozuklar().Length == 2);

        Temizle();
        yaz?.Invoke(null, new[] { dosya, yedek });
        byte[] saglam = File.Exists(dosya) ? File.ReadAllBytes(dosya) : Array.Empty<byte>();
        KenaraAl();
        Denetle("okunabilen yedeğe dokunulmuyor",
            kenara is not null && File.Exists(dosya) && File.ReadAllBytes(dosya).SequenceEqual(saglam) && Bozuklar().Length == 0);

        Temizle();
        File.WriteAllText(dosya, "{");
        using (new FileStream(dosya, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            KenaraAl();
        }

        Denetle("o an açılamayan (kilitli) yedeğin içeriği bilinmiyor, kenara alınmıyor",
            kenara is not null && File.Exists(dosya) && Bozuklar().Length == 0);

        Temizle();
        KenaraAl();
        Denetle("yedek yoksa hiçbir dosya açılmıyor", kenara is not null && Directory.GetFiles(klasor).Length == 0);

        // Yedek alma ve geri yükleme okunamayan yedeği önce kenara alıyor (derlenmiş koddan).
        Type yollar = Tip("Kalender.Core.Paths");
        List<Komut> alma = Komutlar(servis.GetMethod("EnsureBackup", Hepsi)!);
        int almaKenara = Bul(alma, x => Cagri(x, servis, "OkunamayanYedegiKenaraAl"));
        int varMi = Bul(alma, x => Cagri(x, typeof(File), "Exists"));
        Denetle("yedek alınırken okunamayan yedek önce kenara alınıyor, sonra yedek var mı diye bakılıyor",
            almaKenara > 0 && Cagri(alma[almaKenara - 1], yollar, "get_BackupFile") && varMi > almaKenara);
        Denetle("yedek geçici dosya üzerinden yazılıyor (doğrudan WriteAllText yok)",
            Bul(alma, x => Cagri(x, servis, "YedegiYaz")) >= 0 && Bul(alma, x => Cagri(x, typeof(File), "WriteAllText")) < 0);

        List<Komut> geri = Komutlar(servis.GetMethod("Restore", Hepsi, new[] { typeof(bool).MakeByRefType() })!);
        int ilkOkuma = Bul(geri, x => Cagri(x, typeof(File), "ReadAllText"));
        List<int> geriKenara = geri.Select((x, i) => (x, i)).Where(t => Cagri(t.x, servis, "OkunamayanYedegiKenaraAl")).Select(t => t.i).ToList();
        Denetle("geri yüklemede asıl ve bekleyen yedek okunmadan önce denetleniyor",
            geriKenara.Count == 2 && ilkOkuma > geriKenara[1]
            && Cagri(geri[geriKenara[0] - 1], yollar, "get_BackupFile")
            && Cagri(geri[geriKenara[1] - 1], servis, "get_BekleyenYedekDosyasi"));
    }

    // ------------------------------------------------------------------ 32. madde

    /// <summary>Yedek hazırlanırken resim kipine de bakılıyor (sahte duvar kâğıdıyla).</summary>
    private static void DuzRenkDenetimleri(Type servis)
    {
        string zemin = (string)Tip("Kalender.Core.Paths").GetProperty("CanvasFolder")!.GetValue(null)!;
        string bizim = Path.Combine(zemin, "zemin-aaaaaaaa-20261015-101010-123.bmp");
        string foto = @"C:\Users\deneme\Pictures\deniz.jpg";

        // Gerçek ayar yalnız bir kez, yalnız okunarak.
        var gercekOkuyucu = (Func<string?>?)servis.GetProperty("SistemDuvarKagidiOkuyucu", Hepsi)?.GetValue(null);
        string? gercek = gercekOkuyucu?.Invoke();
        Console.WriteLine("   bilgi: Windows'un duvar kâğıdı ayarı: "
            + (gercek is null ? "okunamadı" : gercek.Length == 0 ? "boş (düz renk)" : Path.GetFileName(gercek)));
        Denetle("Windows'un duvar kâğıdı ayarı okunabiliyor (yalnız okuma)", gercek is not null);

        bool KipKapali(string? deger)
        {
            bool sonuc = false;
            SistemDuvarKagidiyla(deger, () => sonuc = (bool)Statik(servis, "ResimKipiKapali")!);
            return sonuc;
        }

        Denetle("düz renkte (yol boş) resim kipi kapalı sayılıyor; resim varken ya da ayar okunamazsa sayılmıyor",
            KipKapali(string.Empty) && KipKapali("  ") && !KipKapali(ResimAcik) && !KipKapali(null));

        SahteDuvarKagidi Takvimli()
        {
            SahteDuvarKagidi t = Sahte("A", "B");
            t.Ekranda["A"] = bizim;
            t.Ekranda["B"] = bizim;
            t.Ortak = bizim;
            t.Renk = 0x00336699;
            return t;
        }

        object? Hazirla(SahteDuvarKagidi t, string? sistem)
        {
            object? sonuc = null;
            SistemDuvarKagidiyla(sistem, () => sonuc = Statik(servis, "YedekHazirla", t));
            return sonuc;
        }

        object? duz = Hazirla(Takvimli(), string.Empty);
        Denetle("ekranlarda yalnız takvimin yolu var ama resim kipi kapalıysa yedek düz renk olarak alınıyor",
            duz is not null && !(bool)Oku(duz, "WallpaperEnabled")!
            && string.IsNullOrEmpty((string?)Oku(duz, "Wallpaper"))
            && ((IDictionary<string, string>)Oku(duz, "PerMonitor")!).Count == 0
            && (uint)Oku(duz, "BackgroundColor")! == 0x00336699u);
        Denetle("aynı ekranlarda resim kipi açıksa ya da ayar okunamazsa eskisi gibi yedek alınmıyor",
            Hazirla(Takvimli(), ResimAcik) is null && Hazirla(Takvimli(), null) is null);

        SahteDuvarKagidi karisik = Takvimli();
        karisik.Ekranda["B"] = foto;
        object? fotolu = Hazirla(karisik, string.Empty);
        Denetle("bir ekranda kullanıcının fotoğrafı varsa eskisi gibi o yedekleniyor",
            fotolu is not null && (bool)Oku(fotolu, "WallpaperEnabled")!
            && ((IDictionary<string, string>)Oku(fotolu, "PerMonitor")!).TryGetValue("B", out string? b) && b == foto);
    }

    private static void Madde29()
    {
        Console.WriteLine("29) Duvar kâğıdı yedeği geçici dosyayla yazılıyor; okunamayan yedek kenara alınıyor");
        Type servis = Tip("Kalender.Desktop.WallpaperService");
        Type yedekTipi = Tip("Kalender.Desktop.WallpaperBackup");
        Type yollar = Tip("Kalender.Core.Paths");
        string yedekDosyasi = (string)yollar.GetProperty("BackupFile")!.GetValue(null)!;
        string kok = Path.GetDirectoryName(yedekDosyasi)!;
        string klasor = Path.Combine(kok, "yedek sınaması");
        Directory.CreateDirectory(klasor);
        YedekDosyasiDenetimleri(servis, yedekTipi, klasor);

        // Uygulamanın klasörü geçici: yedek alma ve geri yükleme bütünüyle.
        string bekleyen = (string)servis.GetProperty("BekleyenYedekDosyasi", Hepsi)!.GetValue(null)!;
        string[] Bozuklar() => Directory.GetFiles(kok, "onceki-duvar-kagidi*bozuk-*.json");
        void YedekleriSil()
        {
            foreach (string f in Directory.GetFiles(kok, "onceki-duvar-kagidi*"))
            {
                File.Delete(f);
            }
        }

        string foto = Path.Combine(klasor, "dağ.jpg");
        File.WriteAllText(foto, "deneme");
        SahteDuvarKagidi s = Sahte("A", "B");
        s.Ekranda["A"] = foto;
        s.Ekranda["B"] = foto;

        YedekleriSil();
        File.WriteAllBytes(yedekDosyasi, new byte[32]);
        SistemDuvarKagidiyla(ResimAcik, () => Statik(servis, "EnsureBackup", s));
        object? taze = YedekOku(yedekDosyasi, yedekTipi);
        Denetle("okunamayan yedek varken yazmaya başlanınca o kenara alınıyor, ekrandaki duvar kâğıdı taze yedekleniyor",
            taze is not null && ((IDictionary<string, string>)Oku(taze, "PerMonitor")!).TryGetValue("A", out string? a) && a == foto
            && Bozuklar().Length == 1);

        // Asıl yedek de bekleyen de okunamıyor: ikisi de kenara alınır, yedek
        // yokmuş gibi davranılır. Yedek kalmadığı için gerçek duvar kâğıdı
        // yalnız okunur (ekranda bu geçici klasörün görüntüsü yok).
        YedekleriSil();
        File.WriteAllText(yedekDosyasi, string.Empty);
        File.WriteAllText(bekleyen, "{\"Wallpaper\":");
        object?[] cikti = { true };
        bool yuklendi = (bool)servis.GetMethod("Restore", Hepsi, new[] { typeof(bool).MakeByRefType() })!.Invoke(null, cikti)!;
        Denetle("geri yüklemede okunamayan yedek ve bekleyen kenara alınıyor, yedek yokmuş gibi davranılıyor (uyarı yok)",
            !File.Exists(yedekDosyasi) && !File.Exists(bekleyen) && Bozuklar().Length == 2 && yuklendi && !(bool)cikti[0]!,
            $"{Bozuklar().Length} kenarda, sonuç {yuklendi}, eksik {cikti[0]}");
        YedekleriSil();
    }

    private static void Madde32()
    {
        Console.WriteLine("32) Düz renk kullananda ekranda takvimin yolu görünse de yedek düz renk olarak alınıyor");
        Type servis = Tip("Kalender.Desktop.WallpaperService");
        DuzRenkDenetimleri(servis);

        // Uygulamanın klasörü geçici: yazma yeniden açılınca yedek gerçekten yazılıyor.
        string yedekDosyasi = (string)Tip("Kalender.Core.Paths").GetProperty("BackupFile")!.GetValue(null)!;
        string bizim = Path.Combine((string)Tip("Kalender.Core.Paths").GetProperty("CanvasFolder")!.GetValue(null)!,
            "zemin-aaaaaaaa-20261015-101010-123.bmp");
        SahteDuvarKagidi s = Sahte("A");
        s.Ekranda["A"] = bizim;
        s.Ortak = bizim;
        File.Delete(yedekDosyasi);
        SistemDuvarKagidiyla(string.Empty, () => Statik(servis, "EnsureBackup", s));
        object? yazilan = YedekOku(yedekDosyasi, Tip("Kalender.Desktop.WallpaperBackup"));
        Denetle("düz renkte yazma yeniden açılınca düz renk yedeği yazılıyor (ikinci geri dönüş düz rengi getirir)",
            yazilan is not null && !(bool)Oku(yazilan, "WallpaperEnabled")!);
        File.Delete(yedekDosyasi);
    }

    // ---------------------------- fare kancası, sağ tık ve menüler (2, 3, 4, 30, 33)

    private static readonly List<System.Drawing.Point> _menuNoktalari = new();

    /// <summary>MSLLHOOKSTRUCT: alt düzey fare kancasının olay bilgisi.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FareOlayi
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr Extra;
    }

    /// <summary>
    /// Takvimin fare kancası (KodIncelemesi'ndeki gibi); takvim x &lt; 1000, ay
    /// düğmesi yok. Kanca hiç takılmaz, gerçek fareye dokunulmaz: "takılı"
    /// görünsün diye tanıtıcısına sahte bir değer yazılır; kapatılmış sayıldığı
    /// için Arm gerçek kanca takmaz. Olaylar doğrudan işleyiciye verilir
    /// (<see cref="Gonder"/>); sağ tıkın noktası kaydedilir, menü açılmaz.
    /// </summary>
    private static object Kanca()
    {
        Type t = Tip("Kalender.Desktop.CalendarMouseHook");
        Type dugmeTipi = Tip("Kalender.Calendar.NavButton");
        Func<System.Drawing.Point, bool> takvimde = p => p.X < 1000;
        Delegate dugme = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(typeof(System.Drawing.Point), dugmeTipi),
            typeof(Program).GetMethod(nameof(DugmeYok), Hepsi)!.MakeGenericMethod(dugmeTipi));
        object kanca = Activator.CreateInstance(t, Dispatcher.CurrentDispatcher, takvimde, dugme)!;

        AlanAta(kanca, "_disposed", true);
        AlanAta(kanca, "_hook", new IntPtr(1));
        t.GetEvent("RightClicked")!.AddEventHandler(kanca, new Action<System.Drawing.Point>(_menuNoktalari.Add));
        return kanca;
    }

    private static T DugmeYok<T>(System.Drawing.Point p) where T : struct, Enum => Enum.Parse<T>("None");

    /// <summary>Kancaya yapay bir fare olayı verir; olay yutulduysa true.</summary>
    private static bool Gonder(object kanca, uint ileti, int x)
    {
        IntPtr bellek = Marshal.AllocHGlobal(Marshal.SizeOf<FareOlayi>());
        try
        {
            Marshal.StructureToPtr(new FareOlayi { X = x, Y = 10 }, bellek, false);
            return (IntPtr)Cagir(kanca, "OnMouse", 0, (IntPtr)ileti, bellek)! == (IntPtr)1;
        }
        finally
        {
            Marshal.FreeHGlobal(bellek);
        }
    }

    private static bool Takili(object kanca) => (IntPtr)AlanOku(kanca, "_hook") != IntPtr.Zero;

    private static bool Bekliyor(object kanca) => (bool)Oku(kanca, "BirakisBekleniyor")!;

    /// <summary>Dağıtıcıda bekleyen işleri çalıştırır.</summary>
    private static void Bosalt()
    {
        var cerceve = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => cerceve.Continue = false));
        Dispatcher.PushFrame(cerceve);
    }

    /// <summary>Dağıtıcıyı verilen süre boyunca çalıştırır.</summary>
    private static void Bekle(int ms)
    {
        var cerceve = new DispatcherFrame();
        var zaman = new DispatcherTimer(TimeSpan.FromMilliseconds(ms), DispatcherPriority.Background,
            (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                cerceve.Continue = false;
            },
            Dispatcher.CurrentDispatcher);
        zaman.Start();
        Dispatcher.PushFrame(cerceve);
    }

    /// <summary>
    /// Kurucusu çalıştırılmamış masaüstü katmanı: ekran yok, yani çizim hemen
    /// biter; duvar kâğıdına ve ayar dosyasına hiçbir şey yazılmaz.
    /// </summary>
    private static object Katman()
    {
        object katman = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            Tip("Kalender.Desktop.DesktopLayerManager"));
        AlanAta(katman, "_monitors", Array.CreateInstance(Tip("Kalender.Interop.MonitorTarget"), 0));
        return katman;
    }

    /// <summary>
    /// Kurucusu çalıştırılmamış uygulama: fare kancası, imleç takvimin üstünde.
    /// Bırakış olayı ve (verildiyse) katmanın çizim kancaları açılıştaki
    /// (Start) gibi bağlanır; sağ tık menüye bağlanmaz (menü açılmaz).
    /// </summary>
    private static object Uygulama(object kanca, object? katman = null)
    {
        object host = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Core.AppHost"));
        AlanAta(host, "_mouseHook", kanca);
        AlanAta(host, "_hoverInside", true);
        kanca.GetType().GetEvent("BirakisGeldi")!.AddEventHandler(kanca, new Action(() => Cagir(host, "UpdateMouseHook")));

        if (katman is not null)
        {
            AlanAta(host, "_layer", katman);
            Yaz(katman, "CizimeBaslarken", new Func<bool>(() => (bool)Cagir(host, "KancayiCizimIcinSok")!));
            katman.GetType().GetEvent("CizimBitti")!.AddEventHandler(katman, new Action(() => Cagir(host, "KancayiCizimdenSonraTak")));
        }

        return host;
    }

    /// <summary>Açılışta (Start) olaya ya da özelliğe verilen yöntem: çağrıdan önceki ldftn.</summary>
    private static string? BaslangicAbonesi(Type kaynak, string uye)
    {
        List<Komut> acilis = Komutlar(Tip("Kalender.Core.AppHost").GetMethod("Start", Hepsi)!);
        int cagri = Bul(acilis, x => Cagri(x, kaynak, uye));
        return cagri > 1 && acilis[cagri - 2].Kod == OpCodes.Ldftn ? acilis[cagri - 2].Uye?.Name : null;
    }

    /// <summary><paramref name="yer"/>'i try bölümünde tutan finally bloğu (yoksa null).</summary>
    private static ExceptionHandlingClause? Finally(MethodBase yontem, int yer)
        => yontem.GetMethodBody()!.ExceptionHandlingClauses.FirstOrDefault(c =>
            c.Flags == ExceptionHandlingClauseOptions.Finally
            && yer >= c.TryOffset && yer < c.TryOffset + c.TryLength);

    private static bool Icinde(ExceptionHandlingClause? c, int yer)
        => c is not null && yer >= c.HandlerOffset && yer < c.HandlerOffset + c.HandlerLength;

    // ------------------------------------------------------------------- 3. madde

    private static void Madde3()
    {
        Console.WriteLine("3) Duvar kâğıdı çizilirken fare kancası sökülü; yarım tıklamada çizim bırakışa kalıyor (kanca takılmaz)");
        const uint RDown = 0x0204, RUp = 0x0205;
        Type hostTipi = Tip("Kalender.Core.AppHost");
        Type katmanTipi = Tip("Kalender.Desktop.DesktopLayerManager");
        object veri = Tip("Kalender.Calendar.CalendarDataSnapshot").GetProperty("Empty")!.GetValue(null)!;

        object kanca = Kanca();
        object katman = Katman();

        // Her çizimin sonunda, uygulama kancaya yeniden bakmadan önce: kanca takılı mıydı?
        var cizimde = new List<bool>();
        katmanTipi.GetEvent("CizimBitti")!.AddEventHandler(katman, new Action(() => cizimde.Add(Takili(kanca))));
        object host = Uygulama(kanca, katman);

        // Google verisi ya da tatil güncellemesi (SetData) ve gün dönümü (SetToday)
        // katmanın Refresh'inden geçiyor.
        Cagir(katman, "SetData", veri, true);
        AlanAta(kanca, "_hook", new IntPtr(1));
        Cagir(katman, "SetToday", new DateOnly(2026, 10, 15));
        Denetle("veri değişince ve gün dönünce çizim fare kancası sökülüyken yapılıyor",
            cizimde.SequenceEqual(new[] { false, false }) && (int)AlanOku(host, "_cizimSuruyor") == 0,
            string.Join(",", cizimde));

        // Çizim sürerken (COM çağrısı mesaj döngüsünü döndürebilir) araya giren yoklama.
        AlanAta(kanca, "_hook", new IntPtr(1));
        AlanAta(host, "_hoverInside", false);
        AlanAta(host, "_cizimSuruyor", 1);
        Cagir(host, "UpdateMouseHook");
        bool dokunulmadi = Takili(kanca);
        AlanAta(host, "_cizimSuruyor", 0);
        Cagir(host, "UpdateMouseHook");
        Denetle("çizim sürerken araya giren yoklama kancaya dokunmuyor, çizim bitince kancaya yeniden bakılıyor",
            dokunulmadi && !Takili(kanca));

        // Yarım tıklama: sağ tuş takvimde basılıyken veri geldi.
        AlanAta(kanca, "_hook", new IntPtr(1));
        AlanAta(host, "_hoverInside", true);
        _menuNoktalari.Clear();
        cizimde.Clear();
        bool basis = Gonder(kanca, RDown, 100);
        Cagir(katman, "SetData", veri, true);
        Denetle("sağ tuş basılıyken gelen veri hemen çizilmiyor, kanca sökülmüyor (bırakış kabuğa gitmez)",
            basis && cizimde.Count == 0 && Takili(kanca) && Bekliyor(kanca));

        bool birakis = Gonder(kanca, RUp, 100);
        Bosalt();
        Denetle("bırakış kancada kalıyor, menü basılan noktada açılıyor",
            birakis && _menuNoktalari.SequenceEqual(new[] { new System.Drawing.Point(100, 10) }),
            string.Join(" ", _menuNoktalari));

        Bekle(300);
        Denetle("bırakıştan sonra bekletilen çizim bir kez, kanca sökülüyken yapılıyor",
            cizimde.SequenceEqual(new[] { false }) && (int)AlanOku(host, "_cizimSuruyor") == 0, string.Join(",", cizimde));

        // Bu aya dönüş süresi tuş basılıyken dolarsa ay değişmiyor (ay değişirken kanca sökülürdü).
        var ay = new DateOnly(2030, 3, 1);
        var buAy = new DateOnly(DateTime.Now.Year, DateTime.Now.Month, 1);
        var sayac = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
        sayac.Start();
        AlanAta(host, "_viewResetTimer", sayac);
        AlanAta(host, "_viewMonth", ay);
        AlanAta(kanca, "_hook", new IntPtr(1));
        Gonder(kanca, RDown, 100);
        Cagir(host, "AyaDonusSuresiDoldu");
        bool beklendi = (DateOnly)AlanOku(host, "_viewMonth") == ay && Takili(kanca) && Bekliyor(kanca) && sayac.IsEnabled;
        Gonder(kanca, RUp, 100);
        Bosalt();
        Cagir(host, "AyaDonusSuresiDoldu");
        sayac.Stop();
        Denetle("bu aya dönüş süresi tuş basılıyken dolarsa ay değişmiyor, kanca sökülmüyor; bırakıştan sonra dönülüyor",
            beklendi && (DateOnly)AlanOku(host, "_viewMonth") == buAy);

        Denetle("açılışta katmanın çizimi fare kancasına bağlanıyor (çizimden önce sökme, sonra yeniden bakma)",
            BaslangicAbonesi(katmanTipi, "set_CizimeBaslarken") == "KancayiCizimIcinSok"
            && BaslangicAbonesi(katmanTipi, "add_CizimBitti") == "KancayiCizimdenSonraTak");

        bool sayacYeni = false, sayacEski = false;
        foreach (MethodInfo m in hostTipi.GetMethods(Hepsi | BindingFlags.DeclaredOnly)
                     .Where(m => m.Name.StartsWith("<Start>", StringComparison.Ordinal)))
        {
            List<Komut> k = Komutlar(m);
            sayacYeni |= Bul(k, x => Cagri(x, hostTipi, "AyaDonusSuresiDoldu")) >= 0;
            sayacEski |= Bul(k, x => Cagri(x, hostTipi, "ChangeMonth")) >= 0;
        }

        Denetle("bu aya dönüş sayacı açılışta bırakışı bekleyen yola bağlanıyor", sayacYeni && !sayacEski);
    }

    // ------------------------------------------------------------------- 4. madde

    private static void Madde4()
    {
        Console.WriteLine("4) Tepsi menüsü açıkken takvime sağ tık ya da ok: tepsi menüsü kapanıyor, takvimin menüsü açılıyor ya da ay değişiyor (menü açılmaz)");
        Type hostTipi = Tip("Kalender.Core.AppHost");
        Type menuTipi = Tip("Kalender.Interop.PopupMenu");
        Type tepsiTipi = Tip("Kalender.Tray.TrayIcon");
        PropertyInfo acik = menuTipi.GetProperty("Acik", Hepsi)!;
        object sonraki = Enum.Parse(Tip("Kalender.Calendar.NavButton"), "Next");
        var nokta = new System.Drawing.Point(300, 200);
        var ay = new DateOnly(2030, 3, 1);

        // Menü yeniden açılırken uygulamanın dağıtıcısı kullanılıyor; pencere açılmaz.
        if (System.Windows.Application.Current is null)
        {
            _ = new System.Windows.Application();
        }

        object mesajlar = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
            Tip("Kalender.Interop.AppMessageWindow"));
        GC.SuppressFinalize(mesajlar);
        object host = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(hostTipi);
        AlanAta(host, "_messages", mesajlar);
        AlanAta(host, "_layer", Katman());
        AlanAta(host, "_viewMonth", ay);

        // Tepsi menüsü açık sayılıyor; menünün kendisi açılmaz. Açık menüyü
        // kapatma isteği (EndMenu) bu iş parçacığında menü olmadığı için bir şey yapmaz.
        acik.SetValue(null, true);
        try
        {
            Cagir(host, "OnCalendarRightClick", nokta);
            Denetle("tepsi menüsü açıkken takvime sağ tık bekletiliyor, açık menü kapatılıyor",
                Equals(AlanOku(host, "_pendingMenuPoint"), nokta));

            Cagir(host, "TepsiMenusuKapandi");
            bool alindi = AlanOku(host, "_pendingMenuPoint") is null;

            // Yeniden işlenen sağ tık, menü hâlâ açık sayıldığı için yine bekletilir; ekranda menü açılmaz.
            Bosalt();
            Denetle("tepsi menüsü kapanınca sağ tık aynı noktada yeniden işleniyor (takvimin menüsü orada açılır)",
                alindi && Equals(AlanOku(host, "_pendingMenuPoint"), nokta));
            AlanAta(host, "_pendingMenuPoint", null);

            Cagir(host, "OnNavClicked", sonraki);
            bool okBekledi = (DateOnly)AlanOku(host, "_viewMonth") == ay && Equals(AlanOku(host, "_pendingNav"), sonraki);
            acik.SetValue(null, false);
            Cagir(host, "TepsiMenusuKapandi");
            Denetle("tepsi menüsü açıkken ok ayı hemen değiştirmiyor, menü kapanınca ay değişiyor",
                okBekledi && (DateOnly)AlanOku(host, "_viewMonth") == ay.AddMonths(1) && AlanOku(host, "_pendingNav") is null);

            // Fare kancası tepsi menüsü açıkken imleç takvimin dışında da takılı (takvimin menüsündeki gibi).
            object kanca = Kanca();
            AlanAta(host, "_mouseHook", kanca);
            AlanAta(host, "_hoverInside", false);
            acik.SetValue(null, true);
            Cagir(host, "UpdateMouseHook");
            bool takiliKaldi = Takili(kanca);
            acik.SetValue(null, false);
            Cagir(host, "TepsiMenusuKapandi");
            Denetle("tepsi menüsü açıkken fare kancası takılı kalıyor, menü kapanınca sökülüyor",
                takiliKaldi && !Takili(kanca));
        }
        finally
        {
            acik.SetValue(null, false);
        }

        // PopupMenu.Show menüyü açarken "açık" der, kapanınca (hata olsa da) önceki değere döner.
        MethodInfo goster = menuTipi.GetMethod("Show", Hepsi)!;
        List<Komut> k = Komutlar(goster);
        int ac = Bul(k, x => Cagri(x, menuTipi, "set_Acik"));
        int izle = Bul(k, x => Cagri(x, Tip("Kalender.Interop.NativeMethods"), "TrackPopupMenuEx"), ac);
        int kapa = Bul(k, x => Cagri(x, menuTipi, "set_Acik"), izle);
        Denetle("menü açıkken PopupMenu.Acik doğru, kapanınca (hata olsa da) önceki değerine dönüyor",
            ac >= 0 && izle > ac && kapa > izle && Icinde(Finally(goster, k[izle].Yer), k[kapa].Yer), $"{ac} / {izle} / {kapa}");

        // Tepsi: menü kapanınca, seçilen satır bildirilmeden önce haber veriyor.
        List<Komut> t = Komutlar(tepsiTipi.GetMethod("ShowMenu", Hepsi)!);
        int show = Bul(t, x => Cagri(x, menuTipi, "Show"));
        int kapandi = Bul(t, x => x.Kod == OpCodes.Ldfld && x.Uye is FieldInfo f && f.Name == "MenuKapandi", show);
        int secildi = Bul(t, x => x.Kod == OpCodes.Ldfld && x.Uye is FieldInfo f && f.Name == "MenuItemClicked", show);
        Denetle("tepsi menüsü kapanınca haber veriyor (seçilen satırdan önce), açılışta uygulamaya bağlanıyor",
            show >= 0 && kapandi > show && secildi > kapandi
            && BaslangicAbonesi(tepsiTipi, "add_MenuKapandi") == "TepsiMenusuKapandi",
            $"{show} / {kapandi} / {secildi}");
    }

    // ------------------------------------- görünmeyen ayrı masaüstü (2, 30, 33)

    private const uint WS_VISIBLE = 0x10000000;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_CHILD = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Nokta
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dortgen
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>WNDCLASSEX.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PencereSinifi
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    /// <summary>INITCOMMONCONTROLSEX.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct OrtakDenetimler
    {
        public int dwSize;
        public uint dwICC;
    }

    /// <summary>LVITEMW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ListeOgesi
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    private delegate IntPtr PencereYordami(IntPtr h, uint ileti, IntPtr w, IntPtr l);

    /// <summary>Sınama pencerelerinin yordamı; çöp toplayıcı toplamasın diye alanda.</summary>
    private static readonly PencereYordami Yordam = DefWindowProc;

    /// <summary>Masaüstünü taklit eden ve başka programa ait sınama pencerelerinin sınıfları.</summary>
    private static readonly string[] SinamaSiniflari = { "Progman", "WorkerW", "SHELLDLL_DefView", "KalenderSinamaProgrami" };

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktop(string ad, IntPtr aygit, IntPtr kip, int bayraklar, uint erisim, IntPtr guvenlik);

    [DllImport("user32.dll")]
    private static extern bool CloseDesktop(IntPtr masa);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr masa);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref PencereSinifi sinif);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClass(string ad, IntPtr ornek);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint ekBicem, string sinif, string? ad, uint bicem,
        int x, int y, int genislik, int yukseklik, IntPtr ust, IntPtr menu, IntPtr ornek, IntPtr veri);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr h, uint ileti, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr h);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr h, int komut);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr h, uint ileti, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr h, ref Nokta n);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Nokta n);

    [DllImport("comctl32.dll")]
    private static extern bool InitCommonControlsEx(ref OrtakDenetimler d);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? ad);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr h, uint kimlik, ref Guid arayuz,
        [MarshalAs(UnmanagedType.Interface)] out Accessibility.IAccessible? nesne);

    /// <summary>
    /// İşi görünmeyen ayrı bir Windows masaüstünde, kendi iş parçacığında
    /// çalıştırır: sınama pencereleri kullanıcının ekranında hiç görünmez;
    /// WindowFromPoint ve FindWindow da yalnız o masaüstünün pencerelerini görür,
    /// gerçek masaüstüne bakılmaz. Sınıflar kaydedilir, iş bitince silinir.
    /// </summary>
    private static void AyriMasaustunde(Action islem)
    {
        IntPtr masa = CreateDesktop("KalenderIkinciInceleme" + Guid.NewGuid().ToString("N"),
            IntPtr.Zero, IntPtr.Zero, 0, 0x10000000 /*GENERIC_ALL*/, IntPtr.Zero);
        if (masa == IntPtr.Zero)
        {
            Denetle("görünmeyen masaüstü açıldı", false, $"hata {Marshal.GetLastWin32Error()}");
            return;
        }

        Exception? hata = null;
        var parca = new Thread(() =>
        {
            IntPtr ornek = GetModuleHandle(null);
            try
            {
                if (!SetThreadDesktop(masa))
                {
                    Denetle("iş parçacığı görünmeyen masaüstüne geçti", false, $"hata {Marshal.GetLastWin32Error()}");
                    return;
                }

                var ortak = new OrtakDenetimler { dwSize = Marshal.SizeOf<OrtakDenetimler>(), dwICC = 0x1 /*ICC_LISTVIEW_CLASSES*/ };
                InitCommonControlsEx(ref ortak);
                foreach (string ad in SinamaSiniflari)
                {
                    var sinif = new PencereSinifi
                    {
                        cbSize = Marshal.SizeOf<PencereSinifi>(),
                        lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Yordam),
                        hInstance = ornek,
                        lpszClassName = ad,
                    };
                    RegisterClassEx(ref sinif);
                }

                islem();
            }
            catch (Exception ex)
            {
                hata = ex;
            }
            finally
            {
                foreach (string ad in SinamaSiniflari)
                {
                    UnregisterClass(ad, ornek);
                }
            }
        });
        parca.SetApartmentState(ApartmentState.STA);
        parca.Start();
        parca.Join();
        CloseDesktop(masa);

        if (hata is not null)
        {
            ExceptionDispatchInfo.Capture(hata).Throw();
        }
    }

    /// <summary>Sınama penceresi; üst pencere verilirse onun çocuğu.</summary>
    private static IntPtr Pencere(string sinif, IntPtr ust, int x, int y, int genislik, int yukseklik, bool gorunur = true)
        => CreateWindowEx(0, sinif, null, (ust == IntPtr.Zero ? WS_POPUP : WS_CHILD) | (gorunur ? WS_VISIBLE : 0),
            x, y, genislik, yukseklik, ust, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);

    /// <summary>Kökü verilen sınıfta, içinde masaüstü listesi duran sınama penceresi; listeyi döndürür.</summary>
    private static IntPtr ListeliPencere(string kok, bool defView, int x, int y, out IntPtr kokPencere)
    {
        kokPencere = Pencere(kok, IntPtr.Zero, x, y, 600, 400);
        IntPtr ust = defView ? Pencere("SHELLDLL_DefView", kokPencere, 0, 0, 600, 400) : kokPencere;
        return Pencere("SysListView32", ust, 0, 0, 600, 400);
    }

    private static Func<System.Drawing.Point, bool> Yontem(Type tip, string ad)
        => (Func<System.Drawing.Point, bool>)Delegate.CreateDelegate(typeof(Func<System.Drawing.Point, bool>),
            tip.GetMethod(ad, Hepsi)!);

    // ------------------------------------------------------------------- 2. madde

    private static void Madde2()
    {
        Console.WriteLine("2) Takvimin üstündeki başka programın listesi masaüstü sayılmıyor (görünmeyen ayrı masaüstünde)");
        Func<System.Drawing.Point, bool> masaustu = Yontem(Tip("Kalender.Desktop.HoverWatcher"), "IsOnDesktopSurface");

        AyriMasaustunde(() =>
        {
            // Masaüstü (kökü Progman, ikincisi WorkerW) ve takvimin üstünde duran,
            // ön planda olmayan iki program: Kayıt Defteri gibi düz liste, dosya
            // penceresi gibi SHELLDLL_DefView içinde liste.
            IntPtr liste = ListeliPencere("Progman", defView: true, 0, 0, out IntPtr progman);
            ListeliPencere("WorkerW", defView: true, 0, 500, out IntPtr worker);
            IntPtr programListesi = ListeliPencere("KalenderSinamaProgrami", defView: false, 1000, 0, out IntPtr program);
            ListeliPencere("KalenderSinamaProgrami", defView: true, 1000, 500, out IntPtr gezgin);
            try
            {
                Denetle("deneme geçerli: noktadaki pencereler görünmeyen masaüstündeki sınama pencereleri",
                    WindowFromPoint(new Nokta { X = 100, Y = 100 }) == liste
                    && WindowFromPoint(new Nokta { X = 1100, Y = 100 }) == programListesi);
                Denetle("deneme geçerli: masaüstünün listesi (kökü Progman ya da WorkerW) masaüstü sayılıyor",
                    masaustu(new(100, 100)) && masaustu(new(100, 600)));
                Denetle("başka programın listesi masaüstü sayılmıyor (takvimin menüsü ve okları ona tıklamayı yutmaz)",
                    !masaustu(new(1100, 100)));
                Denetle("başka programda SHELLDLL_DefView içindeki liste de masaüstü sayılmıyor", !masaustu(new(1100, 600)));
            }
            finally
            {
                foreach (IntPtr p in new[] { progman, worker, program, gezgin })
                {
                    DestroyWindow(p);
                }
            }
        });
    }

    // ------------------------------------------------------------------ 30. madde

    private static void Madde30()
    {
        Console.WriteLine("30) Tam ekran yalnız ön plandaki pencere imlecin ekranındaysa takvimi susturuyor (pencere gösterilmez)");
        Type izleyici = Tip("Kalender.Desktop.HoverWatcher");
        Type noktaTipi = Tip("Kalender.Interop.NativeMethods+POINT");
        IList ekranlar = (IList)Statik(Tip("Kalender.Interop.MonitorEnumerator"), "All")!;

        object Imlec(int x, int y)
        {
            object n = Activator.CreateInstance(noktaTipi)!;
            noktaTipi.GetField("X")!.SetValue(n, x);
            noktaTipi.GetField("Y")!.SetValue(n, y);
            return n;
        }

        bool Ekraninda(IntPtr pencere, int x, int y) => (bool)Statik(izleyici, "ImlecinEkraninda", pencere, Imlec(x, y))!;

        AyriMasaustunde(() =>
        {
            // Gösterilmeyen pencereler: biri ana ekranın sol üstünde, biri hiçbir ekranın üstünde değil.
            IntPtr ekranda = Pencere("KalenderSinamaProgrami", IntPtr.Zero, 0, 0, 400, 300, gorunur: false);
            IntPtr disarida = Pencere("KalenderSinamaProgrami", IntPtr.Zero, 100000, 0, 400, 300, gorunur: false);
            try
            {
                Denetle("ön plandaki pencere imlecin ekranındaysa tam ekranda takvim eskisi gibi dinlenmiyor",
                    Ekraninda(ekranda, 10, 10));
                Denetle("ön plandaki pencere imlecin ekranında değilse takvim dinleniyor (ön planda pencere yoksa da)",
                    !Ekraninda(disarida, 10, 10) && !Ekraninda(IntPtr.Zero, 10, 10));

                if (ekranlar.Count > 1)
                {
                    var ikinci = (System.Drawing.Rectangle)Oku(ekranlar[1]!, "Bounds")!;
                    var birinci = (System.Drawing.Rectangle)Oku(ekranlar[0]!, "Bounds")!;
                    IntPtr obur = Pencere("KalenderSinamaProgrami", IntPtr.Zero, ikinci.X + 10, ikinci.Y + 10, 400, 300, gorunur: false);
                    Denetle("öbür ekrandaki pencere imlecin ekranında sayılmıyor",
                        !Ekraninda(obur, birinci.X + 10, birinci.Y + 10) && Ekraninda(obur, ikinci.X + 20, ikinci.Y + 20));
                    DestroyWindow(obur);
                }
                else
                {
                    Console.WriteLine("   (tek ekran: öbür ekrandaki pencere ayrıca sınanamadı)");
                }
            }
            finally
            {
                DestroyWindow(ekranda);
                DestroyWindow(disarida);
            }
        });

        // Durum (tam ekran, sunum) Windows'tan okunuyor; sınamada değiştirilemez. Sıra derlenmiş koddan.
        MethodInfo izin = izleyici.GetMethod("NotificationsAllowed", Hepsi)!;
        List<Komut> k = Komutlar(izin);
        Type nm = Tip("Kalender.Interop.NativeMethods");
        int durum = Bul(k, x => Cagri(x, nm, "SHQueryUserNotificationState"));
        int onPlan = Bul(k, x => Cagri(x, nm, "GetForegroundWindow"), durum);
        int ekran = Bul(k, x => Cagri(x, izleyici, "ImlecinEkraninda"), onPlan);
        Denetle("tam ekran ya da sunum kipinde ön plandaki pencerenin imlecin ekranında olup olmadığına bakılıyor",
            izin.GetParameters().Length == 1 && durum >= 0 && onPlan > durum && ekran > onPlan, $"{durum} / {onPlan} / {ekran}");
    }

    // ------------------------------------------------------------------ 33. madde

    private static void Madde33()
    {
        Console.WriteLine("33) Masaüstü simgeleri gizliyken gizli simgenin yeri simge sayılmıyor (görünmeyen ayrı masaüstünde)");
        Type simgeler = Tip("Kalender.Desktop.MasaustuSimgeleri");
        Func<System.Drawing.Point, bool> ustunde = Yontem(simgeler, "Ustunde");

        AyriMasaustunde(() =>
        {
            IntPtr liste = ListeliPencere("Progman", defView: true, 0, 0, out IntPtr progman);
            IntPtr metin = Marshal.StringToHGlobalUni("Kısayol");
            IntPtr oge = Marshal.AllocHGlobal(Marshal.SizeOf<ListeOgesi>());
            IntPtr dortgen = Marshal.AllocHGlobal(Marshal.SizeOf<Dortgen>());
            try
            {
                // Listeye bir simge: LVM_INSERTITEMW, yeri LVM_GETITEMRECT (LVIR_BOUNDS).
                Marshal.StructureToPtr(new ListeOgesi { mask = 0x1 /*LVIF_TEXT*/, pszText = metin }, oge, false);
                SendMessage(liste, 0x104D, IntPtr.Zero, oge);
                Marshal.WriteInt32(dortgen, 0);
                SendMessage(liste, 0x100E, IntPtr.Zero, dortgen);
                var d = Marshal.PtrToStructure<Dortgen>(dortgen);
                var orta = new Nokta { X = (d.Left + d.Right) / 2, Y = (d.Top + d.Bottom) / 2 };
                ClientToScreen(liste, ref orta);
                var simge = new System.Drawing.Point(orta.X, orta.Y);

                if ((IntPtr)Statik(simgeler, "Liste")! != liste)
                {
                    Denetle("deneme geçerli: masaüstü listesi olarak sınama listesi bulunuyor", false);
                    return;
                }

                Denetle("deneme geçerli: görünen simgenin yeri simge sayılıyor, boş yer sayılmıyor",
                    ustunde(simge) && !ustunde(new(500, 350)), $"simge {simge}");

                // "Masaüstü simgelerini göster" kapalı: liste gizli.
                ShowWindow(liste, 0 /*SW_HIDE*/);
                Guid arayuz = new("618736E0-3C3D-11CF-810C-00AA00389B71");
                object? isabet = null;
                if (AccessibleObjectFromWindow(liste, 0xFFFFFFFC /*OBJID_CLIENT*/, ref arayuz, out Accessibility.IAccessible? erisim) == 0
                    && erisim is not null)
                {
                    isabet = erisim.accHitTest(simge.X, simge.Y);
                    Marshal.ReleaseComObject(erisim);
                }

                Denetle("deneme geçerli: erişilebilirlik arayüzü gizli listedeki simgeye de isabet veriyor (Windows'un davranışı)",
                    isabet is int kimlik && kimlik > 0, $"{isabet ?? "yok"}");

                bool gizliyken = ustunde(simge);
                ShowWindow(liste, 8 /*SW_SHOWNA*/);
                Denetle("simgeler gizliyken gizli simgenin yeri simge sayılmıyor (tıklama takvime gider), gösterilince yine sayılıyor",
                    !gizliyken && ustunde(simge));
            }
            finally
            {
                Statik(simgeler, "Birak");
                Marshal.FreeHGlobal(metin);
                Marshal.FreeHGlobal(oge);
                Marshal.FreeHGlobal(dortgen);
                DestroyWindow(progman);
            }
        });
    }

    // --------------------- renkler, fotoğraf kenarı ve "Takvimi taşı" (8–12. maddeler)

    private static readonly DateOnly RenkGunu = new(2026, 10, 15);

    private static object BosVeri() => Tip("Kalender.Calendar.CalendarDataSnapshot").GetProperty("Empty")!.GetValue(null)!;

    private static object Secenek(string tur, string ad) => Enum.Parse(Tip("Kalender.Settings." + tur), ad);

    private static string Onaltilik(DColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>Görünüm ayarı: düz renk ya da fotoğraf kipi, yazı rengi otomatik ya da elle.</summary>
    private static object YeniGorunum(string zemin, string yaziKipi = "Auto", string yazi = "#FFFFFF", bool fotograf = false)
    {
        object g = Activator.CreateInstance(Tip("Kalender.Settings.AppearanceSettings"), true)!;
        Yaz(g, "SolidColor", zemin);
        Yaz(g, "TextColorMode", Secenek("TextColorMode", yaziKipi));
        Yaz(g, "TextColor", yazi);
        Yaz(g, "BackgroundMode", Secenek("BackgroundMode", fotograf ? "Photo" : "Solid"));
        return g;
    }

    private static readonly string[] TemaRenkleri = { "Background", "Text", "Muted", "Holiday", "TodayCircle", "TodayText" };

    /// <summary>Uygulamanın bugünkü hesabıyla tema renkleri (ARGB), <see cref="TemaRenkleri"/> sırasıyla.</summary>
    private static int[] YeniTema(object gorunum, bool fotograf, bool otomatikYazi = false)
    {
        object tema = Statik(Tip("Kalender.Rendering.Theme"), "ZemineGore", gorunum, fotograf, otomatikYazi)!;
        return TemaRenkleri.Select(a => ((DColor)Oku(tema, a)!).ToArgb()).ToArray();
    }

    private static bool UygulamadaAcik(DColor c) => (bool)Statik(Tip("Kalender.Rendering.ColorTools"), "IsLight", c)!;

    private static DColor Renk(int argb) => DColor.FromArgb(argb);

    /// <summary>
    /// Kalender 1.13'ün ve Lite 1.8'in renk hesabının kopyası (ColorTools ve
    /// Theme.ZemineGore; ayarlardan tek tek verilen renkler hariç, sınamada hiçbiri
    /// verilmiyor). Hazır renklerin görünümü değişmedi mi diye yenisiyle karşılaştırılır.
    /// </summary>
    private static class EskiRenk
    {
        public static DColor Ayristir(string hex)
        {
            string s = hex.Trim().TrimStart('#');
            return DColor.FromArgb(255, Convert.ToInt32(s[..2], 16), Convert.ToInt32(s.Substring(2, 2), 16),
                Convert.ToInt32(s.Substring(4, 2), 16));
        }

        public static double Parlaklik(DColor c)
        {
            static double Kanal(int v)
            {
                double s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Kanal(c.R)) + (0.7152 * Kanal(c.G)) + (0.0722 * Kanal(c.B));
        }

        public static bool AcikMi(DColor c) => Parlaklik(c) > 0.42;

        public static DColor Karistir(DColor a, DColor b, double t)
        {
            t = Math.Clamp(t, 0, 1);
            return DColor.FromArgb(255,
                (int)Math.Round(a.R + ((b.R - a.R) * t)),
                (int)Math.Round(a.G + ((b.G - a.G) * t)),
                (int)Math.Round(a.B + ((b.B - a.B) * t)));
        }

        public static DColor Alfa(DColor c, int a) => DColor.FromArgb(Math.Clamp(a, 0, 255), c.R, c.G, c.B);

        /// <summary>WCAG karşıtlık oranı (sınamanın kendi hesabı).</summary>
        public static double Karsitlik(DColor a, DColor b)
        {
            double la = Parlaklik(a);
            double lb = Parlaklik(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        public static int[] Tema(string zemin, string yaziKipi, string yazi, bool fotograf, bool otomatikYazi = false)
        {
            DColor background = Ayristir(zemin);
            bool photoMode = fotograf;
            bool manual = photoMode || (!otomatikYazi && yaziKipi == "Manual");

            DColor text;
            bool lightSurface;
            if (manual)
            {
                text = Ayristir(yazi);
                lightSurface = !AcikMi(text);
            }
            else
            {
                lightSurface = AcikMi(background);
                text = lightSurface ? DColor.FromArgb(0x1A, 0x1D, 0x21) : DColor.White;
            }

            DColor muted = lightSurface
                ? Karistir(text, photoMode ? DColor.White : background, 0.55)
                : Karistir(text, DColor.FromArgb(0x30, 0x34, 0x3A), 0.55);

            DColor holiday = lightSurface ? DColor.FromArgb(0xC0, 0x2B, 0x2B) : DColor.FromArgb(0xE4, 0x50, 0x50);

            DColor todayCircle = lightSurface
                ? Alfa(Karistir(background, DColor.Black, 0.18), 235)
                : Alfa(Karistir(background, DColor.White, 0.16), 235);

            if (photoMode)
            {
                todayCircle = lightSurface ? DColor.FromArgb(64, 0, 0, 0) : DColor.FromArgb(90, 255, 255, 255);
            }

            return new[] { background, text, muted, holiday, todayCircle, text }.Select(c => c.ToArgb()).ToArray();
        }
    }

    private static void Madde11()
    {
        Console.WriteLine("11) Açık/koyu kararı karşıtlığa göre; hazır renkler ve hazır fotoğraflar eskisi gibi");
        Type renkAraclari = Tip("Kalender.Rendering.ColorTools");

        // Hazır zemin renkleri (ayarlardaki örnekler) ve varsayılan zemin: eski hesapla yenisi birebir aynı.
        var hazirlar = ((IEnumerable)Tip("Kalender.Settings.ThemePresets").GetProperty("All", Hepsi)!.GetValue(null)!)
            .Cast<object>().Select(p => ((string)Oku(p, "Name")!, (string)Oku(p, "Color")!)).ToList();
        object varsayilan = Activator.CreateInstance(Tip("Kalender.Settings.AppearanceSettings"), true)!;
        hazirlar.Add(("varsayılan", (string)Oku(varsayilan, "SolidColor")!));

        var farklar = new List<string>();
        foreach ((string ad, string renk) in hazirlar)
        {
            DColor zemin = EskiRenk.Ayristir(renk);
            if (UygulamadaAcik(zemin) != EskiRenk.AcikMi(zemin))
            {
                farklar.Add(ad + ": örnekteki işaret");
            }

            int[] otomatik = EskiRenk.Tema(renk, "Auto", "#FFFFFF", fotograf: false);
            if (!YeniTema(YeniGorunum(renk), false).SequenceEqual(otomatik))
            {
                farklar.Add(ad + ": otomatik yazı");
            }

            // "Otomatik" kapatılınca ayarlar penceresi yazıyı o an hesaplanan renkte bırakıyor.
            string oto = Onaltilik(Renk(otomatik[1]));
            if (!YeniTema(YeniGorunum(renk, "Manual", oto), false).SequenceEqual(EskiRenk.Tema(renk, "Manual", oto, false)))
            {
                farklar.Add(ad + ": elle aynı yazı");
            }

            // Fotoğraf açılamayınca kalan düz zemin (yazı zeminden hesaplanıyor).
            if (!YeniTema(YeniGorunum(renk, "Manual", "#FFFFFF"), false, otomatikYazi: true)
                .SequenceEqual(EskiRenk.Tema(renk, "Manual", "#FFFFFF", false, otomatikYazi: true)))
            {
                farklar.Add(ad + ": açılamayan fotoğraf");
            }
        }

        Denetle($"hazır zemin renklerinin hepsinde ({hazirlar.Count - 1} hazır renk ve varsayılan) renkler ve örnek işareti eskisinin aynısı",
            hazirlar.Count >= 9 && farklar.Count == 0, string.Join(", ", farklar));

        // Hazır fotoğraf: seçilince fotoğrafta okunmayacak yazı beyaza çekiliyor (eskisi gibi),
        // sonra fotoğraf kipinin renkleri. Beyaz yazı (hazır fotoğrafların olağan hâli) dâhil 216 renk.
        var fotoFarklari = new List<string>();
        for (int r = 0; r <= 255; r += 51)
        {
            for (int g = 0; g <= 255; g += 51)
            {
                for (int b = 0; b <= 255; b += 51)
                {
                    DColor yazi = DColor.FromArgb(r, g, b);
                    string eskiYazi = EskiRenk.AcikMi(yazi) ? Onaltilik(yazi) : "#FFFFFF";
                    string yeniYazi = (bool)Statik(renkAraclari, "FotograftaOkunur", yazi)! ? Onaltilik(yazi) : "#FFFFFF";
                    if (eskiYazi != yeniYazi
                        || !YeniTema(YeniGorunum("#2E3238", "Auto", yeniYazi, true), true)
                            .SequenceEqual(EskiRenk.Tema("#2E3238", "Auto", eskiYazi, fotograf: true)))
                    {
                        fotoFarklari.Add(Onaltilik(yazi));
                    }
                }
            }
        }

        Denetle("hazır fotoğraf seçilince yazı rengi ve fotoğraf üstündeki renkler eskisi gibi (216 yazı rengi)",
            fotoFarklari.Count == 0, string.Join(", ", fotoFarklari.Take(8)));

        List<Komut> k = Komutlar(Tip("Kalender.Ui.SettingsWindow").GetMethod("OnReadyPhotoClicked", Hepsi)!);
        Denetle("hazır fotoğraf seçilince yazıya fotoğraftaki okunurluk sınırıyla bakılıyor",
            Bul(k, x => Cagri(x, renkAraclari, "FotograftaOkunur")) >= 0 && Bul(k, x => Cagri(x, renkAraclari, "IsLight")) < 0);

        // Eşik: siyah ve beyaz yazının karşıtlığı bağıl parlaklık ~0,179'da eşitleniyor.
        DColor acikGri = DColor.FromArgb(0x76, 0x76, 0x76);
        DColor koyuGri = DColor.FromArgb(0x75, 0x75, 0x75);
        Denetle("eşik karşıtlığa göre: #767676 açık, #757575 koyu (önceden ikisi de koyu)",
            UygulamadaAcik(acikGri) && !UygulamadaAcik(koyuGri),
            $"parlaklık {EskiRenk.Parlaklik(acikGri):0.0000} / {EskiRenk.Parlaklik(koyuGri):0.0000}");

        // Orta tonlu kendi renkleri: koyu yazı, zeminden ayrılan soluk günler ve tatiller.
        DColor beyaz = DColor.White;
        foreach (string renk in new[] { "#8C8C8C", "#E08A3C", "#4CAF50", "#5B9BD5" })
        {
            DColor zemin = EskiRenk.Ayristir(renk);
            int[] yeni = YeniTema(YeniGorunum(renk), false);
            int[] eski = EskiRenk.Tema(renk, "Auto", "#FFFFFF", false);
            double yazi = EskiRenk.Karsitlik(Renk(yeni[1]), zemin);
            double soluk = EskiRenk.Karsitlik(Renk(yeni[2]), zemin);
            double eskiSoluk = EskiRenk.Karsitlik(Renk(eski[2]), zemin);
            double tatil = EskiRenk.Karsitlik(Renk(yeni[3]), zemin);
            double eskiTatil = EskiRenk.Karsitlik(Renk(eski[3]), zemin);
            Denetle($"{renk}: yazı koyu ve beyazdan okunaklı ({yazi:0.0}:1, beyaz {EskiRenk.Karsitlik(beyaz, zemin):0.0}:1); "
                + $"soluk günler {soluk:0.00}:1 (önce {eskiSoluk:0.00}:1), tatil {tatil:0.00}:1 (önce {eskiTatil:0.00}:1)",
                yazi > EskiRenk.Karsitlik(beyaz, zemin) && yazi >= 4.5
                && soluk >= 1.8 && soluk > eskiSoluk && tatil >= 1.6 && tatil > eskiTatil);
        }

        // Koyu dalda soluk renk zemine doğru karıştırılıyor: eşiğin altındaki orta koyu zeminler.
        foreach (string renk in new[] { "#707070", "#4A5A6A" })
        {
            DColor zemin = EskiRenk.Ayristir(renk);
            int[] yeni = YeniTema(YeniGorunum(renk), false);
            int[] eski = EskiRenk.Tema(renk, "Auto", "#FFFFFF", false);
            double soluk = EskiRenk.Karsitlik(Renk(yeni[2]), zemin);
            double eskiSoluk = EskiRenk.Karsitlik(Renk(eski[2]), zemin);
            Denetle($"{renk} (koyu): yazı beyaz, soluk günler zeminden ayrılıyor ({soluk:0.00}:1, önce {eskiSoluk:0.00}:1)",
                yeni[1] == beyaz.ToArgb() && soluk >= 2.0 && soluk > eskiSoluk);
        }
    }

    private static void Madde10()
    {
        Console.WriteLine("10) Elle seçilen yazı renginde zeminin açıklığı zeminden; bugünün dairesi koyu zeminde görünüyor");
        var kalanlar = new List<string>();
        foreach (string zeminHex in new[] { "#000000", "#0B0E13", "#141A22", "#2E3238" })
        {
            DColor zemin = EskiRenk.Ayristir(zeminHex);
            foreach (string yazi in new[] { "#4CAF50", "#42A5F5", "#FF7043" })
            {
                int[] t = YeniTema(YeniGorunum(zeminHex, "Manual", yazi), false);
                DColor daire = Renk(t[4]);
                double daireK = EskiRenk.Karsitlik(DColor.FromArgb(daire.R, daire.G, daire.B), zemin);
                double tatilK = EskiRenk.Karsitlik(Renk(t[3]), zemin);
                if (Renk(t[1]).ToArgb() != EskiRenk.Ayristir(yazi).ToArgb()
                    || EskiRenk.Parlaklik(daire) <= EskiRenk.Parlaklik(zemin) || daireK < 1.4 || tatilK < 3)
                {
                    kalanlar.Add($"{zeminHex}+{yazi} (daire {daireK:0.00}:1, tatil {tatilK:0.0}:1)");
                }
            }
        }

        Denetle("koyu hazır zeminlerde (Siyah, Kuzgun, Mürekkep, Kömür) elle yeşil, mavi, turuncu yazıyla bugünün dairesi "
            + "zeminden açık, tatil koyu zeminin kırmızısı; yazı seçilen renk", kalanlar.Count == 0, string.Join("; ", kalanlar));

        // Açık zeminde elle orta tonlu ya da koyu yazı eskisi gibi (zemin açık, yazı da koyu sayılıyordu).
        bool acikAyni = new[] { ("#F2F0EC", "#4CAF50"), ("#D9CFC1", "#1A1D21"), ("#C8BBA8", "#42A5F5") }
            .All(d => YeniTema(YeniGorunum(d.Item1, "Manual", d.Item2), false).SequenceEqual(EskiRenk.Tema(d.Item1, "Manual", d.Item2, false)));
        Denetle("açık hazır zeminlerde elle seçilen orta tonlu ya da koyu yazının renkleri eskisi gibi", acikAyni);

        // Fotoğraf kipinde zemin bilinmiyor; yazıdan çıkarım sürüyor, ama karşıtlık eşiğiyle.
        int[] foto = YeniTema(YeniGorunum("#2E3238", "Auto", "#4CAF50", true), true);
        Denetle("fotoğrafta orta tonlu (yeşil) yazı açık sayılıyor: daire beyaz (alfa 90), tatil açık kırmızı",
            foto[4] == DColor.FromArgb(90, 255, 255, 255).ToArgb() && foto[3] == DColor.FromArgb(0xE4, 0x50, 0x50).ToArgb(),
            $"daire {Renk(foto[4])}");
        int[] fotoKoyu = YeniTema(YeniGorunum("#2E3238", "Auto", "#1A1D21", true), true);
        Denetle("fotoğrafta koyu yazı eskisi gibi (daire siyah, alfa 64)",
            fotoKoyu.SequenceEqual(EskiRenk.Tema("#2E3238", "Auto", "#1A1D21", fotograf: true)));

        // Masaüstü çiziminde: Siyah zeminde elle yeşil yazıyla bugünün dairesi (koyu gri) çiziliyor.
        object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;
        object gorunum = Oku(ayarlar, "Appearance")!;
        Yaz(gorunum, "SolidColor", "#000000");
        Yaz(gorunum, "PresetId", "siyah");
        Yaz(gorunum, "TextColorMode", Secenek("TextColorMode", "Manual"));
        Yaz(gorunum, "TextColor", "#4CAF50");
        (DBitmap resim, DRectangleF blok) = MasaustuCizimi(ayarlar, TasimaYeri(0.5, 0.25), 2560, 1440);
        using (resim)
        {
            int gri = 0;
            for (int y = (int)blok.Top; y < (int)blok.Bottom; y++)
            {
                for (int x = (int)blok.Left; x < (int)blok.Right; x++)
                {
                    DColor c = resim.GetPixel(x, y);
                    if (c.R is >= 30 and <= 52 && Math.Abs(c.R - c.G) <= 3 && Math.Abs(c.G - c.B) <= 3)
                    {
                        gri++;
                    }
                }
            }

            Denetle("Siyah zeminde elle yeşil yazıyla masaüstünde bugünün dairesi görünüyor", gri > 500, $"{gri} daire pikseli");
        }
    }

    private static void Madde12()
    {
        Console.WriteLine("12) Kapla ve Sığdır'da fotoğrafın kenar sırası fotoğrafın renginde; zemin sızmıyor");
        object ressam = Activator.CreateInstance(Tip("Kalender.Rendering.BackgroundPainter"), true)!;
        object tema = Statik(Tip("Kalender.Rendering.Theme"), "ZemineGore", YeniGorunum("#2E3238", fotograf: true), true, false)!;
        DColor zemin = (DColor)Oku(tema, "Background")!;
        MethodInfo ciz = ressam.GetType().GetMethod("Paint")!;
        const int G = 384;
        const int Y = 216;

        foreach ((string ad, int fg, int fy, string kip) in new[]
        {
            ("Kapla, küçültme (3840 × 2160 fotoğraf 1920 × 1080 ekranda gibi)", 768, 432, "Fill"),
            ("Kapla, büyütme", 256, 144, "Fill"),
            ("Kapla, 3:2 fotoğraf genişliğe oturuyor", 300, 200, "Fill"),
            ("Sığdır, fotoğraf genişliğe oturuyor, üstte ve altta zemin", 400, 200, "Fit"),
        })
        {
            using var foto = new DBitmap(fg, fy, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (DGraphics c = DGraphics.FromImage(foto))
            {
                c.Clear(DColor.White);
            }

            using var yuzey = new DBitmap(G, Y, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (DGraphics c = DGraphics.FromImage(yuzey))
            {
                ciz.Invoke(ressam, new object[] { c, new DRectangle(0, 0, G, Y), tema, foto, Secenek("PhotoFit", kip) });
            }

            // Fotoğrafın yüzeydeki dikdörtgeni (Sığdır'da ortada, kenarlarda zemin).
            DRectangle alan = new(0, 0, G, Y);
            if (kip == "Fit")
            {
                double s = Math.Min((double)G / fg, (double)Y / fy);
                int w = (int)Math.Round(fg * s);
                int h = (int)Math.Round(fy * s);
                alan = new DRectangle((G - w) / 2, (Y - h) / 2, w, h);
            }

            int enKoyu = 255;
            for (int x = alan.Left; x < alan.Right; x++)
            {
                enKoyu = Math.Min(enKoyu, Math.Min(yuzey.GetPixel(x, alan.Top).G, yuzey.GetPixel(x, alan.Bottom - 1).G));
            }

            for (int y = alan.Top; y < alan.Bottom; y++)
            {
                enKoyu = Math.Min(enKoyu, Math.Min(yuzey.GetPixel(alan.Left, y).G, yuzey.GetPixel(alan.Right - 1, y).G));
            }

            bool disari = true;
            if (kip == "Fit")
            {
                for (int x = 0; x < G; x++)
                {
                    disari &= yuzey.GetPixel(x, alan.Top - 1).ToArgb() == zemin.ToArgb()
                        && yuzey.GetPixel(x, alan.Bottom).ToArgb() == zemin.ToArgb();
                }
            }

            Denetle($"{ad}: kenar sırası beyaz (en koyu {enKoyu}){(kip == "Fit" ? ", dışı zemin renginde" : "")}",
                enKoyu >= 254 && disari);
        }

        ((IDisposable)ressam).Dispose();
    }

    // ------------------------------------------------------- "Takvimi taşı" ekranı

    /// <summary>Hayali bir ekran (%100 ölçekli, gerçek ekrana bakılmaz).</summary>
    private static object TasimaEkrani(int g, int y)
    {
        object m = Activator.CreateInstance(Tip("Kalender.Interop.MonitorTarget"), true)!;
        Yaz(m, "Handle", IntPtr.Zero);
        Yaz(m, "DeviceName", @"\\.\DENEME");
        Yaz(m, "StableId", "DENEME");
        Yaz(m, "Bounds", new DRectangle(0, 0, g, y));
        Yaz(m, "WorkArea", new DRectangle(0, 0, g, y - 48));
        Yaz(m, "Dpi", 96u);
        Yaz(m, "IsPrimary", true);
        return m;
    }

    private static object TasimaYeri(double x, double y, int yuzde = 100)
    {
        object yer = Activator.CreateInstance(Tip("Kalender.Settings.MonitorPlacement"), true)!;
        Yaz(yer, "XRatio", x);
        Yaz(yer, "YRatio", y);
        Yaz(yer, "ScalePercent", yuzde);
        return yer;
    }

    /// <summary>
    /// "Takvimi taşı" penceresi. Gösterilmez: tanıtıcısı yok, ekrana çıkmaz.
    /// Açılış, pencere kurulunca çağrılan yöntemle (OnSourceInitialized) yapılır.
    /// </summary>
    private static System.Windows.Window TasimaPenceresi(object ekran, object ayarlar, object yer, bool ac = true)
    {
        var pencere = (System.Windows.Window)Activator.CreateInstance(Tip("Kalender.Ui.PlacementOverlayWindow"),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { ekran, ayarlar, yer, BosVeri(), RenkGunu }, null)!;
        if (ac)
        {
            // Window'un kendi OnSourceInitialized'ı da var; pencerenin olay işleyicisi çağrılıyor.
            pencere.GetType().GetMethod("OnSourceInitialized", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                new[] { typeof(object), typeof(EventArgs) })!.Invoke(pencere, new object?[] { null, EventArgs.Empty });
        }

        return pencere;
    }

    private static void TasimaPenceresiniBirak(System.Windows.Window pencere)
        => ((IDisposable)AlanOku(pencere, "_renderer")).Dispose();

    private static DPointF TakvimYeri(System.Windows.Window pencere) => (DPointF)AlanOku(pencere, "_origin");

    /// <summary>
    /// "Kaydet"e basılmış gibi sonucu yazar. Pencere iletişim kutusu olarak
    /// açılmadığı için sondaki DialogResult ataması hata veriyor; oranlar ondan
    /// önce yazılmış oluyor.
    /// </summary>
    private static object TasimayiKaydet(System.Windows.Window pencere)
    {
        try
        {
            Cagir(pencere, "Save");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            // Beklenen: pencere ShowDialog ile açılmadı.
        }

        return Oku(pencere, "Result")!;
    }

    /// <summary>Masaüstü katmanının ürettiği görüntü ve takvim bloğunun yeri.</summary>
    private static (DBitmap Resim, DRectangleF Blok) MasaustuCizimi(object ayarlar, object yer, int g, int y)
    {
        object cizici = Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"), true)!;
        try
        {
            object yuzey = cizici.GetType().GetMethod("Render")!.Invoke(cizici, new[]
            {
                new DSize(g, y), ayarlar, yer, BosVeri(), RenkGunu, RenkGunu, true,
            })!;
            return ((DBitmap)Oku(yuzey, "Image")!, (DRectangleF)Oku(Oku(yuzey, "Layout")!, "Bounds")!);
        }
        finally
        {
            ((IDisposable)cizici).Dispose();
        }
    }

    private static bool Yakin(DPointF a, DPointF b) => Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01;

    private static void Madde8()
    {
        Console.WriteLine("8) \"Takvimi taşı\" ekranı takvimi masaüstündeki çizimle aynı kenar payında tutuyor");
        foreach ((int g, int y, int yuzde) in new[] { (2560, 1440, 100), (3840, 2160, 150), (1920, 1080, 80) })
        {
            object ekran = TasimaEkrani(g, y);
            object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;
            System.Windows.Window pencere = TasimaPenceresi(ekran, ayarlar, TasimaYeri(0.5, 0.5, yuzde));
            float olcek = (float)Oku(pencere, "TotalScale")!;
            float pay = 12f * olcek;
            var blok = (DSizeF)AlanOku(pencere, "_blockSize");
            var sagAltSiniri = new DPointF(g - blok.Width - pay, y - blok.Height - pay);

            Cagir(pencere, "MoveTo", new DPointF(-5000, -5000), true);
            DPointF solUst = TakvimYeri(pencere);
            Cagir(pencere, "MoveTo", new DPointF(50000, 50000), true);
            DPointF sagAlt = TakvimYeri(pencere);
            Cagir(pencere, "MoveTo", new DPointF(-5000, 50000), false);
            DPointF solAlt = TakvimYeri(pencere);
            Denetle($"{g}×{y} %{yuzde}: kenara sürüklenen takvim kenardan {pay:0.#} px içeride duruyor (sürüklerken ve boyut değişince)",
                Yakin(solUst, new DPointF(pay, pay)) && Yakin(sagAlt, sagAltSiniri) && Yakin(solAlt, new DPointF(pay, sagAltSiniri.Y)),
                $"sol üst {solUst}, sağ alt {sagAlt}, sol alt {solAlt}");

            // Yapışma çizgileri (ekranın %6'sı) eskisi gibi.
            var cizgi = new DPointF((float)(g * 0.06), (float)(y * 0.06));
            Cagir(pencere, "MoveTo", new DPointF(cizgi.X + 10, cizgi.Y - 10), true);
            Denetle($"{g}×{y} %{yuzde}: kenar yakınındaki yapışma çizgisi eskisi gibi", Yakin(TakvimYeri(pencere), cizgi),
                TakvimYeri(pencere).ToString());

            // Gidiş-dönüş: kaydedilen oranla yeniden açılan pencere ve masaüstü aynı yeri gösteriyor.
            var kalanlar = new List<string>();
            foreach (DPointF hedef in new[] { new DPointF(50000, 50000), new DPointF(-5000, -5000), new DPointF(g * 0.37f, y * 0.41f) })
            {
                Cagir(pencere, "MoveTo", hedef, false);
                DPointF birakilan = TakvimYeri(pencere);
                object sonuc = TasimayiKaydet(pencere);
                System.Windows.Window ikinci = TasimaPenceresi(ekran, ayarlar, sonuc);
                DPointF acilis = TakvimYeri(ikinci);
                TasimaPenceresiniBirak(ikinci);
                (DBitmap resim, DRectangleF masaustu) = MasaustuCizimi(ayarlar, sonuc, g, y);
                resim.Dispose();
                if (!Yakin(acilis, birakilan) || !Yakin(masaustu.Location, birakilan))
                {
                    kalanlar.Add($"bırakılan {birakilan}, yeniden açılınca {acilis}, masaüstünde {masaustu.Location}");
                }
            }

            Denetle($"{g}×{y} %{yuzde}: kaydedilen yer yeniden açılınca ve masaüstünde aynı (kenarlarda ve ortada)",
                kalanlar.Count == 0, string.Join("; ", kalanlar));
            TasimaPenceresiniBirak(pencere);
        }

        // Yapıştırıcı da aynı payı kullanıyor (ekran dışına itilen öneri).
        Type yapistirici = Tip("Kalender.Settings.PlacementSnapper");
        MethodInfo yapistir = yapistirici.GetMethod("Snap", Hepsi)!;
        object?[] a = { new DPointF(-300, 99999), new DSizeF(700, 704), new DSize(2560, 1440), 1.5f, null };
        var sonucNokta = (DPointF)yapistir.Invoke(null, a)!;
        Denetle("yapıştırıcı ekran dışına itilen takvimi payın (12 × ölçek) çizgisinde tutuyor",
            Yakin(sonucNokta, new DPointF(18, 1440 - 704 - 18)), sonucNokta.ToString());
    }

    private static void Madde9()
    {
        Console.WriteLine("9) \"Takvimi taşı\" ekranında fotoğraf karartması masaüstündekiyle aynı, takvimle birlikte taşınıyor");
        string klasor = Path.Combine(Path.GetTempPath(), "Kalender ikinci inceleme karartma " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        try
        {
            DColor fotoRengi = DColor.FromArgb(0xB4, 0xB4, 0xB4);
            string foto = Path.Combine(klasor, "duz-gri.png");
            using (var resim = new DBitmap(640, 360, System.Drawing.Imaging.PixelFormat.Format24bppRgb))
            {
                using (DGraphics c = DGraphics.FromImage(resim))
                {
                    c.Clear(fotoRengi);
                }

                resim.Save(foto, System.Drawing.Imaging.ImageFormat.Png);
            }

            object Ayarlar(int karartma, bool fotografli = true)
            {
                object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;
                object gorunum = Oku(ayarlar, "Appearance")!;
                Yaz(gorunum, "BackgroundMode", Secenek("BackgroundMode", fotografli ? "Photo" : "Solid"));
                Yaz(gorunum, "PhotoPath", fotografli ? foto : null);
                Yaz(gorunum, "BackdropDim", karartma);
                return ayarlar;
            }

            // Masaüstü görüntüsü = zemin + taşıma ekranının karartma görüntüsü (takvim dışındaki her yerde).
            object yer = TasimaYeri(0.5, 0.25);
            foreach (int karartma in new[] { 30, 60, 100 })
            {
                object ayarlar = Ayarlar(karartma);
                object cizici = Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"), true)!;
                (DBitmap masaustu, DRectangleF blok) = MasaustuCizimi(ayarlar, yer, 2560, 1440);
                using (masaustu)
                using (var zemin = (DBitmap)cizici.GetType().GetMethod("RenderBackgroundOnly")!.Invoke(cizici, new[] { new DSize(2560, 1440), ayarlar, yer })!)
                {
                    object?[] a = { 1f, ayarlar, yer, null };
                    using var karartmaResmi = (DBitmap?)cizici.GetType().GetMethod("KarartmaYuzeyi")!.Invoke(cizici, a);
                    if (karartmaResmi is null)
                    {
                        Denetle($"%{karartma} karartma: taşıma ekranı için karartma çiziliyor", false);
                        ((IDisposable)cizici).Dispose();
                        continue;
                    }

                    var kayma = (DPointF)a[3]!;
                    var kare = new DRectangle((int)Math.Round(blok.X + kayma.X), (int)Math.Round(blok.Y + kayma.Y),
                        karartmaResmi.Width, karartmaResmi.Height);
                    using (DGraphics c = DGraphics.FromImage(zemin))
                    {
                        c.DrawImage(karartmaResmi, kare);
                    }

                    DRectangle takvim = DRectangle.Round(blok);
                    takvim.Inflate(3, 3);
                    DRectangle bakilan = kare;
                    bakilan.Inflate(12, 12);
                    int enCokFark = 0;
                    int enKoyu = 255;
                    int bakilanSayisi = 0;
                    for (int y = bakilan.Top; y < bakilan.Bottom; y += 3)
                    {
                        for (int x = bakilan.Left; x < bakilan.Right; x += 3)
                        {
                            if (takvim.Contains(x, y))
                            {
                                continue;
                            }

                            DColor m = masaustu.GetPixel(x, y);
                            DColor o = zemin.GetPixel(x, y);
                            enCokFark = Math.Max(enCokFark, Math.Max(Math.Abs(m.R - o.R), Math.Max(Math.Abs(m.G - o.G), Math.Abs(m.B - o.B))));
                            enKoyu = Math.Min(enKoyu, m.R);
                            bakilanSayisi++;
                        }
                    }

                    Denetle($"%{karartma} karartma: taşıma ekranındaki karartma masaüstündekiyle aynı boyut ve renkte "
                        + $"(kare {karartmaResmi.Width} px, en çok fark {enCokFark}, en koyu {enKoyu})",
                        karartmaResmi.Width == 816 && karartmaResmi.Height == 816 && enCokFark <= 3
                        && enKoyu < fotoRengi.R - 40 && bakilanSayisi > 10000);
                }

                ((IDisposable)cizici).Dispose();
            }

            // Pencerede: karartma takvimin altında, ortalı; takvimle birlikte taşınıyor.
            object ekran = TasimaEkrani(2560, 1440);
            System.Windows.Window pencere = TasimaPenceresi(ekran, Ayarlar(60), yer);
            var karartmaGoruntusu = (System.Windows.Controls.Image)pencere.FindName("BackdropImage");
            var takvimGoruntusu = (System.Windows.Controls.Image)pencere.FindName("CalendarImage");
            var cerceve = (System.Windows.Controls.Border)pencere.FindName("CalendarHost");
            var sahne = (System.Windows.Controls.Canvas)pencere.FindName("Stage");

            (double X, double Y) Kayma()
            {
                double tx = System.Windows.Controls.Canvas.GetLeft(cerceve) + cerceve.BorderThickness.Left + (takvimGoruntusu.Width / 2);
                double ty = System.Windows.Controls.Canvas.GetTop(cerceve) + cerceve.BorderThickness.Top + (takvimGoruntusu.Height / 2);
                double kx = System.Windows.Controls.Canvas.GetLeft(karartmaGoruntusu) + (karartmaGoruntusu.Width / 2);
                double ky = System.Windows.Controls.Canvas.GetTop(karartmaGoruntusu) + (karartmaGoruntusu.Height / 2);
                return (kx - tx, ky - ty);
            }

            (double X, double Y) once = Kayma();
            int sira = sahne.Children.IndexOf(karartmaGoruntusu);
            Denetle("pencerede karartma çiziliyor, takvimin altında ve takvimi ortalıyor",
                karartmaGoruntusu.Source is not null && karartmaGoruntusu.Width == 816
                && sira > sahne.Children.IndexOf((System.Windows.UIElement)pencere.FindName("BackgroundImage"))
                && sira < sahne.Children.IndexOf(cerceve) && Math.Abs(once.X) < 0.51 && Math.Abs(once.Y) < 0.51,
                $"merkezler arası {once}");

            Cagir(pencere, "MoveTo", new DPointF(400, 300), true);
            (double X, double Y) sonra = Kayma();
            Denetle("takvim sürüklenince karartma da birlikte taşınıyor",
                karartmaGoruntusu.Source is not null && Math.Abs(sonra.X - once.X) < 0.01 && Math.Abs(sonra.Y - once.Y) < 0.01 && Yakin(TakvimYeri(pencere), new DPointF(400, 300)),
                $"merkezler arası {sonra}");

            ((System.Windows.Controls.Slider)pencere.FindName("ScaleSlider")).Value = 130;
            (double X, double Y) buyuyunce = Kayma();
            Denetle("boyut değişince karartma da büyüyüp ortada kalıyor",
                karartmaGoruntusu.Width > 816 && Math.Abs(buyuyunce.X) < 0.51 && Math.Abs(buyuyunce.Y) < 0.51,
                $"{karartmaGoruntusu.Width} px, merkezler arası {buyuyunce}");
            TasimaPenceresiniBirak(pencere);

            foreach ((object ayarlar, string ad) in new[] { (Ayarlar(0), "karartma %0"), (Ayarlar(60, fotografli: false), "düz renk zemin") })
            {
                System.Windows.Window p = TasimaPenceresi(ekran, ayarlar, yer);
                Denetle($"{ad}: pencerede karartma yok", ((System.Windows.Controls.Image)p.FindName("BackdropImage")).Source is null);
                TasimaPenceresiniBirak(p);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(klasor, recursive: true);
            }
            catch (IOException)
            {
                // Silinemezse geçici klasörde kalır.
            }
        }
    }

    // ------------------ ayarlar penceresi: zemin, fotoğraf ve renk seçimleri (13–16)

    private const string Ekran1 = @"\\.\DISPLAY1";
    private const string Ekran2 = @"\\.\DISPLAY2";

    /// <summary>13. maddede ikinci ekrana verilen koyu deneme fotoğrafının rengi.</summary>
    private static readonly DColor KoyuFoto = DColor.FromArgb(0x28, 0x30, 0x38);

    /// <summary>14 ve 15. maddelerde ikinci ekrana verilen deneme fotoğrafının rengi.</summary>
    private static readonly DColor YesilFoto = DColor.FromArgb(0x2E, 0x7D, 0x32);

    /// <summary>Genel (bütün ekranlara çizilen) deneme fotoğrafının rengi.</summary>
    private static readonly DColor TuruncuFoto = DColor.FromArgb(0xC0, 0x60, 0x20);

    /// <summary>Geçici klasörde düz renkli bir deneme fotoğrafı (PNG, 64 × 36).</summary>
    private static string DenemeFotografi(string klasor, string ad, DColor renk)
    {
        string yol = Path.Combine(klasor, ad);
        using var resim = new DBitmap(64, 36, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (DGraphics c = DGraphics.FromImage(resim))
        {
            c.Clear(renk);
        }

        resim.Save(yol, System.Drawing.Imaging.ImageFormat.Png);
        return yol;
    }

    /// <summary>Deneme fotoğrafları için geçici bir klasörle çalıştırır; sonunda klasör silinir.</summary>
    private static void FotografKlasoruyle(Action<string> islem)
    {
        string klasor = Path.Combine(Path.GetTempPath(), "Kalender ikinci inceleme zemin " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(klasor);
        try
        {
            islem(klasor);
        }
        finally
        {
            try
            {
                Directory.Delete(klasor, recursive: true);
            }
            catch (IOException)
            {
                // Silinemezse geçici klasörde kalır.
            }
        }
    }

    /// <summary>
    /// Ayarları bellekte tutan bir depo (KodIncelemesi'ndeki gibi; sınanan
    /// işleyiciler onu kaydetmez). Genel zemin <paramref name="kip"/> kipinde,
    /// düz rengi <paramref name="renk"/>; <paramref name="genel"/> genel fotoğraf,
    /// <paramref name="ekran2"/> ikinci ekranın kendi fotoğrafı. Yazı rengi
    /// otomatik, fotoğraf için seçilen yazı rengi beyaz (varsayılan). Açılışta
    /// başlatma kapalı: pencerenin açılış notu kayıt defterine bakmasın.
    /// </summary>
    private static object ZeminDeposu(string kip, string renk, string? hazir, string? genel = null, string? ekran2 = null)
    {
        object depo = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Settings.SettingsStore"));
        AlanAta(depo, "_gate", new object());

        object ayarlar = Activator.CreateInstance(Tip("Kalender.Settings.AppSettings"), true)!;
        Yaz(ayarlar, "StartWithWindows", false);
        object g = Oku(ayarlar, "Appearance")!;
        Yaz(g, "BackgroundMode", Secenek("BackgroundMode", kip));
        Yaz(g, "SolidColor", renk);
        Yaz(g, "PresetId", hazir);
        Yaz(g, "PhotoPath", genel);
        Yaz(g, "TextColorMode", Secenek("TextColorMode", "Auto"));
        Yaz(g, "TextColor", "#FFFFFF");

        if (ekran2 is not null)
        {
            object yer = Activator.CreateInstance(Tip("Kalender.Settings.MonitorPlacement"), true)!;
            Yaz(yer, "PhotoPath", ekran2);
            ((IDictionary)Oku(ayarlar, "Monitors")!)[Ekran2] = yer;
        }

        Yaz(depo, "Current", ayarlar);
        return depo;
    }

    private static object Gorunum(object depo) => Oku(Oku(depo, "Current")!, "Appearance")!;

    private static IDictionary Yerlesimler(object depo) => (IDictionary)Oku(Oku(depo, "Current")!, "Monitors")!;

    /// <summary>Hayali ekran (1920 × 1080, %100 ölçek; gerçek ekrana bakılmaz).</summary>
    private static object ZeminEkrani(string aygit, int x)
    {
        object m = Activator.CreateInstance(Tip("Kalender.Interop.MonitorTarget"), true)!;
        Yaz(m, "Handle", IntPtr.Zero);
        Yaz(m, "DeviceName", aygit);
        Yaz(m, "StableId", aygit);
        Yaz(m, "KimlikOkunamadi", true);
        Yaz(m, "Bounds", new DRectangle(x, 0, 1920, 1080));
        Yaz(m, "WorkArea", new DRectangle(x, 0, 1920, 1032));
        Yaz(m, "Dpi", 96u);
        Yaz(m, "IsPrimary", x == 0);
        return m;
    }

    /// <summary>
    /// Kurucusu çalıştırılmamış bir ayarlar penceresi (KodIncelemesi'ndeki gibi):
    /// ekranda açılmaz. Adlı denetimlerinin hepsi boş kurulur, XAML'daki olay
    /// bağlantıları yoktur; otomatik anahtarlarının bağlantıları kurucudaki
    /// gibi kurulur. Hazır renk ve hazır fotoğraf örnekleri pencerenin kurduğu
    /// gibi etiketlenir. Uygulamaya açılan kapı sahte (duvar kâğıdına yazılmaz),
    /// iki hayali ekran bağlı. Sonunda kurucu gibi ekran satırlarını kurar ve
    /// ayarları denetimlere yükler.
    /// </summary>
    private static object ZeminPenceresi(object depo)
    {
        Type tip = Tip("Kalender.Ui.SettingsWindow");
        object pencere = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(tip);
        typeof(DispatcherObject).GetField("_dispatcher", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(pencere, Dispatcher.CurrentDispatcher);

        var kapi = (SahteAyarKapisi)DispatchProxy.Create(Tip("Kalender.Ui.IAppearanceHost"), typeof(SahteAyarKapisi));
        var ekranlar = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Interop.MonitorTarget")))!;
        ekranlar.Add(ZeminEkrani(Ekran1, 0));
        ekranlar.Add(ZeminEkrani(Ekran2, 1920));
        kapi.Ayarlar = depo;
        kapi.Ekranlar = ekranlar;

        AlanAta(pencere, "_host", kapi);
        AlanAta(pencere, "_applyTimer", new DispatcherTimer { Interval = TimeSpan.FromHours(1) });
        AlanAta(pencere, "_rows", Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Ui.MonitorRow")))!);

        foreach (FieldInfo alan in tip.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!alan.Name.StartsWith('_') && typeof(System.Windows.DependencyObject).IsAssignableFrom(alan.FieldType)
                && alan.FieldType.GetConstructor(Type.EmptyTypes) is not null)
            {
                alan.SetValue(pencere, Activator.CreateInstance(alan.FieldType));
            }
        }

        var hazirRenkler = (System.Windows.Controls.Panel)AlanOku(pencere, "PresetPanel");
        foreach (object hazir in (IEnumerable)Tip("Kalender.Settings.ThemePresets").GetProperty("All", Hepsi)!.GetValue(null)!)
        {
            hazirRenkler.Children.Add(new System.Windows.Controls.RadioButton { Tag = hazir });
        }

        var hazirFotograflar = (System.Windows.Controls.Panel)AlanOku(pencere, "ReadyPhotoPanel");
        foreach (object foto in (IEnumerable)Tip("Kalender.Settings.HazirFotograflar").GetProperty("All", Hepsi)!.GetValue(null)!)
        {
            hazirFotograflar.Children.Add(new System.Windows.Controls.RadioButton { Tag = foto });
        }

        // Takvim renklerinin satırları ve anahtar bağlantıları, kurucudaki gibi.
        Type satirTipi = tip.GetNestedType("CalendarColorRow", BindingFlags.NonPublic)!;
        Type renkler = Tip("Kalender.Settings.CalendarTextColors");
        Type tema = Tip("Kalender.Rendering.Theme");
        MethodInfo otomatik = tip.GetMethod("SetCalendarColorAuto", Hepsi)!;
        (string Kutu, string Dugme, string Ayar, string Hesap)[] tanimlar =
        {
            ("WeekdayAutoBox", "WeekdayColorButton", "Weekday", "Text"),
            ("HolidayAutoBox", "HolidayColorButton", "Holiday", "Holiday"),
            ("MutedAutoBox", "MutedColorButton", "Muted", "Muted"),
            ("TodayCircleAutoBox", "TodayCircleColorButton", "TodayCircle", "TodayCircle"),
        };

        Array satirlar = Array.CreateInstance(satirTipi, tanimlar.Length);
        for (int i = 0; i < tanimlar.Length; i++)
        {
            var k = Ifade.Parameter(renkler, "k");
            var v = Ifade.Parameter(typeof(string), "v");
            var t = Ifade.Parameter(tema, "t");
            Delegate oku = Ifade.Lambda(typeof(Func<,>).MakeGenericType(renkler, typeof(string)),
                Ifade.Property(k, tanimlar[i].Ayar), k).Compile();
            Delegate yaz = Ifade.Lambda(typeof(Action<,>).MakeGenericType(renkler, typeof(string)),
                Ifade.Assign(Ifade.Property(k, tanimlar[i].Ayar), v), k, v).Compile();
            Delegate hesap = Ifade.Lambda(typeof(Func<,>).MakeGenericType(tema, typeof(DColor)),
                Ifade.Property(t, tanimlar[i].Hesap), t).Compile();

            var kutu = (System.Windows.Controls.CheckBox)AlanOku(pencere, tanimlar[i].Kutu);
            object satir = Activator.CreateInstance(satirTipi, kutu, AlanOku(pencere, tanimlar[i].Dugme), oku, yaz, hesap)!;
            System.Windows.RoutedEventHandler isleyici = (_, _) => otomatik.Invoke(pencere, new[] { satir });
            kutu.Checked += isleyici;
            kutu.Unchecked += isleyici;
            satirlar.SetValue(satir, i);
        }

        AlanAta(pencere, "_calendarColors", satirlar);

        // Yazı renginin otomatik anahtarı XAML'da bağlı.
        var yaziKutusu = (System.Windows.Controls.CheckBox)AlanOku(pencere, "AutoTextColorBox");
        System.Windows.RoutedEventHandler yaziIsleyici = (s, e) => Cagir(pencere, "OnAutoTextColorChanged", s, e);
        yaziKutusu.Checked += yaziIsleyici;
        yaziKutusu.Unchecked += yaziIsleyici;

        Cagir(pencere, "BuildMonitorRows");
        Cagir(pencere, "LoadFromSettings");
        return pencere;
    }

    private static System.Windows.Controls.CheckBox Kutu(object pencere, string ad)
        => (System.Windows.Controls.CheckBox)AlanOku(pencere, ad);

    private static string Metin(object pencere, string ad) => ((System.Windows.Controls.TextBlock)AlanOku(pencere, ad)).Text;

    /// <summary>Renk düğmesinde görünen renk.</summary>
    private static DColor Dugme(object pencere, string ad)
    {
        var c = (System.Windows.Media.Color)Oku(AlanOku(pencere, ad), "SelectedColor")!;
        return DColor.FromArgb(c.R, c.G, c.B);
    }

    /// <summary>Panelde işaretli örneğin kimliği (hazır renk ya da hazır fotoğraf); yoksa null.</summary>
    private static string? Isaretli(object pencere, string panel)
        => ((System.Windows.Controls.Panel)AlanOku(pencere, panel)).Children.OfType<System.Windows.Controls.RadioButton>()
            .Where(r => r.IsChecked == true).Select(r => (string?)Oku(r.Tag, "Id")).FirstOrDefault();

    /// <summary>Hazır renklerden birine tıklanmış gibi.</summary>
    private static void HazirRenkSec(object pencere, string kimlik)
    {
        System.Windows.Controls.RadioButton ornek = ((System.Windows.Controls.Panel)AlanOku(pencere, "PresetPanel")).Children
            .OfType<System.Windows.Controls.RadioButton>().First(r => (string?)Oku(r.Tag, "Id") == kimlik);
        Cagir(pencere, "OnPresetClicked", ornek, new System.Windows.RoutedEventArgs());
    }

    /// <summary>Renk çarkından renk seçilmiş gibi (zemin ya da yazı rengi işleyicisi).</summary>
    private static void RenkCarkindan(object pencere, string isleyici, string renk)
    {
        DColor c = EskiRenk.Ayristir(renk);
        Cagir(pencere, isleyici, System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
    }

    /// <summary>Boyut ve Konum sayfasındaki ekran satırı.</summary>
    private static object Satir(object pencere, string aygit)
        => ((IList)AlanOku(pencere, "_rows")).Cast<object>().First(s => (string?)Oku(Oku(s, "Monitor")!, "StableId") == aygit);

    /// <summary>
    /// "Bu ekrana fotoğraf…": dosya seçildikten sonraki adımlar, OnPickMonitorPhoto'daki
    /// sırayla (seçme penceresi açılmaz; sıranın aynı olduğu derlenmiş koddan denetlenir).
    /// </summary>
    private static void EkranaFotografVer(object pencere, object depo, string aygit, string yol)
    {
        object yer = Oku(Satir(pencere, aygit), "Placement")!;
        Yaz(yer, "PhotoPath", yol);
        Yerlesimler(depo)[aygit] = yer;
        Cagir(pencere, "EkranFotografiVerildi");
    }

    /// <summary>Ekran satırındaki "Kaldır".</summary>
    private static void EkranFotografiniKaldir(object pencere, string aygit)
        => Cagir(pencere, "OnRemoveMonitorPhoto", new System.Windows.Controls.Button { Tag = Satir(pencere, aygit) },
            new System.Windows.RoutedEventArgs());

    /// <summary>Masaüstü katmanının o ekrana çizdiği zeminin ortasındaki renk (takvimsiz, küçük yüzeyde).</summary>
    private static DColor ZeminPikseli(object depo, string aygit)
    {
        IDictionary yerler = Yerlesimler(depo);
        object yer = yerler.Contains(aygit) ? yerler[aygit]! : Activator.CreateInstance(Tip("Kalender.Settings.MonitorPlacement"), true)!;
        object cizici = Activator.CreateInstance(Tip("Kalender.Rendering.DesktopSurfaceRenderer"), true)!;
        try
        {
            object yuzey = cizici.GetType().GetMethod("Render")!.Invoke(cizici, new[]
            {
                new DSize(64, 36), Oku(depo, "Current")!, yer, BosVeri(), RenkGunu, RenkGunu, false,
            })!;
            using var resim = (DBitmap)Oku(yuzey, "Image")!;
            return resim.GetPixel(32, 18);
        }
        finally
        {
            ((IDisposable)cizici).Dispose();
        }
    }

    private static bool RenkYakin(DColor a, DColor b) => Math.Abs(a.R - b.R) <= 3 && Math.Abs(a.G - b.G) <= 3 && Math.Abs(a.B - b.B) <= 3;

    /// <summary>
    /// Bir ekranın çizimde kullandığı renkler (ARGB, <see cref="TemaRenkleri"/>
    /// sırasıyla): çizicinin o ekran için seçtiği tema. <paramref name="foto"/>
    /// o ekrana çizilen fotoğraf; null ise ekran düz renkle çiziliyor.
    /// </summary>
    private static int[] EkranRenkleri(object depo, string? foto)
    {
        using DBitmap? resim = foto is null ? null : new DBitmap(4, 4);
        object t = Statik(Tip("Kalender.Rendering.DesktopSurfaceRenderer"), "EkranTemasi", Gorunum(depo), foto, resim)!;
        return TemaRenkleri.Select(a => ((DColor)Oku(t, a)!).ToArgb()).ToArray();
    }

    /// <summary>
    /// Bugünün rakamının, zemine bindirilen dairenin üstündeki karşıtlığı (WCAG).
    /// <paramref name="zemin"/> dairenin altındaki renk: düz renk ya da fotoğraf.
    /// </summary>
    private static double RakamKarsitligi(int[] renkler, DColor zemin)
    {
        DColor daire = Renk(renkler[4]);
        double a = daire.A / 255.0;
        int Karistir(int d, int z) => (int)Math.Round((d * a) + (z * (1 - a)));
        DColor ust = DColor.FromArgb(Karistir(daire.R, zemin.R), Karistir(daire.G, zemin.G), Karistir(daire.B, zemin.B));
        return EskiRenk.Karsitlik(Renk(renkler[5]), ust);
    }

    /// <summary>Pencerenin renk düğmeleri (yazı, hafta günleri, tatil, soluk gün, daire) bu renkleri mi gösteriyor?</summary>
    private static bool DugmelerGosteriyor(object pencere, int[] r, out string ayrinti)
    {
        DColor[] dugmeler =
        {
            Dugme(pencere, "TextColorButton"), Dugme(pencere, "WeekdayColorButton"), Dugme(pencere, "HolidayColorButton"),
            Dugme(pencere, "MutedColorButton"), Dugme(pencere, "TodayCircleColorButton"),
        };
        DColor[] beklenen = { Renk(r[1]), Renk(r[1]), Renk(r[3]), Renk(r[2]), Renk(r[4]) };
        ayrinti = string.Join(" ", dugmeler.Select(Onaltilik));
        return dugmeler.Zip(beklenen).All(c => Onaltilik(c.First) == Onaltilik(c.Second));
    }

    private static void Madde13() => FotografKlasoruyle(klasor =>
    {
        Console.WriteLine("13) Fotoğraf yalnız bir ekrandayken pencerede fotoğrafsız ekranın renkleri; \"Otomatik\" kapatılınca orada görünüm aynı, rakam her ekranda okunur");
        string foto = DenemeFotografi(klasor, "koyu.png", KoyuFoto);

        // Maddedeki durum: Kömür zemin, genel fotoğraf yok, ikinci ekranın kendi fotoğrafı var.
        object depo = ZeminDeposu("Photo", "#2E3238", "komur", ekran2: foto);
        object pencere = ZeminPenceresi(depo);
        int[] duzOnce = EkranRenkleri(depo, null);
        int[] fotoOnce = EkranRenkleri(depo, foto);
        bool karisik = DugmelerGosteriyor(pencere, duzOnce, out string dugmeler);

        // Tam fotoğraf ve düz renk kipinde düğmeler eskisi gibi o kipin renklerinde.
        object tamDepo = ZeminDeposu("Photo", "#2E3238", "komur", genel: foto, ekran2: foto);
        bool tam = DugmelerGosteriyor(ZeminPenceresi(tamDepo), EkranRenkleri(tamDepo, foto), out _);
        object duzDepo = ZeminDeposu("Solid", "#F2F0EC", "kagit");
        bool duz = DugmelerGosteriyor(ZeminPenceresi(duzDepo), EkranRenkleri(duzDepo, null), out _);
        Denetle("ekranlardan yalnız birinde fotoğraf varken pencerenin renk düğmeleri fotoğrafsız ekranda çizilen renkler; "
            + "tam fotoğraf ve düz renk kipinde eskisi gibi", karisik && tam && duz, dugmeler);

        Kutu(pencere, "TodayCircleAutoBox").IsChecked = false;
        int[] duzSonra = EkranRenkleri(depo, null);
        int[] fotoSonra = EkranRenkleri(depo, foto);
        var daire = (string?)Oku(Oku(Gorunum(depo), "Colors")!, "TodayCircle");
        Denetle("\"Bugünün dairesi\" otomatiği kapatılınca düğmedeki renk kalıcı oluyor, fotoğrafsız ekranda görünüm aynı",
            string.Equals(daire, Onaltilik(Dugme(pencere, "TodayCircleColorButton")), StringComparison.OrdinalIgnoreCase)
            && duzSonra.SequenceEqual(duzOnce), $"saklanan {daire ?? "(yok)"}");

        double duzK = RakamKarsitligi(duzSonra, Renk(duzSonra[0]));
        double fotoK = RakamKarsitligi(fotoSonra, KoyuFoto);
        Denetle($"bugünün rakamı iki ekranda da okunur (fotoğrafsız {duzK:0.0}:1, fotoğraflı {fotoK:0.0}:1); "
            + "fotoğraflı ekranda rakamın rengi aynı, daire yine yarı saydam",
            duzK >= 3 && fotoK >= 3 && fotoSonra[5] == fotoOnce[5] && Renk(fotoSonra[4]).A == Renk(fotoOnce[4]).A
            && Renk(fotoSonra[4]).A < 128);

        // Açık düz zemin (Kâğıt): fotoğrafsız ekranın otomatik renkleri fotoğraflı ekranınkilerden ayrı.
        var kalanlar = new List<string>();
        foreach (string ad in new[] { "AutoTextColorBox", "WeekdayAutoBox", "HolidayAutoBox", "MutedAutoBox", "TodayCircleAutoBox" })
        {
            object d = ZeminDeposu("Photo", "#F2F0EC", "kagit", ekran2: foto);
            object p = ZeminPenceresi(d);
            int[] once = EkranRenkleri(d, null);
            Kutu(p, ad).IsChecked = false;
            int[] sonra = EkranRenkleri(d, null);
            double k = RakamKarsitligi(sonra, Renk(sonra[0]));
            bool gosteriyor = DugmelerGosteriyor(p, sonra, out string goster);
            if (!sonra.SequenceEqual(once) || k < 3 || !gosteriyor || Kutu(p, ad).IsChecked != false)
            {
                kalanlar.Add($"{ad}: rakam {k:0.0}:1, düğmeler {goster}");
            }
        }

        Denetle("Kâğıt zeminde (ikinci ekranda fotoğraf) yazı, hafta günleri, tatil, soluk gün ve daire anahtarlarının her biri "
            + "kapatılınca fotoğrafsız ekranda görünüm aynı, rakam okunur, düğmeler çizilen rengi gösteriyor",
            kalanlar.Count == 0, string.Join("; ", kalanlar));

        // Renk çarkından yazı rengi: fotoğrafsız ekranda da kullanılıyor, düğmede kalıyor. Otomatik
        // yeniden açılınca fotoğrafsız ekran yine zemine göre, fotoğraflı ekranda seçilen renk kalıyor.
        object cDepo = ZeminDeposu("Photo", "#F2F0EC", "kagit", ekran2: foto);
        object cPencere = ZeminPenceresi(cDepo);
        int otomatikYazi = EkranRenkleri(cDepo, null)[1];
        RenkCarkindan(cPencere, "OnTextColorPicked", "#8E24AA");
        int mor = EskiRenk.Ayristir("#8E24AA").ToArgb();
        bool secilince = EkranRenkleri(cDepo, null)[1] == mor && EkranRenkleri(cDepo, foto)[1] == mor
            && Onaltilik(Dugme(cPencere, "TextColorButton")) == "#8E24AA" && Kutu(cPencere, "AutoTextColorBox").IsChecked == false;
        Kutu(cPencere, "AutoTextColorBox").IsChecked = true;
        bool yenidenOtomatik = EkranRenkleri(cDepo, null)[1] == otomatikYazi && EkranRenkleri(cDepo, foto)[1] == mor;
        Denetle("renk çarkından yazı rengi seçilince iki ekranda da o renk, düğmede o renk, Otomatik kapalı; Otomatik yeniden "
            + "açılınca fotoğrafsız ekran zemine göre, fotoğraflı ekranda seçilen renk", secilince && yenidenOtomatik,
            $"otomatik yazı {Onaltilik(Renk(otomatikYazi))}");
    });

    private static void Madde14() => FotografKlasoruyle(klasor =>
    {
        Console.WriteLine("14) Ekrana ayrı fotoğraf varken zemin rengi seçilince o ekran fotoğrafını koruyor, öbürü yeni rengi alıyor");
        string yesil = DenemeFotografi(klasor, "ekran-2.png", YesilFoto);
        string turuncu = DenemeFotografi(klasor, "genel.png", TuruncuFoto);

        // Genel fotoğraf yok, ikinci ekranın kendi fotoğrafı var, genel zemin Kömür.
        object depo = ZeminDeposu("Photo", "#2E3238", "komur", ekran2: yesil);
        object pencere = ZeminPenceresi(depo);
        string? acilista = Isaretli(pencere, "PresetPanel");

        HazirRenkSec(pencere, "kagit");
        DColor birinci = ZeminPikseli(depo, Ekran1);
        DColor ikinci = ZeminPikseli(depo, Ekran2);
        Denetle("hazır renk (Kâğıt) seçilince ikinci ekran kendi fotoğrafıyla, birinci ekran Kâğıt rengiyle çiziliyor",
            RenkYakin(birinci, EskiRenk.Ayristir("#F2F0EC")) && RenkYakin(ikinci, YesilFoto)
            && (string?)Oku(Yerlesimler(depo)[Ekran2]!, "PhotoPath") == yesil,
            $"birinci {Onaltilik(birinci)}, ikinci {Onaltilik(ikinci)}");

        object satir2 = Satir(pencere, Ekran2);
        Denetle("hazır renklerde açılışta Kömür, seçilince Kâğıt işaretli; Boyut ve Konum sayfasında ikinci ekranın satırı çizilen "
            + "fotoğrafı gösteriyor, birincisi genel zemini",
            acilista == "komur" && Isaretli(pencere, "PresetPanel") == "kagit" && (bool)Oku(satir2, "HasPhoto")!
            && ((string)Oku(satir2, "Detail")!).EndsWith("ekran-2.png", StringComparison.Ordinal)
            && ((string)Oku(Satir(pencere, Ekran1), "Detail")!).EndsWith("genel zemin", StringComparison.Ordinal)
            && RenkYakin(ZeminPikseli(depo, Ekran2), YesilFoto), $"açılışta {acilista ?? "(hiçbiri)"}");

        RenkCarkindan(pencere, "OnBackgroundColorPicked", "#336699");
        birinci = ZeminPikseli(depo, Ekran1);
        ikinci = ZeminPikseli(depo, Ekran2);
        Denetle("renk çarkından renk seçilince de ikinci ekran fotoğraflı, birinci yeni renkte; hazır renklerden hiçbiri "
            + "işaretli değil, \"kendi renginiz\" notu",
            RenkYakin(birinci, EskiRenk.Ayristir("#336699")) && RenkYakin(ikinci, YesilFoto)
            && Isaretli(pencere, "PresetPanel") is null && Metin(pencere, "CustomColorNote") == "Şu an kendi renginiz kullanılıyor.",
            $"birinci {Onaltilik(birinci)}, ikinci {Onaltilik(ikinci)}");

        // Genel fotoğraf da varken: genel fotoğraf bırakılıyor, ekranın kendi fotoğrafı kalıyor.
        object genelDepo = ZeminDeposu("Photo", "#2E3238", "komur", genel: turuncu, ekran2: yesil);
        object genelPencere = ZeminPenceresi(genelDepo);
        bool onceTuruncu = RenkYakin(ZeminPikseli(genelDepo, Ekran1), TuruncuFoto);
        HazirRenkSec(genelPencere, "siyah");
        Denetle("genel fotoğraf varken hazır renk (Siyah) seçilince genel fotoğraf bırakılıyor: birinci ekran Siyah, ikinci ekran "
            + "kendi fotoğrafıyla; Siyah işaretli",
            onceTuruncu && RenkYakin(ZeminPikseli(genelDepo, Ekran1), DColor.Black) && RenkYakin(ZeminPikseli(genelDepo, Ekran2), YesilFoto)
            && Isaretli(genelPencere, "PresetPanel") == "siyah");
    });

    private static void Madde15() => FotografKlasoruyle(klasor =>
    {
        Console.WriteLine("15) Düz renge geçerken genel fotoğraf bırakılıyor; sonra bir ekrana fotoğraf verilince öbür ekran düz renkte kalıyor");
        string yesil = DenemeFotografi(klasor, "ekran-2.png", YesilFoto);
        string turuncu = DenemeFotografi(klasor, "genel.png", TuruncuFoto);
        DColor murekkep = EskiRenk.Ayristir("#141A22");
        object duzKip = Secenek("BackgroundMode", "Solid");
        bool KaldirAcik(object p) => ((System.Windows.UIElement)AlanOku(p, "RemovePhotoButton")).IsEnabled;

        // Maddedeki sıra: genel fotoğraf, hazır renk (Mürekkep), ikinci ekrana fotoğraf, sonra "Kaldır".
        object depo = ZeminDeposu("Photo", "#2E3238", "komur", genel: turuncu);
        object pencere = ZeminPenceresi(depo);
        HazirRenkSec(pencere, "murekkep");
        object g = Gorunum(depo);
        Denetle("hazır renk seçilince genel fotoğrafın yolu siliniyor: \"Şu an fotoğraf kullanılmıyor.\", \"Fotoğrafı kaldır\" "
            + "kapalı, zemin Mürekkep",
            Oku(g, "PhotoPath") is null && Oku(g, "BackgroundMode")!.Equals(duzKip)
            && Metin(pencere, "PhotoPathText") == "Şu an fotoğraf kullanılmıyor." && !KaldirAcik(pencere)
            && RenkYakin(ZeminPikseli(depo, Ekran1), murekkep), Metin(pencere, "PhotoPathText"));

        EkranaFotografVer(pencere, depo, Ekran2, yesil);
        DColor birinci = ZeminPikseli(depo, Ekran1);
        Denetle("sonra ikinci ekrana fotoğraf verilince birinci ekran Mürekkep'te kalıyor (bırakılan fotoğraf geri gelmiyor), "
            + "ikinci ekran fotoğraflı; Mürekkep işaretli",
            RenkYakin(birinci, murekkep) && RenkYakin(ZeminPikseli(depo, Ekran2), YesilFoto)
            && Isaretli(pencere, "PresetPanel") == "murekkep", $"birinci {Onaltilik(birinci)}");

        EkranFotografiniKaldir(pencere, Ekran2);
        Denetle("ikinci ekranın fotoğrafı kaldırılınca düz renge dönülüyor, iki ekran da Mürekkep",
            Oku(g, "BackgroundMode")!.Equals(duzKip) && RenkYakin(ZeminPikseli(depo, Ekran1), murekkep)
            && RenkYakin(ZeminPikseli(depo, Ekran2), murekkep));

        // Eski sürümden kalan durum: düz renkte genel fotoğrafın yolu duruyor.
        object eskiDepo = ZeminDeposu("Solid", "#141A22", "murekkep", genel: turuncu);
        object eskiPencere = ZeminPenceresi(eskiDepo);
        Denetle("eski sürümün düz renkte bıraktığı fotoğraf \"Seçili fotoğraf\" diye gösterilmiyor, \"Fotoğrafı kaldır\" kapalı",
            Metin(eskiPencere, "PhotoPathText") == "Şu an fotoğraf kullanılmıyor." && !KaldirAcik(eskiPencere),
            Metin(eskiPencere, "PhotoPathText"));

        EkranaFotografVer(eskiPencere, eskiDepo, Ekran2, yesil);
        birinci = ZeminPikseli(eskiDepo, Ekran1);
        Denetle("o durumda ikinci ekrana fotoğraf verilince eski yol siliniyor, birinci ekran Mürekkep'te kalıyor",
            Oku(Gorunum(eskiDepo), "PhotoPath") is null && RenkYakin(birinci, murekkep)
            && RenkYakin(ZeminPikseli(eskiDepo, Ekran2), YesilFoto), $"birinci {Onaltilik(birinci)}");

        EkranFotografiniKaldir(eskiPencere, Ekran2);
        Denetle("fotoğrafı kaldırılınca yine düz renk, iki ekran da Mürekkep",
            Oku(Gorunum(eskiDepo), "BackgroundMode")!.Equals(duzKip) && RenkYakin(ZeminPikseli(eskiDepo, Ekran1), murekkep)
            && RenkYakin(ZeminPikseli(eskiDepo, Ekran2), murekkep));

        // Derlenmiş kodda: "Bu ekrana fotoğraf…" dosya seçildikten sonra yukarıda sınanan adımlardan geçiyor.
        Type pencereTipi = Tip("Kalender.Ui.SettingsWindow");
        List<Komut> k = Komutlar(pencereTipi.GetMethod("OnPickMonitorPhoto", Hepsi)!);
        int sec = Bul(k, x => Cagri(x, pencereTipi, "AskForPhoto"));
        Denetle("\"Bu ekrana fotoğraf…\" dosya seçildikten sonra aynı adımlardan geçiyor",
            sec >= 0 && Bul(k, x => Cagri(x, pencereTipi, "EkranFotografiVerildi"), sec) > sec);
    });

    private static void Madde16()
    {
        Console.WriteLine("16) Renk çarkından zemin rengi seçilince hazır fotoğrafın işareti kalkıyor");
        object foto = ((IList)Tip("Kalender.Settings.HazirFotograflar").GetProperty("All", Hepsi)!.GetValue(null)!)[1]!;
        string yol = (string)Statik(Tip("Kalender.Settings.HazirFotograflar"), "PathOf", foto)!;
        object depo = ZeminDeposu("Photo", "#2E3238", "komur", genel: yol);
        object pencere = ZeminPenceresi(depo);
        string? once = Isaretli(pencere, "ReadyPhotoPanel");

        RenkCarkindan(pencere, "OnBackgroundColorPicked", "#336699");
        string? sonra = Isaretli(pencere, "ReadyPhotoPanel");
        Denetle($"hazır fotoğraf ({Oku(foto, "Name")}) zemindeyken renk çarkından renk seçilince hiçbir hazır fotoğraf işaretli "
            + "değil; zemin fotoğrafı satırı boş, \"kendi renginiz\" notu",
            once == (string?)Oku(foto, "Id") && sonra is null && Metin(pencere, "PhotoPathText") == "Şu an fotoğraf kullanılmıyor."
            && Metin(pencere, "CustomColorNote") == "Şu an kendi renginiz kullanılıyor.",
            $"önce {once ?? "(hiçbiri)"}, sonra {sonra ?? "(hiçbiri)"}");
    }

    // ------------------------ Google eşitlemesi ve ayarları (17, 18, 19, 25, 26, 31)

    /// <summary>
    /// Google'ın sahtesi: bağlı hesabın takvimlerini, tüm gün etkinliklerini, görev
    /// listelerini ve görevlerini bu program verir, ağa çıkılmaz; her anahtarı
    /// kabul eder (GoogleEsitleme'dekine benzer). Artımlı yoklamanın "son
    /// yoklamadan beri değişen var mı" sorusuna hep "yok" der. Bağlı hesapta
    /// olmayan takvim ya da liste istenirse 404 döner.
    /// </summary>
    private sealed class SahteGoogleHesabi : HttpMessageHandler
    {
        /// <summary>Bağlı hesabın e-postası; birincil takvimin kimliği de bu.</summary>
        public string Hesap = "a@deneme";

        /// <summary>Takvim kimliği → o takvimdeki tüm gün etkinlikleri (kimlik, gün).</summary>
        public Dictionary<string, (string Id, DateOnly Gun)[]> Takvimler = new();

        /// <summary>Görev listesi kimliği → görevler (kimlik, gün).</summary>
        public Dictionary<string, (string Id, DateOnly Gun)[]> Listeler = new();

        /// <summary>Gelen isteklerin adresleri (anahtar adresi dışında), çözülmüş hâliyle.</summary>
        public readonly List<string> Adresler = new();

        /// <summary>Anahtar adresine (oauth2) gelen istek sayısı.</summary>
        public int AnahtarIstegi;

        /// <summary>Her yanıttan önceki bekleme (anahtar adresi dışında).</summary>
        public TimeSpan Gecikme;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage istek, CancellationToken iptal)
        {
            string adres = Uri.UnescapeDataString(istek.RequestUri!.AbsoluteUri);
            if (adres.StartsWith("https://oauth2.googleapis.com/token", StringComparison.Ordinal))
            {
                int sira = Interlocked.Increment(ref AnahtarIstegi);
                return Yanit(200, new { access_token = "ERISIM" + sira, refresh_token = "yenileme", expires_in = 3599 });
            }

            lock (Adresler)
            {
                Adresler.Add(adres);
            }

            if (Gecikme > TimeSpan.Zero)
            {
                await Task.Delay(Gecikme, iptal);
            }

            string yol = adres.Split('?')[0];
            bool artimli = adres.Contains("updatedMin=", StringComparison.Ordinal);

            if (yol.EndsWith("/users/me/calendarList", StringComparison.Ordinal))
            {
                return Yanit(200, new
                {
                    items = Takvimler.Keys.Select(k => new { id = k, summary = k, backgroundColor = "#039be5", primary = k == Hesap }),
                });
            }

            if (yol.EndsWith("/colors", StringComparison.Ordinal))
            {
                return Yanit(200, new { @event = new Dictionary<string, object>() });
            }

            if (yol.EndsWith("/users/@me/lists", StringComparison.Ordinal))
            {
                return Yanit(200, new { items = Listeler.Keys.Select(k => new { id = k, title = k }) });
            }

            if (Parca(yol, "/lists/", "/tasks") is string liste)
            {
                if (!Listeler.TryGetValue(liste, out (string Id, DateOnly Gun)[]? gorevler))
                {
                    return Yanit(404, new { });
                }

                return Yanit(200, new
                {
                    items = (artimli ? Array.Empty<(string Id, DateOnly Gun)>() : gorevler).Select(g => new
                    {
                        id = g.Id,
                        title = g.Id,
                        due = g.Gun.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "T00:00:00.000Z",
                        status = "needsAction",
                    }),
                });
            }

            if (Parca(yol, "/calendars/", "/events") is string takvim)
            {
                if (!Takvimler.TryGetValue(takvim, out (string Id, DateOnly Gun)[]? etkinlikler))
                {
                    return Yanit(404, new { });
                }

                return Yanit(200, new
                {
                    items = (artimli ? Array.Empty<(string Id, DateOnly Gun)>() : etkinlikler).Select(e => new
                    {
                        id = e.Id,
                        summary = e.Id,
                        status = "confirmed",
                        start = new { date = e.Gun.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) },
                        end = new { date = e.Gun.AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) },
                    }),
                });
            }

            // Takvimin etiket renkleri: etiket yok.
            return yol.Contains("/calendars/", StringComparison.Ordinal) ? Yanit(200, new { }) : Yanit(404, new { });
        }

        /// <summary>Yolun <paramref name="bas"/> ile <paramref name="son"/> arasındaki parçası (takvim ya da liste kimliği).</summary>
        private static string? Parca(string yol, string bas, string son)
        {
            int i = yol.IndexOf(bas, StringComparison.Ordinal);
            return i >= 0 && yol.EndsWith(son, StringComparison.Ordinal) && yol.Length - son.Length > i + bas.Length
                ? yol[(i + bas.Length)..^son.Length]
                : null;
        }

        private static HttpResponseMessage Yanit(int durum, object govde) => new((HttpStatusCode)durum)
        {
            Content = new StringContent(JsonSerializer.Serialize(govde), Encoding.UTF8, "application/json"),
        };
    }

    private const string IstemciKimligi = "deneme-istemci";

    /// <summary>
    /// A hesabı: kendi takvimi (a@deneme) ve "Ev" takvimi, iki görev listesi;
    /// Ekim ve Kasım 2026'da birer kayıtları var.
    /// </summary>
    private static SahteGoogleHesabi AHesabi() => new()
    {
        Hesap = "a@deneme",
        Takvimler =
        {
            ["a@deneme"] = new[] { ("is-ekim", new DateOnly(2026, 10, 15)), ("is-kasim", new DateOnly(2026, 11, 20)) },
            ["ev@deneme"] = new[] { ("ev-ekim", new DateOnly(2026, 10, 16)), ("ev-kasim", new DateOnly(2026, 11, 25)) },
        },
        Listeler =
        {
            ["liste1"] = new[] { ("gorev-ekim", new DateOnly(2026, 10, 14)) },
            ["liste2"] = new[] { ("gorev2-kasim", new DateOnly(2026, 11, 24)) },
        },
    };

    /// <summary>B hesabı: kendi takvimi (b@deneme), "İş" takvimi ve bir görev listesi.</summary>
    private static void BHesabinaGec(SahteGoogleHesabi google)
    {
        google.Hesap = "b@deneme";
        google.Takvimler = new()
        {
            ["b@deneme"] = new[] { ("b-ekim", new DateOnly(2026, 10, 12)) },
            ["b-is@deneme"] = new[] { ("b-is-ekim", new DateOnly(2026, 10, 13)) },
        };
        google.Listeler = new() { ["b-liste"] = new[] { ("b-gorev", new DateOnly(2026, 10, 14)) } };
    }

    /// <summary>
    /// Sahte Google'a bağlı yetki ve sağlayıcı: ayarlar bellekte (kaydedilince
    /// geçici klasöre yazılır), yetki kaydı sahte hesabınki, istemci kimliği
    /// kayıttakiyle aynı.
    /// </summary>
    private static (object Depo, object Yetki, object Saglayici) GoogleKur(SahteGoogleHesabi google)
    {
        object depo = ZeminDeposu("Solid", "#2E3238", "komur");
        Yaz(GoogleAyari(depo), "ClientId", IstemciKimligi);

        object yapilandirma = Activator.CreateInstance(Tip("Kalender.Google.SettingsOAuthConfig"), depo)!;
        object yetki = Activator.CreateInstance(Tip("Kalender.Google.GoogleAuthenticator"), yapilandirma)!;
        object kayit = Activator.CreateInstance(Tip("Kalender.Google.StoredToken"))!;
        Yaz(kayit, "RefreshToken", "yenileme");
        Yaz(kayit, "ClientId", IstemciKimligi);
        Yaz(kayit, "AccountEmail", google.Hesap);
        AlanAta(yetki, "_token", kayit);
        ((IDisposable)AlanOku(yetki, "_http")).Dispose();
        AlanAta(yetki, "_http", new HttpClient(google));

        object saglayici = Activator.CreateInstance(Tip("Kalender.Google.GoogleCalendarProvider"), yetki, depo)!;
        ((IDisposable)AlanOku(saglayici, "_http")).Dispose();
        AlanAta(saglayici, "_http", new HttpClient(google));
        return (depo, yetki, saglayici);
    }

    private static object GoogleAyari(object depo) => Oku(Oku(depo, "Current")!, "Google")!;

    /// <summary>Ayarlardaki canlı seçim listesi (SelectedCalendarIds ya da SelectedTaskListIds).</summary>
    private static List<string> Secim(object depo, string ad) => (List<string>)Oku(GoogleAyari(depo), ad)!;

    private static string HicbiriSecili => (string)Tip("Kalender.Settings.GoogleSettings").GetField("HicbiriSecili", Hepsi)!.GetValue(null)!;

    /// <summary>2026'nın verilen ayının ızgarasının kapsadığı gün aralığı.</summary>
    private static (DateOnly Ilk, DateOnly Son) Izgara(int ay)
        => ((DateOnly, DateOnly))Statik(Tip("Kalender.Calendar.CalendarLayout"), "GridRange", new DateOnly(2026, ay, 1))!;

    /// <summary>Ayı Google'dan eşitler (iş parçacığı havuzunda, arayüz dağıtıcısına bağlanmadan).</summary>
    private static bool AyiEsitle(object saglayici, int ay, bool tam)
    {
        (DateOnly ilk, DateOnly son) = Izgara(ay);
        return Task.Run(() => (Task<bool>)Cagir(saglayici, "SyncAsync", ilk, son, tam, CancellationToken.None)!)
            .GetAwaiter().GetResult();
    }

    /// <summary>Ok ile o aya geçilmiş gibi: ay bellekteyse hemen kullanılır.</summary>
    private static bool AyiGetir(object saglayici, int ay)
    {
        (DateOnly ilk, DateOnly son) = Izgara(ay);
        return (bool)Cagir(saglayici, "GecmistenGetir", ilk, son)!;
    }

    /// <summary>Sağlayıcının elindeki etkinliklerin ve görevlerin kimlikleri.</summary>
    private static HashSet<string> Kayitlar(object saglayici)
    {
        object veri = Oku(saglayici, "Data")!;
        return ((IEnumerable)Oku(veri, "Events")!).Cast<object>().Select(e => (string)Oku(e, "Id")!)
            .Concat(((IEnumerable)Oku(veri, "Tasks")!).Cast<object>().Select(g => (string)Oku(g, "Id")!))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IList AyBellegi(object saglayici) => (IList)AlanOku(saglayici, "_gecmis");

    private static int AdresSayisi(SahteGoogleHesabi google)
    {
        lock (google.Adresler)
        {
            return google.Adresler.Count;
        }
    }

    private static List<string> AdreslerSonra(SahteGoogleHesabi google, int once)
    {
        lock (google.Adresler)
        {
            return google.Adresler.Skip(once).ToList();
        }
    }

    /// <summary>Ekim 2026'yı gösteren takvim içeriği (tatil listesi boş).</summary>
    private static object GoogleVeriHizmeti(object saglayici, object depo)
    {
        object tatiller = Activator.CreateInstance(Tip("Kalender.Calendar.Holidays.HolidayService"), true)!;
        object veri = Activator.CreateInstance(Tip("Kalender.Calendar.CalendarDataService"), tatiller, saglayici, depo)!;
        Cagir(veri, "SetViewMonth", new DateOnly(2026, 10, 1));
        return veri;
    }

    /// <summary>Takvimde o güne çizilen kayıtların başlıkları.</summary>
    private static List<string> Cizilen(object veri, DateOnly gun)
    {
        object? icerik = Cagir(Oku(veri, "Current")!, "For", gun);
        return icerik is null
            ? new List<string>()
            : ((IEnumerable)Oku(icerik, "Entries")!).Cast<object>().Select(k => (string)Oku(k, "Title")!).ToList();
    }

    // ------------------------------------------------------------------ 17. madde

    private static void Madde17()
    {
        Console.WriteLine("17) Ayarlarda açılan takvim ve görev listesi bellekteki aylarda da hemen görünüyor; kapatılan, bellekten gelen ayda çizilmiyor");
        SahteGoogleHesabi google = AHesabi();
        var (depo, _, saglayici) = GoogleKur(google);
        List<string> takvimler = Secim(depo, "SelectedCalendarIds");
        List<string> listeler = Secim(depo, "SelectedTaskListIds");
        IList bellek = AyBellegi(saglayici);

        // "Ev" takvimi ve ikinci liste kapalı. Kasım'a bakılıp Ekim'e dönülmüş:
        // Kasım ay belleğinde, son tam eşitlemesi az önce.
        takvimler.Add("a@deneme");
        listeler.Add("liste1");
        bool hazir = AyiEsitle(saglayici, 10, tam: true) && AyiEsitle(saglayici, 11, tam: true) && AyiGetir(saglayici, 10);
        Denetle("sınama düzeni: Ekim gösteriliyor, Kasım bellekte; kapalı takvimden ve listeden bir şey alınmadı",
            hazir && bellek.Count == 1 && Kayitlar(saglayici).SetEquals(new[] { "is-ekim", "gorev-ekim" }),
            string.Join(", ", Kayitlar(saglayici)));

        // Ayarlarda "Ev" ve ikinci liste açılıyor (bütün anahtarlar açık: seçim "hepsi").
        takvimler.Clear();
        listeler.Clear();
        Cagir(saglayici, "SecimDegisti");
        Denetle("seçim değişince bellekteki aylar kalıyor, tam yenileme zamanları siliniyor",
            bellek.Count == 1 && Oku(bellek[0]!, "FullSyncUtc") is null && Oku(Oku(saglayici, "Data")!, "FullSyncUtc") is null);

        // Ayarlar penceresinin zorladığı yenileme görünen ayı (Ekim) yeni seçimle alıyor;
        // ardından ok ile Kasım'a geçiliyor: önce bellekteki Kasım, sonra zorlanmamış eşitleme.
        AyiEsitle(saglayici, 10, tam: true);
        int once = AdresSayisi(google);
        bool geldi = AyiGetir(saglayici, 11);
        bool tuttu = AyiEsitle(saglayici, 11, tam: false);
        List<string> sorulan = AdreslerSonra(google, once);
        HashSet<string> kasim = Kayitlar(saglayici);
        Denetle("bellekten gelen Kasım'da yeni açılan takvimin ve görev listesinin kayıtları var",
            geldi && tuttu && kasim.Contains("ev-kasim") && kasim.Contains("gorev2-kasim"), string.Join(", ", kasim));
        bool kasimBastan = sorulan.Any(a => a.Contains("/events?timeMin=", StringComparison.Ordinal))
            && !sorulan.Any(a => a.Contains("updatedMin=", StringComparison.Ordinal));
        int kasimIstegi = sorulan.Count;

        // 30 sn sonra bu aya dönüş: seçim yeniden değişmedi, bellek eskisi gibi hızlı.
        once = AdresSayisi(google);
        bool dondu = AyiGetir(saglayici, 10) && AyiEsitle(saglayici, 10, tam: false);
        sorulan = AdreslerSonra(google, once);
        Denetle("seçim değişince bellekteki Kasım baştan alındı (\"değişen var mı\" sorusu sorulmadı); seçim yeniden "
            + "değişmeden Ekim'e dönüşte bellekteki ay eskisi gibi artımlı tazeleniyor, iki takvimin kaydı da var",
            kasimBastan && dondu && sorulan.Any(a => a.Contains("updatedMin=", StringComparison.Ordinal))
            && !sorulan.Any(a => a.Contains("/events?timeMin=", StringComparison.Ordinal))
            && Kayitlar(saglayici).IsSupersetOf(new[] { "is-ekim", "ev-ekim" }),
            $"Kasım {kasimIstegi} istek, Ekim {sorulan.Count} istek");

        // "Ev" ve ikinci liste yeniden kapatılıyor; ağ yok, eşitleme hiç tutmuyor.
        // Ok ile Kasım'a geçiliyor: bellekteki Kasım iki takvimle alınmıştı.
        takvimler.Add("a@deneme");
        listeler.Add("liste1");
        Cagir(saglayici, "SecimDegisti");
        object veri = GoogleVeriHizmeti(saglayici, depo);
        AyiGetir(saglayici, 11);
        Cagir(veri, "SetViewMonth", new DateOnly(2026, 11, 1));
        Denetle("çevrim dışıyken bellekten gelen Kasım'da kapatılan takvimin ve listenin kayıtları çizilmiyor, açık takvimin kaydı çiziliyor",
            Kayitlar(saglayici).Contains("ev-kasim") && !Cizilen(veri, new DateOnly(2026, 11, 25)).Contains("ev-kasim")
            && !Cizilen(veri, new DateOnly(2026, 11, 24)).Contains("gorev2-kasim")
            && Cizilen(veri, new DateOnly(2026, 11, 20)).Contains("is-kasim"),
            "25 Kasım: " + string.Join(", ", Cizilen(veri, new DateOnly(2026, 11, 25))));

        takvimler.Clear();
        takvimler.Add(HicbiriSecili);
        Cagir(veri, "Rebuild");
        Denetle("bütün takvimler kapatılınca bellekteki ayın hiçbir etkinliği çizilmiyor",
            !Cizilen(veri, new DateOnly(2026, 11, 20)).Contains("is-kasim"));
        takvimler.Clear();
        takvimler.Add("a@deneme");

        // Eşitleme sürerken seçim değişiyor; yeni bir istek gelmeden eşitleme bitiyor.
        google.Gecikme = TimeSpan.FromMilliseconds(150);
        (DateOnly ilk, DateOnly son) = Izgara(11);
        once = AdresSayisi(google);
        Task<bool> suren = Task.Run(() => (Task<bool>)Cagir(saglayici, "SyncAsync", ilk, son, true, CancellationToken.None)!);
        var saat = System.Diagnostics.Stopwatch.StartNew();
        while (AdresSayisi(google) == once && saat.Elapsed < TimeSpan.FromSeconds(5))
        {
            Thread.Sleep(5);
        }

        Cagir(saglayici, "SecimDegisti");
        bool bitti = suren.GetAwaiter().GetResult();
        google.Gecikme = TimeSpan.Zero;
        Denetle("eşitleme sürerken seçim değişirse sonuç yazılıyor, ama o ayın sıradaki eşitlemesi tam",
            bitti && Oku(Oku(saglayici, "Data")!, "FullSyncUtc") is null);

        // Uygulama, ayarlar penceresinin haberini sağlayıcıya iletiyor.
        AyiEsitle(saglayici, 11, tam: true);
        object host = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Core.AppHost"));
        AlanAta(host, "_google", saglayici);
        bool onceVardi = Oku(Oku(saglayici, "Data")!, "FullSyncUtc") is not null;
        Cagir(host, "GoogleSecimiDegisti");
        Denetle("uygulama seçim değişikliğini sağlayıcıya iletiyor",
            onceVardi && Oku(Oku(saglayici, "Data")!, "FullSyncUtc") is null);

        // Ayarlar penceresinde anahtar değişince önce bu haber, sonra zorlanmış yenileme.
        object pencere = GooglePenceresi(depo);
        var kapi = (SahteAyarKapisi)AlanOku(pencere, "_host");
        var panel = (StackPanel)AlanOku(pencere, "CalendarListPanel");
        Cagir(pencere, "BuildSourceList", panel,
            new List<(string Id, string Name)> { ("a@deneme", "Takvimim"), ("ev@deneme", "Ev") }, takvimler);
        GoogleAnahtarlari(panel)[1].IsChecked = true;
        Denetle("ayarlar penceresinde anahtar açılınca bellekteki aylar bayatlatılıyor, sonra görünen ay zorla yenileniyor",
            takvimler.Count == 0 && kapi.Cagrilar.SequenceEqual(new[] { "GoogleSecimiDegisti", "ApplyAppearance", "RefreshGoogleAsync(zorla)" }),
            string.Join(" → ", kapi.Cagrilar));
        Cagir(saglayici, "ClearCache");
    }

    /// <summary>Paneldeki takvim ya da görev listesi anahtarları, görünüş sırasıyla.</summary>
    private static List<CheckBox> GoogleAnahtarlari(StackPanel panel)
        => panel.Children.OfType<DockPanel>().SelectMany(d => d.Children.OfType<CheckBox>()).ToList();

    /// <summary>
    /// Google sayfası da kurulabilen ayarlar penceresi: <see cref="ZeminPenceresi"/>
    /// gibi kurulur (ekranda açılmaz, kapısı sahte), üstüne pencerenin temel
    /// kurucusu çalıştırılır ve takvim listesinin satır stilleri verilir
    /// (KodIncelemesi'ndeki gibi).
    /// </summary>
    private static object GooglePenceresi(object depo)
    {
        object pencere = ZeminPenceresi(depo);
        typeof(Window).GetConstructor(Type.EmptyTypes)!.Invoke(pencere, null);
        ((FrameworkElement)pencere).Resources = new ResourceDictionary
        {
            ["Satir"] = new Style(typeof(DockPanel)),
            ["SatirBasligi"] = new Style(typeof(TextBlock)),
            ["SatirAciklamasi"] = new Style(typeof(TextBlock)),
        };
        AlanAta(pencere, "_sourceSignatures", new Dictionary<StackPanel, string>());
        return pencere;
    }

    // ------------------------------------------------------------------ 18. madde

    /// <summary>
    /// Tarayıcının yerine geçer (GoogleEsitleme'deki gibi): onay adresindeki dönüş
    /// adresine ve durum değerine bakıp yerel dinleyiciye "onaylandı" yanıtını gönderir.
    /// </summary>
    private static async Task OnayVer(string adres)
    {
        var sorgu = System.Web.HttpUtility.ParseQueryString(new Uri(adres).Query);
        using var istemci = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            await istemci.GetStringAsync($"{sorgu["redirect_uri"]}/?code=deneme-kodu&state={Uri.EscapeDataString(sorgu["state"]!)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("   (onay isteği gönderilemedi: " + ex.Message + ")");
        }
    }

    /// <summary>Bağlı uygulamanın Google parçaları; uygulamanın kendisi kurucusu çalıştırılmadan kurulur.</summary>
    private sealed record GoogleUygulamasi(object Depo, object Yetki, object Saglayici, object Veri, object Yoklayici, object Host);

    /// <summary>
    /// Google'a bağlı bir uygulama: kurucusu çalıştırılmamış AppHost'a ayarlar,
    /// yetki, sağlayıcı, takvim içeriği (Ekim) ve yoklayıcı verilir; açılıştaki
    /// gibi bağlantı durumu değişince uygulamaya haber gider. Tepsi, katman ve
    /// duvar kâğıdı yok. Uygulamanın dağıtıcısı bu iş parçacığınınki.
    /// </summary>
    private static GoogleUygulamasi GoogleUygulamasiKur(SahteGoogleHesabi google)
    {
        if (System.Windows.Application.Current is null)
        {
            _ = new System.Windows.Application();
        }

        var (depo, yetki, saglayici) = GoogleKur(google);
        object veri = GoogleVeriHizmeti(saglayici, depo);
        object yoklayici = Activator.CreateInstance(Tip("Kalender.Google.GooglePoller"), saglayici, veri, depo)!;

        Type hostTipi = Tip("Kalender.Core.AppHost");
        object host = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(hostTipi);
        AlanAta(host, "_settings", depo);
        AlanAta(host, "_googleAuth", yetki);
        AlanAta(host, "_google", saglayici);
        AlanAta(host, "_calendarData", veri);
        AlanAta(host, "_poller", yoklayici);
        AlanAta(host, "_viewMonth", new DateOnly(2026, 10, 1));
        AlanAta(host, "_bilinenKusak", Oku(yetki, "BaglantiKusagi"));

        EventInfo olay = yetki.GetType().GetEvent("StateChanged", Hepsi)!;
        olay.AddEventHandler(yetki, Delegate.CreateDelegate(olay.EventHandlerType!, host,
            hostTipi.GetMethod("OnGoogleStateChanged", Hepsi)!));
        return new GoogleUygulamasi(depo, yetki, saglayici, veri, yoklayici, host);
    }

    /// <summary>
    /// Ayarlar penceresindeki "Yeniden bağlan": uygulamanın bağlanma yolu arayüz
    /// dağıtıcısında çalıştırılır, onayı sınama verir. Bağlanma bitip yoklama
    /// durulana ve <paramref name="durdu"/> doğru olana dek (en çok 10 sn) dağıtıcı çalıştırılır.
    /// </summary>
    private static (bool Basari, string Ileti) YenidenBaglan(GoogleUygulamasi u, Func<bool> durdu)
    {
        Task? gorev = null;
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            gorev = (Task)Cagir(u.Host, "ConnectGoogleAsync", null, CancellationToken.None)!));

        bool Bos() => AlanOku(u.Yoklayici, "_current") is not Task t || t.IsCompleted;

        var saat = System.Diagnostics.Stopwatch.StartNew();
        while (saat.Elapsed < TimeSpan.FromSeconds(10) && !(gorev is { IsCompleted: true } && Bos() && durdu()))
        {
            Bekle(20);
        }

        // Kuyruğa düşmüş son haberler de işlensin.
        Bekle(100);

        if (gorev is not { IsCompleted: true })
        {
            return (false, "bağlanma bitmedi");
        }

        object sonuc = gorev.GetType().GetProperty("Result")!.GetValue(gorev)!;
        return ((bool)Oku(sonuc, "Success")!, (string)Oku(sonuc, "Message")!);
    }

    private static void Madde18()
    {
        Console.WriteLine("18) \"Yeniden bağlan\" ile başka hesaba geçilince önceki hesabın seçimi, ay belleği, yerel kopyası ve e-postası kalmıyor (tarayıcı açılmıyor)");
        PropertyInfo acici = Tip("Kalender.Google.GoogleAuthenticator").GetProperty("TarayiciAcici", Hepsi)!;
        object asil = acici.GetValue(null)!;
        int acilan = 0;
        acici.SetValue(null, new Action<string>(adres =>
        {
            Interlocked.Increment(ref acilan);
            _ = Task.Run(() => OnayVer(adres));
        }));

        var yoklayicilar = new List<object>();
        try
        {
            // Yalnız giriş: yeni yetki kaydına önceki hesabın e-postası yazılmıyor.
            var (_, yetki, _) = GoogleKur(AHesabi());
            var gorev = (Task)Task.Run(() => Cagir(yetki, "SignInAsync", CancellationToken.None)!).GetAwaiter().GetResult();
            gorev.GetAwaiter().GetResult();
            object giris = gorev.GetType().GetProperty("Result")!.GetValue(gorev)!;
            Denetle("girişten sonra yeni yetki kaydında önceki hesabın e-postası yok (ilk eşitleme doldurur)",
                (bool)Oku(giris, "Success")! && Oku(yetki, "AccountEmail") is null && (bool)Oku(yetki, "IsConnected")!,
                (string)Oku(giris, "Message")!);

            // A hesabıyla bağlı; "Ev" takvimi ve ikinci liste kapalı. Ekim gösteriliyor, Kasım bellekte.
            SahteGoogleHesabi google = AHesabi();
            GoogleUygulamasi u = GoogleUygulamasiKur(google);
            yoklayicilar.Add(u.Yoklayici);
            List<string> takvimler = Secim(u.Depo, "SelectedCalendarIds");
            List<string> listeler = Secim(u.Depo, "SelectedTaskListIds");
            takvimler.Add("a@deneme");
            listeler.Add("liste1");
            Yaz(GoogleAyari(u.Depo), "AccountEmail", "a@deneme");
            bool hazir = AyiEsitle(u.Saglayici, 11, tam: true) && AyiEsitle(u.Saglayici, 10, tam: true);
            Denetle("sınama düzeni: A hesabı bağlı, Ekim gösteriliyor, Kasım bellekte",
                hazir && AyBellegi(u.Saglayici).Count == 1 && Kayitlar(u.Saglayici).Contains("is-ekim"));

            // Tarayıcıda B hesabı seçiliyor.
            BHesabinaGec(google);
            var (basari, ileti) = YenidenBaglan(u, () => (string?)Oku(u.Yetki, "AccountEmail") == "b@deneme"
                && takvimler.Count == 0 && listeler.Count == 0 && Kayitlar(u.Saglayici).Contains("b-is-ekim"));
            HashSet<string> kayit = Kayitlar(u.Saglayici);
            Denetle("bağlantı tuttu; takvim ve görev listesi seçimi \"hepsi\"ne döndü, yeni hesabın bütün takvimlerinin ve listesinin kayıtları var",
                basari && takvimler.Count == 0 && listeler.Count == 0 && kayit.SetEquals(new[] { "b-ekim", "b-is-ekim", "b-gorev" }),
                $"{ileti}; seçim [{string.Join(", ", takvimler)}] [{string.Join(", ", listeler)}]; kayıtlar {string.Join(", ", kayit)}");
            Denetle("önceki hesabın ayları ay belleğinde kalmadı", AyBellegi(u.Saglayici).Count == 0,
                $"{AyBellegi(u.Saglayici).Count} ay");

            string kopyaYolu = Path.Combine((string)Tip("Kalender.Core.Paths").GetProperty("CacheFolder", Hepsi)!.GetValue(null)!, "google.json");
            string kopya = File.Exists(kopyaYolu) ? File.ReadAllText(kopyaYolu) : string.Empty;
            Denetle("yerel kopyada önceki hesabın verisi yok, yeni hesabınki var",
                !kopya.Contains("a@deneme", StringComparison.Ordinal) && !kopya.Contains("\"is-ekim\"", StringComparison.Ordinal)
                && kopya.Contains("\"b-is-ekim\"", StringComparison.Ordinal));
            Denetle("takvimde yeni hesabın kayıtları çiziliyor, öncekininki çizilmiyor",
                Cizilen(u.Veri, new DateOnly(2026, 10, 13)).Contains("b-is-ekim")
                && !Cizilen(u.Veri, new DateOnly(2026, 10, 15)).Contains("is-ekim"));

            // Ok ile önceki hesapla bakılmış Kasım'a geçiliyor, ardından zorlanmamış eşitleme.
            // Uygulamadaki gibi takvimin içeriği de Kasım'ı gösteriyor: yoklayıcı da Kasım'ı ister.
            AyiGetir(u.Saglayici, 11);
            Cagir(u.Veri, "SetViewMonth", new DateOnly(2026, 11, 1));
            bool kasimTuttu = AyiEsitle(u.Saglayici, 11, tam: false);
            for (int i = 0; i < 5 || (i < 150 && AlanOku(u.Yoklayici, "_current") is Task { IsCompleted: false }); i++)
            {
                Bekle(20);
            }

            HashSet<string> kasim = Kayitlar(u.Saglayici);
            Denetle("önceki hesapla bakılmış aya geçince o hesabın kayıtları ve e-postası geri gelmiyor (yetkide ve ayarlarda yeni hesap; \"takvimde aç\" onu açar)",
                kasimTuttu && !kasim.Overlaps(new[] { "is-kasim", "ev-kasim", "gorev2-kasim" })
                && (string?)Oku(u.Yetki, "AccountEmail") == "b@deneme" && (string?)Oku(GoogleAyari(u.Depo), "AccountEmail") == "b@deneme",
                $"{(string?)Oku(u.Yetki, "AccountEmail") ?? "yok"}; Kasım: {string.Join(", ", kasim)}");

            // Aynı hesapla yeniden bağlanma: seçim korunur.
            SahteGoogleHesabi ayni = AHesabi();
            GoogleUygulamasi v = GoogleUygulamasiKur(ayni);
            yoklayicilar.Add(v.Yoklayici);
            List<string> takvimler2 = Secim(v.Depo, "SelectedCalendarIds");
            List<string> listeler2 = Secim(v.Depo, "SelectedTaskListIds");
            takvimler2.Add("a@deneme");
            listeler2.Add("liste1");
            bool hazir2 = AyiEsitle(v.Saglayici, 11, tam: true) && AyiEsitle(v.Saglayici, 10, tam: true);
            var (basari2, ileti2) = YenidenBaglan(v, () => (string?)Oku(v.Yetki, "AccountEmail") == "a@deneme");
            HashSet<string> kayit2 = Kayitlar(v.Saglayici);
            Denetle("aynı hesapla yeniden bağlanınca seçim korunuyor (kapalı takvim kapalı kalıyor), ay belleği yine siliniyor",
                hazir2 && basari2 && takvimler2.SequenceEqual(new[] { "a@deneme" }) && listeler2.SequenceEqual(new[] { "liste1" })
                && AyBellegi(v.Saglayici).Count == 0 && kayit2.SetEquals(new[] { "is-ekim", "gorev-ekim" })
                && (string?)Oku(v.Yetki, "AccountEmail") == "a@deneme",
                $"{ileti2}; seçim [{string.Join(", ", takvimler2)}]; {AyBellegi(v.Saglayici).Count} ay; kayıtlar {string.Join(", ", kayit2)}");

            // Ayarlar penceresi açıksa anahtarlar seçime eşitleniyor (satırlar aynı kalsa da).
            object pencere = GooglePenceresi(u.Depo);
            var kapi = (SahteAyarKapisi)AlanOku(pencere, "_host");
            var panel = (StackPanel)AlanOku(pencere, "CalendarListPanel");
            var kaynaklar = new List<(string Id, string Name)> { ("b@deneme", "B"), ("b-is@deneme", "İş") };
            var eskiSecim = new List<string> { "a@deneme" };
            Cagir(pencere, "BuildSourceList", panel, kaynaklar, eskiSecim);
            bool kapaliydi = GoogleAnahtarlari(panel).All(k => k.IsChecked == false);
            Cagir(pencere, "BuildSourceList", panel, kaynaklar, new List<string>());
            List<CheckBox> anahtarlar = GoogleAnahtarlari(panel);
            Denetle("ayarlar penceresinde anahtarlar \"hepsi\"ne dönen seçimi gösteriyor (yeniden yenileme istenmeden)",
                kapaliydi && anahtarlar.Count == 2 && anahtarlar.All(k => k.IsChecked == true) && kapi.Cagrilar.Count == 0,
                string.Join(", ", anahtarlar.Select(k => k.IsChecked)) + "; " + string.Join(" → ", kapi.Cagrilar));

            Denetle("gerçek tarayıcı açılmadı, onayı sınama verdi", acilan == 3, $"{acilan} kez");
        }
        finally
        {
            acici.SetValue(null, asil);
            foreach (object yoklayici in yoklayicilar)
            {
                ((IDisposable)yoklayici).Dispose();
            }
        }
    }

    // ------------------------------------------------------------------ 19. madde

    /// <summary>Geçici klasördeki günlük dosyalarının metni.</summary>
    private static string Gunluk()
    {
        string klasor = (string)Tip("Kalender.Core.Paths").GetProperty("LogFolder", Hepsi)!.GetValue(null)!;
        return Directory.Exists(klasor)
            ? string.Join("\n", Directory.GetFiles(klasor, "kalender-*.log").Select(File.ReadAllText))
            : string.Empty;
    }

    private static void Madde19()
    {
        Console.WriteLine("19) Bağlıyken istemci kimliği değiştirilince bağlantı uyarısız kopmuyor; ayarlar yeniden bağlanmayı söylüyor");
        SahteGoogleHesabi google = AHesabi();
        var (depo, yetki, _) = GoogleKur(google);
        bool ayniyken = (bool)Oku(yetki, "IstemciKimligiDegisti")!;
        Yaz(GoogleAyari(depo), "ClientId", "yeni-istemci");
        object host = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Tip("Kalender.Core.AppHost"));
        AlanAta(host, "_googleAuth", yetki);
        Denetle("ayarlarda kimlik değişince yetki ve uygulama \"değişti\" diyor, aynıyken demiyor",
            !ayniyken && (bool)Oku(yetki, "IstemciKimligiDegisti")! && (bool)Oku(host, "GoogleIstemciKimligiDegisti")!);

        // Bellekteki erişim anahtarının süresi dolmuş: yenilenmesi gerekiyor.
        int dustu = 0;
        EventInfo olay = yetki.GetType().GetEvent("YetkiDustu", Hepsi)!;
        olay.AddEventHandler(yetki, new Action(() => Interlocked.Increment(ref dustu)));
        const string neden = "İstemci kimliği (Client ID) ayarlarda değiştirilmiş";
        int onceki = Gunluk().Split('\n').Count(s => s.Contains(neden, StringComparison.Ordinal));
        string? anahtar = Task.Run(() => (Task<string?>)Cagir(yetki, "GetAccessTokenAsync", CancellationToken.None)!)
            .GetAwaiter().GetResult();
        List<string> satirlar = Gunluk().Split('\n').Where(s => s.Contains(neden, StringComparison.Ordinal)).ToList();

        Denetle("bağlantı kesildi (Google'a eski izinle sorulmadan), ama kullanıcıya izni düşen bağlantıdaki gibi haber "
            + "veriliyor (uygulama tepside balon gösterir)",
            anahtar is null && !(bool)Oku(yetki, "IsConnected")! && google.AnahtarIstegi == 0 && dustu == 1, $"{dustu} kez haber");
        Denetle("varsayılan günlük düzeyinde de nedeniyle yazılıyor (hata)",
            satirlar.Count == onceki + 1 && satirlar[^1].Contains("[HATA]", StringComparison.Ordinal),
            satirlar.LastOrDefault()?.Trim() ?? "yok");

        // Ayarlar penceresi: bağlıyken kimlik kutusu değiştirilip odak çıkıyor.
        object pencereDeposu = ZeminDeposu("Solid", "#2E3238", "komur");
        Yaz(GoogleAyari(pencereDeposu), "ClientId", IstemciKimligi);
        object pencere = GooglePenceresi(pencereDeposu);
        var kapi = (SahteAyarKapisi)AlanOku(pencere, "_host");
        kapi.Degerler["GoogleConnected"] = true;
        kapi.Degerler["GoogleOnline"] = true;
        kapi.Degerler["GoogleAccountEmail"] = "a@deneme";
        kapi.Degerler["GoogleCalendars"] = Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Google.ProviderCalendar")));
        kapi.Degerler["GoogleTaskLists"] = Activator.CreateInstance(typeof(List<>).MakeGenericType(Tip("Kalender.Google.ProviderTaskList")));
        kapi.Degerler["GoogleIstemciKimligiDegisti"] = false;
        Cagir(pencere, "UpdateGoogleUi");
        string once = Metin(pencere, "GoogleStatusText");

        ((TextBox)AlanOku(pencere, "ClientIdBox")).Text = "yeni-istemci";
        kapi.Degerler["GoogleIstemciKimligiDegisti"] = true;
        Cagir(pencere, "OnGoogleCredentialsChanged", null, null);
        string sonra = Metin(pencere, "GoogleStatusText");
        Denetle("kimlik kutusu değişince durum satırı yeni kimlikle yeniden bağlanmayı söylüyor; değişmeden önce söylemiyor",
            (string?)Oku(GoogleAyari(pencereDeposu), "ClientId") == "yeni-istemci"
            && !once.Contains("Yeniden bağlan", StringComparison.Ordinal)
            && sonra.Contains("İstemci kimliği değişti", StringComparison.Ordinal)
            && sonra.Contains("\"Yeniden bağlan\"a basın", StringComparison.Ordinal),
            sonra.Replace("\n", " / "));
    }

    // ------------------------------------------------------- 25. ve 26. maddeler

    /// <summary>Derlenmiş ayarlar penceresinin (BAML) metni; dizgiler UTF-8 yazılıyor.</summary>
    private static string AyarPenceresiMetni()
    {
        using Stream akis = Uyg.GetManifestResourceStream(Uyg.GetName().Name + ".g.resources")!;
        using var okuyucu = new System.Resources.ResourceReader(akis);
        foreach (DictionaryEntry kayit in okuyucu)
        {
            if ((string)kayit.Key == "ui/settingswindow.baml")
            {
                using var bellek = new MemoryStream();
                ((Stream)kayit.Value!).CopyTo(bellek);
                return Encoding.UTF8.GetString(bellek.ToArray());
            }
        }

        return string.Empty;
    }

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

    private static void Madde25()
    {
        Console.WriteLine("25) Ayarlar, istemci kimliğinin de şifreli saklandığını söylemiyor");
        string metin = AyarPenceresiMetni();
        Denetle("\"İstemci bilgileri\" açıklaması: yalnız gizli anahtar şifreli saklanıyor",
            metin.Contains("sizin projenize aittir. Gizli anahtar bilgisayarınızda şifreli saklanır.", StringComparison.Ordinal)
            && !metin.Contains("aittir ve bilgisayarınızda şifreli saklanır", StringComparison.Ordinal),
            metin.Length == 0 ? "pencerenin derlenmiş metni bulunamadı" : "");
    }

    private static void Madde26()
    {
        Console.WriteLine("26) \"Client ID nasıl alınır?\" 5. adımındaki gizlilik bağlantısı README'deki gibi depo adresi");
        string metin = AyarPenceresiMetni();
        string beniOku = File.ReadAllText(Path.Combine(DepoKoku(), "README.md"));
        Denetle("iki kutuya da README'deki adımdaki gibi depo adresi yazdırılıyor, kaldırılan README bölümüne bağlantı yok",
            metin.Contains("Application home page ve Application privacy policy link kutularına https://github.com/SCgrS/Kalender yazın.",
                StringComparison.Ordinal)
            && !metin.Contains("#verileriniz-nerede-duruyor", StringComparison.Ordinal)
            && beniOku.Contains("link** kutularına `https://github.com/SCgrS/Kalender` yazın.", StringComparison.Ordinal)
            && !beniOku.Contains("#verileriniz-nerede-duruyor", StringComparison.Ordinal),
            metin.Length == 0 ? "pencerenin derlenmiş metni bulunamadı" : "");
    }

    // ------------------------------------------------------------------ 31. madde

    private static void Madde31()
    {
        Console.WriteLine("31) Ekran düzeni değişince istemci kutularına yazılıp kaydedilmemiş değer kalıyor");
        object depo = ZeminDeposu("Solid", "#2E3238", "komur");
        object google = GoogleAyari(depo);
        Yaz(google, "ClientId", "eski-kimlik");
        Yaz(google, "ClientSecret", "eski-sir");
        object pencere = ZeminPenceresi(depo);
        var kimlik = (TextBox)AlanOku(pencere, "ClientIdBox");
        var gizli = (PasswordBox)AlanOku(pencere, "ClientSecretBox");
        bool yuklendi = kimlik.Text == "eski-kimlik" && gizli.Password == "eski-sir";

        // Yeni değerler yapıştırıldı, odak kutuda (kaydedilmedi); bu sırada ikinci ekran çıkarıldı.
        kimlik.Text = "yeni-kimlik";
        gizli.Password = "yeni-sir";
        var kapi = (SahteAyarKapisi)AlanOku(pencere, "_host");
        ((IList)kapi.Ekranlar!).RemoveAt(1);
        Cagir(pencere, "OnMonitorsChanged");
        Bosalt();
        int satir = ((IList)AlanOku(pencere, "_rows")).Count;
        bool korundu = kimlik.Text == "yeni-kimlik" && gizli.Password == "yeni-sir"
            && (string?)Oku(google, "ClientId") == "eski-kimlik" && (string?)Oku(google, "ClientSecret") == "eski-sir";

        // Ayarlardan yükleme (bağlanınca, bağlantıyı kesince) kutuları yine dolduruyor.
        Cagir(pencere, "LoadFromSettings");
        Denetle("ekran satırları tazelendi, kutulardaki kaydedilmemiş değerler duruyor (ayara yazılmadı); ayarlardan yükleme kutuları yine dolduruyor",
            yuklendi && satir == 1 && korundu && kimlik.Text == "eski-kimlik" && gizli.Password == "eski-sir",
            $"{satir} ekran satırı; ekran değişiminden sonra kutuda \"{(korundu ? "yeni-kimlik" : "eski değer")}\"");
    }
}

/// <summary>
/// Ayarlar penceresinin uygulamaya açılan kapısının (IAppearanceHost) sahtesi
/// (KodIncelemesi'ndekine benzer): duvar kâğıdına hiçbir şey yazmaz,
/// ApplyAppearance çağrıları yalnız sayılır; öbür özellikler boş değer verir.
/// </summary>
public class SahteAyarKapisi : DispatchProxy
{
    /// <summary>Ayar deposu (SettingsStore).</summary>
    public object? Ayarlar { get; set; }

    /// <summary>Bağlı ekranlar (MonitorTarget listesi).</summary>
    public object? Ekranlar { get; set; }

    /// <summary>ApplyAppearance kaç kez çağrıldı.</summary>
    public int Uygulama { get; private set; }

    /// <summary>Özellik adı → verilecek değer (ör. GoogleConnected); verilmeyen özellik boş değer verir.</summary>
    public Dictionary<string, object?> Degerler { get; } = new();

    /// <summary>
    /// Ayarlar penceresinin Google için çağırdığı yöntemler, sırasıyla; zorlanmış
    /// yenileme "RefreshGoogleAsync(zorla)" diye yazılır.
    /// </summary>
    public List<string> Cagrilar { get; } = new();

    protected override object? Invoke(MethodInfo? yontem, object?[]? a)
    {
        string ad = yontem!.Name;
        switch (ad)
        {
            case "get_Settings":
                return Ayarlar;

            case "get_Monitors":
                return Ekranlar;

            case "ApplyAppearance":
                Uygulama++;
                Cagrilar.Add(ad);
                return null;

            case "GoogleSecimiDegisti":
                Cagrilar.Add(ad);
                return null;

            case "RefreshGoogleAsync":
                Cagrilar.Add(a?[0] is true ? ad + "(zorla)" : ad);
                return Task.CompletedTask;
        }

        if (ad.StartsWith("add_", StringComparison.Ordinal) || ad.StartsWith("remove_", StringComparison.Ordinal))
        {
            return null;
        }

        if (ad.StartsWith("get_", StringComparison.Ordinal))
        {
            if (Degerler.TryGetValue(ad[4..], out object? deger))
            {
                return deger;
            }

            return yontem.ReturnType.IsValueType ? Activator.CreateInstance(yontem.ReturnType) : null;
        }

        throw new NotSupportedException(ad);
    }
}

/// <summary>
/// Windows'un duvar kâğıdı arayüzünün (IDesktopWallpaper) sahtesi: gerçek
/// masaüstüne dokunmadan yedekleme ve geri yükleme mantığını sınamak için
/// (KodIncelemesi'ndekinin aynısı).
/// </summary>
public class SahteDuvarKagidi : DispatchProxy
{
    /// <summary>Bağlı ekranların aygıt yolları, sırasıyla.</summary>
    public List<string> Aygitlar { get; } = new();

    /// <summary>Aygıt yolu -> ekranda duran duvar kâğıdı.</summary>
    public Dictionary<string, string> Ekranda { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ortak yol (aygıt yolu verilmeden okunan).</summary>
    public string Ortak { get; set; } = string.Empty;

    /// <summary>Windows'un kabul etmediği yollar: SetWallpaper bunlarda hata verir.</summary>
    public HashSet<string> Reddet { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Doğruysa GetWallpaper hata verir.</summary>
    public bool OkumadaHata { get; set; }

    /// <summary>SetWallpaper çağrıları, sırasıyla.</summary>
    public List<(string? Aygit, string Yol)> Yazilanlar { get; } = new();

    public int Konum { get; set; } = 4;

    public uint Renk { get; set; }

    protected override object? Invoke(MethodInfo? yontem, object?[]? a)
    {
        switch (yontem!.Name)
        {
            case "GetMonitorDevicePathCount":
                return (uint)Aygitlar.Count;

            case "GetMonitorDevicePathAt":
                return Aygitlar[(int)(uint)a![0]!];

            case "GetWallpaper":
                if (OkumadaHata)
                {
                    throw new COMException("Duvar kâğıdı okunamadı (deneme).", unchecked((int)0x80004005));
                }

                return a![0] is string aygit ? (Ekranda.TryGetValue(aygit, out string? yol) ? yol : string.Empty) : Ortak;

            case "SetWallpaper":
                string yeni = (string)a![1]!;
                if (Reddet.Contains(yeni))
                {
                    throw new COMException("Dosya bulunamadı (deneme).", unchecked((int)0x80070002));
                }

                Yazilanlar.Add(((string?)a[0], yeni));
                if (a[0] is string hedef)
                {
                    Ekranda[hedef] = yeni;
                }
                else
                {
                    Ortak = yeni;
                    foreach (string ekran in Aygitlar)
                    {
                        Ekranda[ekran] = yeni;
                    }
                }

                return null;

            case "GetBackgroundColor":
                return Renk;

            case "SetBackgroundColor":
                Renk = (uint)a![0]!;
                return null;

            case "GetPosition":
                return Enum.ToObject(yontem.ReturnType, Konum);

            case "SetPosition":
                Konum = Convert.ToInt32(a![0]);
                return null;

            case "Enable":
                return null;

            default:
                throw new NotSupportedException(yontem.Name);
        }
    }
}
