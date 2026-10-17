using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Threading;
using Kalender.Core;
using Kalender.Interop;
using Microsoft.Win32;

namespace Kalender.Desktop;

/// <summary>Bir monitör ve o monitör için üretilmiş tam ekran görüntü.</summary>
internal readonly record struct CanvasScreen(MonitorTarget Monitor, Bitmap Image);

/// <summary>Bir uygulama turunun sonucu.</summary>
/// <param name="Success">Duvar kağıdına yazma yolu çalıştı mı.</param>
/// <param name="Written">Kaç ekranın görüntüsü yenilendi. Sıfır, "değişen bir şey yoktu" demek.</param>
internal readonly record struct CanvasResult(bool Success, int Written);

/// <summary>
/// Takvimi ekrana taşıyan katman: üretilen tam ekran görüntüleri Windows'un
/// gerçek duvar kağıdı olarak uygular.
///
/// Neden çizim penceresi değil de duvar kağıdı?
/// Lively ve Wallpaper Engine tarzı "WorkerW'ye çocuk pencere as" yöntemi
/// Windows 11'in yeni yapılarında çalışmıyor: pencere kuruluyor, görünür
/// görünüyor, çizim çağrıları başarı döndürüyor, ama tek piksel ekrana
/// ulaşmıyor. Kabuğun masaüstünü artık kendi bileşim katmanından çizmesi
/// çocuk GDI pencerelerini devre dışı bırakıyor ve uygulamanın bunu anlamasının
/// bir yolu yok — her adım başarılı görünüyor. Duvar kağıdının kendisine yazmak
/// ise Windows'un desteklediği yol: her sürümde çalışır, simgelerin altında
/// kalır, fareye hiç dokunmaz.
///
/// Bedeli: takvim gerçekten duvar kağıdının bir parçası olur. Gün kutusu ve
/// sağ tık menüsü bundan etkilenmez; ikisi de imleç konumunu kendisi izliyor.
/// </summary>
internal sealed class WallpaperCanvas
{
    /// <summary>Kullanılmayan görüntü dosyalarının silinmeden önce bekletildiği süre.</summary>
    private static readonly TimeSpan FileGrace = TimeSpan.FromMinutes(2);

    /// <summary>Monitör kimliği -> en son yazılan görüntünün özeti.</summary>
    private readonly Dictionary<string, string> _hashes = new(StringComparer.Ordinal);

    /// <summary>
    /// Monitör kimliği -> en son yazılan dosya ve yazarken kullanılan aygıt yolu.
    /// Windows'un gerçekten bunu gösterip göstermediği buradan denetlenir;
    /// dosya temizliği de bu dosyalara dokunmaz.
    /// </summary>
    private readonly Dictionary<string, (string? Aygit, string Dosya)> _sonYazilanlar = new(StringComparer.Ordinal);

    private bool _qualityChecked;

    /// <summary>
    /// Yazımlar durduktan sonra eski görüntüleri silen zamanlayıcı (ilk
    /// yazımda kurulur). Silme önceden yalnız yazım anında yapılıyordu ve
    /// yeni dosyalara dokunmuyordu: renk çarkı ya da kaydırıcı sürüklenirken
    /// saniyede birkaç büyük dosya yazılıyor, sürükleme bitince bunlar bir
    /// sonraki yazıma (çoğu zaman gece yarısına) dek diskte kalıyordu.
    /// </summary>
    private DispatcherTimer? _temizlikZamanlayicisi;

    /// <summary>
    /// Bu süreç duvar kâğıdına yazdı mı (ve o zamandan beri önceki duvar
    /// kâğıdı geri yüklenmedi mi)? Kapanış temizliği ve yazmanın kapatılması
    /// buna bakar.
    /// </summary>
    public bool HasPainted { get; private set; }

    /// <summary>
    /// Bir sonraki turda her şeyin yeniden yazılmasını sağlar. "Yeniden çiz"
    /// menüsü ve ekran yapılandırması değişimi bunu kullanır. Sıkıştırma
    /// kalitesi de yeniden denetlenir: "Eski duvar kâğıdıma dön" onu eski
    /// değerine döndürür; yazma aynı oturumda yeniden açılınca takvim yeniden
    /// başlatılana dek bulanık kalıyordu.
    /// </summary>
    public void Forget()
    {
        _hashes.Clear();
        _qualityChecked = false;
    }

