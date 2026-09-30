using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

    /// <summary>Bu süreç duvar kağıdına hiç yazdı mı? Kapanış temizliği buna bakar.</summary>
    public bool HasPainted { get; private set; }

    /// <summary>
    /// Bir sonraki turda her şeyin yeniden yazılmasını sağlar. "Yeniden çiz"
    /// menüsü ve ekran yapılandırması değişimi bunu kullanır.
    /// </summary>
    public void Forget() => _hashes.Clear();

    /// <summary>
    /// Verilen görüntüleri duvar kağıdı olarak uygular. Yalnızca içeriği
    /// değişen ekranlar yeniden yazılır; aynı görüntüyü tekrar yazmak
    /// masaüstünü boşuna sarsıyor ve diske yazıyor.
    /// </summary>
    public CanvasResult Apply(IReadOnlyList<CanvasScreen> screens)
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

            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            WallpaperService.EnsureBackup(wallpaper);
            EnsureImportQuality();

            // Görüntüler ekranın tam piksel ölçüsünde üretiliyor; "Doldur"
            // yerleşimiyle bire bir oturuyorlar. Zaten öyleyse dokunulmuyor.
            if (wallpaper.GetPosition() != DesktopWallpaperPosition.Fill)
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
                    Log.Warn($"Monitörün duvar kağıdı kimliği eşleşmedi, atlandı: {screen.Monitor}");
                    continue;
                }

                string file = DosyaYolu(screen.Monitor.StableId);
                SaveBmp(screen.Image, file);
                wallpaper.SetWallpaper(id, file);

                _hashes[screen.Monitor.StableId] = hash;
                _sonYazilanlar[screen.Monitor.StableId] = (id, file);
                written++;

                Log.Debug($"Duvar kağıdı yazıldı: {screen.Monitor} -> {Path.GetFileName(file)}");
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
                CleanUnusedFiles();
            }

            return new CanvasResult(true, written);
        }
        catch (Exception ex)
        {
            Log.Error("Takvim duvar kağıdına yazılamadı.", ex);
            return new CanvasResult(false, 0);
        }
    }

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
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();

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
            Log.Debug("Ekrandaki duvar kağıdı okunamadı: " + ex.Message);
            return true;
        }
    }

    // --------------------------------------------------------------- dosyalar

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
    /// </summary>
    private static string DosyaYolu(string monitorId)
    {
        Directory.CreateDirectory(Paths.CanvasFolder);

        return Path.Combine(Paths.CanvasFolder,
            $"zemin-{ShortId(monitorId)}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.bmp");
    }

    /// <summary>
    /// Görüntüyü sıkıştırmasız, 24 bitlik BMP olarak yazar.
    ///
    /// Neden PNG değil: 2560 × 1440 bir ekranda PNG kodlaması ~140 ms, bu yazım
    /// ~10 ms sürüyor; renk seçerken aradaki fark doğrudan gecikme olarak
    /// hissediliyordu. Windows görüntüyü zaten kendi önbelleğine yeniden
    /// kodladığı için sıkıştırmanın masaüstüne yararı yok. Dosya büyük ama her
    /// ekran için en fazla iki tane durur. 24 bit, çünkü 32 bitlik BMP'nin
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
    /// Artık kullanılmayan görüntüleri siler. Windows dosyayı uygulamadan
    /// hemen sonra hâlâ okuyor olabildiği için yeni yazılanlara dokunmuyoruz.
    /// Ekranda duran görüntüler de korunur: Explorer yeniden başladığında ya
    /// da çözünürlük değiştiğinde Windows onları yeniden okuyor.
    /// </summary>
    private void CleanUnusedFiles()
    {
        try
        {
            DateTime esik = DateTime.UtcNow - FileGrace;
            var ekrandakiler = new HashSet<string>(
                _sonYazilanlar.Values.Select(v => v.Dosya), StringComparer.OrdinalIgnoreCase);

            // Uzantısız: 1.4.4'e kadar yazılan PNG'ler de temizlensin.
            foreach (string file in Directory.EnumerateFiles(Paths.CanvasFolder, "zemin-*"))
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
                NativeMethods.RECT rect = wallpaper.GetMonitorRECT(id);
                if (rect.Left == monitor.Bounds.Left && rect.Top == monitor.Bounds.Top
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
            Log.Info("Duvar kağıdı sıkıştırma kalitesi 100'e çekildi (takvim yazısı keskin çıksın diye).");
        }
        catch (Exception ex)
        {
            Log.Debug("Duvar kağıdı sıkıştırma kalitesi ayarlanamadı: " + ex.Message);
        }
    }
}