    /// <summary>
    /// Takvim duvar kâğıdından kaldırıldı, önceki duvar kâğıdı geri yüklendi:
    /// ekranda bu sürecin tuttuğu bir görüntü yok. Kapanışta zemin yazılmaz;
    /// yazma yeniden açılınca her şey baştan yazılır.
    /// </summary>
    public void Birak()
    {
        Forget();
        _sonYazilanlar.Clear();
        HasPainted = false;

        // Ekranda ne durduğunu artık bu sınıf bilmiyor (önceki duvar kâğıdını
        // kabul etmeyen ekranda görüntümüz kalmış olabilir); dosyaları geri
        // yükleme ekrana bakarak siliyor. Bekleyen temizlik iptal.
        _temizlikZamanlayicisi?.Stop();
    }

    /// <summary>
    /// Verilen görüntüleri duvar kağıdı olarak uygular. Yalnızca içeriği
    /// değişen ekranlar yeniden yazılır; aynı görüntüyü tekrar yazmak
    /// masaüstünü boşuna sarsıyor ve diske yazıyor.
    /// </summary>
    /// <param name="kaldirirken">
    /// Takvim duvar kâğıdından kaldırılırken (kapanış, geri yükleme) yazılan
    /// takvimsiz zemin mi? Öyleyse önceki duvar kâğıdı yedeklenmez, sıkıştırma
    /// kalitesine dokunulmaz: geri yüklemenin hemen ardından yeni bir yedek
    /// açılır ya da kalite yeniden 100'e çekilirdi. Yerleşim de yalnız "Yay"daysa
    /// değiştirilir (bkz. <see cref="DoldurYerlesimineCekilsinMi"/>). Dosyanın
    /// adı takvimsiz olduğunu söyler (bkz. <see cref="TakvimGosterenler"/>).
    /// </param>
    public CanvasResult Apply(IReadOnlyList<CanvasScreen> screens, bool kaldirirken = false)
    {
        if (screens.Count == 0)
        {
            return new CanvasResult(true, 0);
        }

        // Artık bağlı olmayan ekranların kaydı denetimi yanıltmasın.
        foreach (string eski in _sonYazilanlar.Keys.Where(k => screens.All(s => s.Monitor.StableId != k)).ToList())
        {
            _sonYazilanlar.Remove(eski);
        }

        try
        {
            // Hangi ekranlar gerçekten değişti? Önce bunu bilmemiz gerekiyor:
            // değişen yoksa duvar kağıdı arayüzüne hiç dokunmuyoruz.
            var pending = new List<(CanvasScreen Screen, string Hash)>();
            foreach (CanvasScreen screen in screens)
            {
                string hash = HashOf(screen.Image);
                if (_hashes.TryGetValue(screen.Monitor.StableId, out string? previous)
                    && string.Equals(previous, hash, StringComparison.Ordinal))
                {
                    continue;
                }

                pending.Add((screen, hash));
            }

            if (pending.Count == 0)
            {
                return new CanvasResult(true, 0);
            }

            // Hiç yazılmamış ya da Forget ile sıfırlanmışsa (açılış, ayar
            // değişikliği, ekran değişimi, duvar kağıdının elden gitmesi) tam yol.
            bool tamYol = _hashes.Count == 0;

            var wallpaper = DuvarKagidiKayitlari.Ac();
            if (!kaldirirken)
            {
                WallpaperService.EnsureBackup(wallpaper);
                EnsureImportQuality();
            }

            // Görüntüler ekranın tam piksel ölçüsünde üretiliyor; "Doldur"
            // yerleşimiyle bire bir oturuyorlar. Zaten öyleyse dokunulmuyor.
            if (DoldurYerlesimineCekilsinMi(wallpaper.GetPosition(), kaldirirken))
            {
                wallpaper.SetPosition(DesktopWallpaperPosition.Fill);
            }

            IReadOnlyList<string> devicePaths = ReadDevicePaths(wallpaper);
            int written = 0;

            foreach ((CanvasScreen screen, string hash) in pending)
            {
                string? id = MatchDevicePath(wallpaper, devicePaths, screen.Monitor);
                if (id is null && screens.Count > 1)
                {
                    // Kimliği bulamadığımız ekrana yazmak yerine atlıyoruz:
                    // null geçmek bütün ekranlara aynı görüntüyü basardı.
                    Log.Warn($"Monitörün duvar kâğıdı kimliği eşleşmedi, atlandı: {screen.Monitor}");
                    continue;
                }

                string file = DosyaYolu(screen.Monitor.StableId, kaldirirken);
                SaveBmp(screen.Image, file);
                wallpaper.SetWallpaper(id, file);

                _hashes[screen.Monitor.StableId] = hash;
                _sonYazilanlar[screen.Monitor.StableId] = (id, file);
                written++;

                Log.Debug($"Duvar kâğıdı yazıldı: {screen.Monitor} -> {Path.GetFileName(file)}");
            }

            if (written > 0)
            {
                // Resim kipi kapalıysa (kullanıcı düz renk duvar kağıdı
                // kullanıyorsa) yazdığımız görüntü ekrana hiç çıkmaz, o yüzden
                // açıyoruz. Bu çağrı ilk görüntü yazıldıktan SONRA yapılmalı:
                // ortada hiç resim yokken kabuk açacak dosyayı bulamıyor ve
                // 0x80070002 ile hata veriyor.
                //
                // Yalnız tam yolda çağrılıyor: kip zaten açıkken de Windows
                // duvar kağıdını baştan işliyordu (~280 ms, ekranda ikinci bir
                // yenilenme); ay düğmeleriyle gezinirken geçişi yavaşlatıyordu.
                // Kullanıcı sonradan düz renge geçerse ekrandaki görüntü bizim
                // olmaz, nöbet denetimi Forget ile tam yolu yeniden açar.
                if (tamYol)
                {
                    WallpaperService.EnableImages(wallpaper);
                }

                HasPainted = true;

                // Takvim kaldırılırken ekranda bu sürecin yazmadığı görüntüler de
                // durabilir (açılışta, önceki oturumun başka ekrana yazdığı
                // takvimsiz zemin); temizlik onları da ekrana bakarak korur.
                CleanUnusedFiles(kaldirirken ? wallpaper : null);
                TemizligiPlanla();
            }

            return new CanvasResult(true, written);
        }
        catch (Exception ex)
        {
            Log.Error("Takvim duvar kâğıdına yazılamadı.", ex);
            return new CanvasResult(false, 0);
        }
    }

    /// <summary>
    /// Yazmadan önce yerleşim "Doldur"a çekilsin mi? Takvim kaldırılırken yalnız
    /// "Yay"dan çekilir: ekran boyutundaki görüntü Doldur, Sığdır, Ortala, Uzat
    /// ve Döşe'de aynı görünür. Yerleşim bütün ekranlar için ortak; geri yükleme
    /// az önce kullanıcının yerleşimini (Sığdır, Ortala) geri koymuşken bir
    /// ekrana takvimsiz zemin yazmak onu önceden Doldur'a çekiyordu, geri
    /// yüklenen ekranlar yanlış yerleşimle kalıyordu.
    /// </summary>
    internal static bool DoldurYerlesimineCekilsinMi(DesktopWallpaperPosition konum, bool kaldirirken)
        => konum != DesktopWallpaperPosition.Fill && (!kaldirirken || konum == DesktopWallpaperPosition.Span);

    /// <summary>
    /// Windows her ekranda gerçekten en son yazdığımız görüntüyü mü gösteriyor?
    ///
    /// İçeriği aynı olan görüntü yeniden yazılmıyor. Ama duvar kağıdını Windows
    /// da değiştirebiliyor: açılışta kendi önbelleğindeki eski görüntüyü
    /// koyabiliyor, tema eşitlemesi ya da kullanıcı başka bir resim seçebiliyor.
    /// Önceden Kalender bunu fark etmiyordu; takvim bir sonraki değişikliğe
    /// (yeni etkinlik, gün dönümü) kadar ekrana gelmiyordu. Emin olamadığımız
    /// durumda true döner, yanlış alarmla boşuna yazmayalım.
    /// </summary>
    public bool EkrandakilerBizimMi()
    {
        if (_sonYazilanlar.Count == 0)
        {
            return true;
        }

        try
        {
            var wallpaper = DuvarKagidiKayitlari.Ac();

            foreach ((string? aygit, string dosya) in _sonYazilanlar.Values)
            {
                // Aygıt yolu olmadan (tek ekranda ortak olarak) yazılan görüntü
                // denetlenemiyor: ekran başına duvar kağıdında Windows ortak
                // yolu boş döndürüyor.
                if (aygit is null)
                {
                    continue;
                }

                string gorunen = wallpaper.GetWallpaper(aygit) ?? string.Empty;
                if (!string.Equals(gorunen, dosya, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("Ekranda Kalender'in görüntüsü yok ("
                        + (gorunen.Length == 0 ? "boş" : Path.GetFileName(gorunen))
                        + "); yeniden yazılacak.");
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıdı okunamadı: " + ex.Message);
            return true;
        }
    }

    /// <summary>
    /// Bu sürecin en son yazdığı görüntü hâlâ hangi ekranlarda duruyor?
    /// Takvim duvar kâğıdından kaldırılırken (kapanış, geri yükleme) takvimsiz
    /// zemin yalnız bu ekranlara yazılır: önceki duvar kâğıdı geri gelen ya da
    /// kullanıcının başka bir duvar kâğıdı seçtiği ekrana dokunulmaz. Okunamayan
    /// ekran sayılmaz; emin olamadığımız yere yazmıyoruz.
    /// </summary>
    /// <returns>Monitör kimlikleri.</returns>
    public HashSet<string> EkrandaDuranlar()
    {
        var duranlar = new HashSet<string>(StringComparer.Ordinal);
        if (_sonYazilanlar.Count == 0)
        {
            return duranlar;
        }

        try
        {
            var wallpaper = DuvarKagidiKayitlari.Ac();

            foreach (KeyValuePair<string, (string? Aygit, string Dosya)> kayit in _sonYazilanlar)
            {
                try
                {
                    // Aygıt yolu olmadan yalnız tek ekranda yazılıyor; ortak yol o ekranınki.
                    string gorunen = wallpaper.GetWallpaper(kayit.Value.Aygit) ?? string.Empty;
                    if (string.Equals(gorunen, kayit.Value.Dosya, StringComparison.OrdinalIgnoreCase))
                    {
                        duranlar.Add(kayit.Key);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"Ekrandaki duvar kâğıdı okunamadı ({kayit.Key}): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıdı okunamadı: " + ex.Message);
        }

        return duranlar;
    }

    /// <summary>
    /// Hangi ekranlarda takvimin donmuş bir görüntüsü duruyor? Takvim duvar
    /// kâğıdından kaldırılırken takvimsiz zemin bu ekranlara yazılır. Bu sürecin
    /// ne yazdığına değil ekrana bakılır: açılışta yazma kapalıyken önceki bir
    /// sürümün bıraktığı takvimi bu süreç yazmadı; önceden o takvim, önceki duvar
    /// kâğıdı geri konamayınca ekranda kalıyordu. Takvimsiz zemin sayılmaz: onu
    /// yeniden yazmak bir şey değiştirmez, her açılışta boşuna yazılırdı. Windows'ta
    /// resim kipi kapalıysa (düz renk) ekranda görüntü yoktur. Okunamayan ekran
    /// sayılmaz; emin olamadığımız yere yazmıyoruz.
    /// </summary>
    /// <param name="ekranlar">Bakılacak ekranlar.</param>
    /// <param name="wallpaper">Ekrandakileri okumak için; null ise burada açılır.</param>
    /// <returns>Monitör kimlikleri.</returns>
    internal static HashSet<string> TakvimGosterenler(IReadOnlyList<MonitorTarget> ekranlar, IDesktopWallpaper? wallpaper = null)
    {
        var gosterenler = new HashSet<string>(StringComparer.Ordinal);
        if (ekranlar.Count == 0 || WallpaperService.ResimKipiKapali())
        {
            return gosterenler;
        }

        try
        {
            wallpaper ??= DuvarKagidiKayitlari.Ac();
            IReadOnlyList<string> devicePaths = ReadDevicePaths(wallpaper);

            foreach (MonitorTarget ekran in ekranlar)
            {
                try
                {
                    // Aygıt yolu bulunamazsa ortak yol yalnız tek ekranda o
                    // ekranınkidir (yazımdaki gibi).
                    string? id = MatchDevicePath(wallpaper, devicePaths, ekran);
                    if (id is null && ekranlar.Count > 1)
                    {
                        continue;
                    }

                    string? gorunen = wallpaper.GetWallpaper(id);
                    if (WallpaperService.ZeminDosyasiMi(gorunen) && !TakvimsizZeminMi(gorunen))
                    {
                        gosterenler.Add(ekran.StableId);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug($"Ekrandaki duvar kâğıdı okunamadı ({ekran.StableId}): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıdı okunamadı: " + ex.Message);
        }

        return gosterenler;
    }

    // --------------------------------------------------------------- dosyalar

    /// <summary>Takvimsiz zeminin dosya adının sonu (uzantıdan önce).</summary>
    internal const string TakvimsizEki = "-takvimsiz";

    /// <summary>Dosya, takvim kaldırılırken yazılan takvimsiz zemin mi?</summary>
    internal static bool TakvimsizZeminMi(string? yol)
        => !string.IsNullOrWhiteSpace(yol)
            && Path.GetFileNameWithoutExtension(yol).EndsWith(TakvimsizEki, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Bir monitörün yeni görüntü dosyası. Her yazışta yeni bir ad veriliyor.
    ///
    /// Windows duvar kağıdını yola, dosya boyutuna ve ölçüye göre önbelleğe
    /// alıyor; sıkıştırmasız BMP'nin boyutu da içerik değişse bile aynı
    /// kalıyor. Aynı yola farklı içerik yazınca Windows eski görüntüyü
    /// göstermeye devam edebiliyor, aynı yolu yeniden vermek de ekranı
    /// tazelemiyor. Önceden iki ad (a/b) arasında gidip geliniyordu ama her
    /// açılışta yine "a"dan başlandığı için bir önceki oturumun dosyasıyla
    /// çakışılabiliyordu. Eski dosyaları <see cref="CleanUnusedFiles"/> siler.
    /// Takvimsiz zeminin adı <see cref="TakvimsizEki"/> ile biter.
    /// </summary>
    private static string DosyaYolu(string monitorId, bool takvimsiz)
    {
        Directory.CreateDirectory(Paths.CanvasFolder);

        return Path.Combine(Paths.CanvasFolder,
            $"zemin-{ShortId(monitorId)}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}{(takvimsiz ? TakvimsizEki : string.Empty)}.bmp");
    }

    /// <summary>
    /// Görüntüyü sıkıştırmasız, 24 bitlik BMP olarak yazar.
    ///
    /// Neden PNG değil: 2560 × 1440 bir ekranda PNG kodlaması ~140 ms, bu yazım
    /// ~10 ms sürüyor; renk seçerken aradaki fark doğrudan gecikme olarak
    /// hissediliyordu. Windows görüntüyü zaten kendi önbelleğine yeniden
    /// kodladığı için sıkıştırmanın masaüstüne yararı yok. Dosya büyük; ekranda
    /// durmayanlar son yazımdan birkaç dakika sonra silinir (bkz.
    /// <see cref="TemizligiPlanla"/>). 24 bit, çünkü 32 bitlik BMP'nin
    /// dördüncü baytını bazı okuyucular saydamlık sanıyor.
    ///
    /// Görüntü kendi 32 bitlik biçiminde okunup satır satır 24 bite burada
    /// çevriliyor. Önceden LockBits'ten doğrudan 24 bit isteniyordu; GDI+ bunun
    /// için görüntünün tamamının 24 bitlik bir kopyasını açıyordu (2560 × 1440'ta
    /// 11 MB). Dosya arabelleği de 1 MB'tan 64 KB'a indi.
    /// </summary>
    private static void SaveBmp(Bitmap image, string path)
    {
        const int HeaderSize = 14 + 40;
        int width = image.Width;
        int height = image.Height;
        int stride = ((width * 3) + 3) & ~3;
        int imageSize = stride * height;

        // Görüntü baştan sona opak; 24 bite çevirirken ön-çarpılmış alfa bir şey
        // değiştirmez, dördüncü bayt atılıyor. Görüntü zaten bu biçimde üretildiği
        // için LockBits kopya açmıyor.
        BitmapData data = image.LockBits(
            new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);

        try
        {
            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            using var w = new BinaryWriter(file);

            // BITMAPFILEHEADER
            w.Write((ushort)0x4D42); // "BM"
            w.Write(HeaderSize + imageSize);
            w.Write(0);
            w.Write(HeaderSize);

            // BITMAPINFOHEADER; yükseklik pozitif, yani satırlar aşağıdan yukarıya.
            w.Write(40);
            w.Write(width);
            w.Write(height);
            w.Write((ushort)1);
            w.Write((ushort)24);
            w.Write(0); // BI_RGB, sıkıştırmasız
            w.Write(imageSize);
            w.Write(3780); // 96 DPI, metre başına piksel
            w.Write(3780);
            w.Write(0);
            w.Write(0);

            byte[] source = new byte[width * 4];
            byte[] row = new byte[stride];
            for (int y = height - 1; y >= 0; y--)
            {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), source, 0, source.Length);

                // BGRA -> BGR; satır sonundaki dolgu baytları sıfır kalıyor.
                for (int s = 0, d = 0; d < width * 3; s += 4, d += 3)
                {
                    row[d] = source[s];
                    row[d + 1] = source[s + 1];
                    row[d + 2] = source[s + 2];
                }

                w.Write(row);
            }
        }
        finally
        {
            image.UnlockBits(data);
        }
    }

    /// <summary>Monitör kimliğinden kısa ve dosya adında kullanılabilir bir imza (FNV-1a).</summary>
    private static string ShortId(string monitorId)
    {
        uint hash = 2166136261;
        foreach (char c in monitorId)
        {
            hash = (hash ^ c) * 16777619;
        }

        return hash.ToString("x8");
    }

    /// <summary>
    /// Artık kullanılmayan görüntüleri siler (bkz. <see cref="KullanilmayanlariSil"/>).
    /// </summary>
    /// <param name="wallpaper">Verilirse ekranda duran bütün dosyalar da korunur.</param>
    private void CleanUnusedFiles(IDesktopWallpaper? wallpaper = null)
        => KullanilmayanlariSil(
            Paths.CanvasFolder, _sonYazilanlar.Values.Select(v => v.Dosya), wallpaper, DateTime.UtcNow - FileGrace);

    /// <summary>
    /// Artık kullanılmayan görüntüleri siler. Windows dosyayı uygulamadan
    /// hemen sonra hâlâ okuyor olabildiği için yeni yazılanlara dokunmuyoruz.
    /// Ekranda duran görüntüler de korunur: Explorer yeniden başladığında ya
    /// da çözünürlük değiştiğinde Windows onları yeniden okuyor.
    /// </summary>
    /// <param name="klasor">Zemin klasörü.</param>
    /// <param name="sonYazilanlar">Bu sürecin ekranlara en son yazdığı dosyalar.</param>
    /// <param name="wallpaper">
    /// Verilirse ekranda duran bütün dosyalar da korunur; ekran okunamazsa o
    /// turda hiçbir dosya silinmez. Takvim kaldırılırken verilir: ekranda bu
    /// sürecin yazmadığı bir zemin durabiliyor, önceden silinebiliyordu.
    /// </param>
    /// <param name="esik">Bundan sonra yazılan dosyalara dokunulmaz.</param>
    internal static void KullanilmayanlariSil(
        string klasor, IEnumerable<string> sonYazilanlar, IDesktopWallpaper? wallpaper, DateTime esik)
    {
        try
        {
            var ekrandakiler = new HashSet<string>(sonYazilanlar, StringComparer.OrdinalIgnoreCase);
            if (wallpaper is not null)
            {
                HashSet<string>? gorunenler = WallpaperService.EkrandakiDosyalar(wallpaper);
                if (gorunenler is null)
                {
                    return;
                }

                ekrandakiler.UnionWith(gorunenler);
            }

            // Uzantısız: 1.4.4'e kadar yazılan PNG'ler de temizlensin.
            foreach (string file in Directory.EnumerateFiles(klasor, "zemin-*"))
            {
                if (!ekrandakiler.Contains(file) && File.GetLastWriteTimeUtc(file) < esik)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Eski zemin dosyaları silinemedi: " + ex.Message);
        }
    }

    /// <summary>
    /// Son yazımdan bekleme süresi kadar sonra temizliği bir kez daha yapar;
    /// her yazımda süre baştan başlar. Peş peşe yazılan dosyalar böylece
    /// sürükleme bitince birkaç dakika içinde silinir; ekranda duran görüntü
    /// yine korunur.
    /// </summary>
    private void TemizligiPlanla()
    {
        if (_temizlikZamanlayicisi is null)
        {
            var zamanlayici = new DispatcherTimer(DispatcherPriority.Background)
            {
                // Küçük pay: süre dolunca en son yazılan dosya da bekleme süresini aşmış olur.
                Interval = FileGrace + TimeSpan.FromSeconds(5),
            };
            zamanlayici.Tick += (_, _) =>
            {
                zamanlayici.Stop();
                CleanUnusedFiles();
            };
            _temizlikZamanlayicisi = zamanlayici;
        }

        _temizlikZamanlayicisi.Stop();
        _temizlikZamanlayicisi.Start();
    }

    // -------------------------------------------------------------- monitörler

    private static IReadOnlyList<string> ReadDevicePaths(IDesktopWallpaper wallpaper)
    {
        var list = new List<string>();

        try
        {
            uint count = wallpaper.GetMonitorDevicePathCount();
            for (uint i = 0; i < count; i++)
            {
                string id = wallpaper.GetMonitorDevicePathAt(i);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    list.Add(id);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Monitör aygıt yolları okunamadı.", ex);
        }

        return list;
    }

    /// <summary>
    /// Monitörün duvar kağıdı arayüzündeki karşılığını bulur.
    ///
    /// Birinci yol kimlik karşılaştırması: <see cref="MonitorTarget.StableId"/>
    /// da aygıt arayüzü yolu olduğu için normalde bire bir tutuyor. Tutmazsa
    /// (kimliği okunamamış bir ekran, aygıt yolu biçimi değişmiş bir sürüm)
    /// ekranın dikdörtgenine bakıyoruz. O da tutmazsa null dönüyoruz; çağıran
    /// taraf null'ı yalnızca tek ekranlı kurulumda kullanıyor.
    /// </summary>
    private static string? MatchDevicePath(
        IDesktopWallpaper wallpaper, IReadOnlyList<string> devicePaths, MonitorTarget monitor)
    {
        foreach (string id in devicePaths)
        {
            if (string.Equals(id, monitor.StableId, StringComparison.OrdinalIgnoreCase))
            {
                return id;
            }
        }

        foreach (string id in devicePaths)
        {
            try
            {
                int hr = wallpaper.GetMonitorRECT(id, out NativeMethods.RECT rect);
                if (hr == 0
                    && rect.Left == monitor.Bounds.Left && rect.Top == monitor.Bounds.Top
                    && rect.Right == monitor.Bounds.Right && rect.Bottom == monitor.Bounds.Bottom)
                {
                    return id;
                }
            }
            catch (Exception ex)
            {
                Log.Debug($"Monitör dikdörtgeni okunamadı ({id}): {ex.Message}");
            }
        }

        return null;
    }

    // ------------------------------------------------------------ görüntü özeti

    /// <summary>
    /// Görüntünün ham piksellerinin özeti. Kodlamadan önce hesaplanıyor:
    /// değişmemiş bir ekran için PNG üretme maliyetine hiç girmiyoruz.
    /// </summary>
    private static string HashOf(Bitmap image)
    {
        BitmapData data = image.LockBits(
            new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, image.PixelFormat);

        try
        {
            int uzunluk = Math.Abs(data.Stride);
            byte[] satir = new byte[uzunluk];

            using var sha = SHA256.Create();
            for (int y = 0; y < data.Height; y++)
            {
                // Scan0 her zaman ilk satırı gösterir; Stride negatifse sonraki
                // satırlar geriye doğru gider, çarpım yine doğru adresi verir.
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), satir, 0, uzunluk);
                sha.TransformBlock(satir, 0, uzunluk, null, 0);
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return Convert.ToHexString(sha.Hash ?? Array.Empty<byte>());
        }
        finally
        {
            image.UnlockBits(data);
        }
    }

    // --------------------------------------------------------- görüntü kalitesi

    /// <summary>
    /// Windows duvar kağıdını kendi önbelleğine JPEG olarak kopyalıyor ve
    /// varsayılan kalite (85) ince yazıların kenarında gözle görülür bulanıklık
    /// bırakıyor. Takvim baştan sona yazı olduğu için kaliteyi 100'e çekiyoruz.
    /// Önceki değer yedeğe yazılır; "eski duvar kağıdıma dön" ile geri gelir.
    /// </summary>
    private void EnsureImportQuality()
    {
        if (_qualityChecked)
        {
            return;
        }

        _qualityChecked = true;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
            if (key is null)
            {
                return;
            }

            if (key.GetValue("JPEGImportQuality") is int mevcut && mevcut >= 100)
            {
                return;
            }

            key.SetValue("JPEGImportQuality", 100, RegistryValueKind.DWord);
            Log.Info("Duvar kâğıdı sıkıştırma kalitesi 100'e çekildi (takvim yazısı keskin çıksın diye).");
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kâğıdı sıkıştırma kalitesi ayarlanamadı: " + ex.Message);
        }
    }
}
