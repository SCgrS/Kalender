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
    /// Monitör kimliği -> ekran, yazarken izlenen yol (bkz.
    /// <see cref="DuvarKagidiKayitlari.Coz"/>) ve en son yazılan dosya.
    /// Windows'un gerçekten bunu gösterip göstermediği buradan denetlenir;
    /// dosya temizliği de bu dosyalara dokunmaz.
    /// </summary>
    private readonly Dictionary<string, (MonitorTarget Ekran, DuvarKagidiHedefi Hedef, string Dosya)> _sonYazilanlar
        = new(StringComparer.Ordinal);

    /// <summary>
    /// Monitör kimliği -> nöbet denetiminde görülen, yazımdakinden farklı yol.
    /// Yol ancak iki ardışık denetimde aynı farklılık görülünce değişmiş
    /// sayılır (bkz. <see cref="EkrandakilerBizimMi"/>).
    /// </summary>
    private readonly Dictionary<string, DuvarKagidiHedefi> _bekleyenDegisimler = new(StringComparer.Ordinal);

    /// <summary>Son yazımda hesaba katılan bağlı ekran sayısı; denetimler aynı sayıyla çözer.</summary>
    private int _yazimdakiEkranSayisi;

    /// <summary>
    /// Windows'un duvar kâğıdı ekran listesi tutarsız diye günlüğe yazıldı mı?
    /// Bozukluk dönemi başına bir kez yazılır; liste düzelince de bir kez.
    /// </summary>
    private bool _listeBozukYazildi;

    /// <summary>
    /// Temizlikte ekranda duranlar okunamadı diye günlüğe yazıldı mı? Okunamama
    /// dönemi başına bir kez (bkz. <see cref="CleanUnusedFiles"/>).
    /// </summary>
    private bool _temizlikOkunamadiYazildi;

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
    /// Bağlı ekran sayısı; masaüstü katmanı her yazımdan önce verir. Yazıma
    /// bütün ekranlar gelmeyebilir: takvim kaldırılırken takvimsiz zemin yalnız
    /// takvimi gösteren ekranlara yazılıyor. Tek ekranlı düzende aygıt yolu
    /// verilmeden yazmak (bütün ekranlara) yalnız gerçekten tek ekran varken
    /// doğru. Sıfırsa yazılan ekran sayısı kullanılır.
    /// </summary>
    public int BagliEkranSayisi { get; set; }

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
        _bekleyenDegisimler.Clear();
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
            _bekleyenDegisimler.Remove(eski);
        }

        try
        {
            // Hangi ekranlar gerçekten değişti? Önce bunu bilmemiz gerekiyor:
            // değişen yoksa duvar kağıdı arayüzüne hiç dokunmuyoruz.
            var pending = new List<(CanvasScreen Screen, string Hash)>();
            var ozetler = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CanvasScreen screen in screens)
            {
                string hash = HashOf(screen.Image);
                ozetler[screen.Monitor.StableId] = hash;
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

            // Her ekranın yazım yolu Windows'un listesindeki canlı kayıtlardan
            // seçilir (bkz. DuvarKagidiKayitlari.Coz); hayalet kayda yazılan
            // görüntü hatasız kabul ediliyor ama ekrana çıkmıyor. Liste
            // okunamazsa boş sayılır: tek ekranda aygıt yolu verilmeden yazılır,
            // çok ekranda hiçbir ekran yazılmaz (önceden de böyleydi).
            IReadOnlyList<DuvarKagidiKaydi>? okunan = DuvarKagidiKayitlari.Oku(wallpaper);
            if (okunan is null)
            {
                Log.Warn("Monitör aygıt yolları okunamadı.");
            }

            IReadOnlyList<DuvarKagidiKaydi> kayitlar = okunan ?? Array.Empty<DuvarKagidiKaydi>();
            int bagli = BagliEkranSayisi > 0 ? BagliEkranSayisi : screens.Count;
            _yazimdakiEkranSayisi = bagli;

            var hedefler = screens.Select(s => (Screen: s, Hedef: DuvarKagidiKayitlari.Coz(kayitlar, s.Monitor, bagli))).ToList();
            var atlananlar = new List<MonitorTarget>();
            List<(CanvasScreen Screen, DuvarKagidiHedefi Hedef)> sira = YazimSirasi(
                hedefler, pending.Select(p => p.Screen.Monitor.StableId).ToHashSet(StringComparer.Ordinal), bagli, atlananlar);
            int written = 0;

            // Windows görüntüyü ~270 ms'de işliyor; yazımdan hemen sonra okumak
            // yanıltır. Yazılanın ekranda durduğunu nöbet denetimi doğrular.
            foreach ((CanvasScreen screen, DuvarKagidiHedefi hedef) in sira)
            {
                string file = DosyaYolu(screen.Monitor.StableId, kaldirirken);
                SaveBmp(screen.Image, file);
                wallpaper.SetWallpaper(hedef.Tur == HedefTuru.Kimlik ? hedef.Kimlik : null, file);

                _hashes[screen.Monitor.StableId] = ozetler[screen.Monitor.StableId];
                _sonYazilanlar[screen.Monitor.StableId] = (screen.Monitor, hedef, file);
                _bekleyenDegisimler.Remove(screen.Monitor.StableId);
                written++;

                Log.Debug($"Duvar kâğıdı yazıldı: {screen.Monitor} -> {Path.GetFileName(file)} ({YolAdi(hedef)})");
            }

            // Yazılamayan ekran: eski kaydı yerine şimdiki yazım yolu, dosyası
            // boş olarak tutulur. Eski kayıt kalsaydı liste değişti diye her
            // denetimde yeniden yazım istenirdi, o ekran yine yazılamazdı; kayıt
            // hiç tutulmasaydı ekranın kaydı sonradan canlı olunca (dizüstüne
            // takılan haricî ekranı Windows bir an bağlı saymıyor) nöbet bunu
            // görmez, ekranda bir sonraki değişikliğe dek dünkü takvim kalırdı.
            // Nöbet bu ekranın yalnız yazım yoluna bakar (bkz. EkraniDenetle).
            foreach (MonitorTarget atlanan in atlananlar)
            {
                DuvarKagidiHedefi hedef = hedefler.First(h => h.Screen.Monitor.StableId == atlanan.StableId).Hedef;
                _sonYazilanlar[atlanan.StableId] = (atlanan, hedef, string.Empty);
                _bekleyenDegisimler.Remove(atlanan.StableId);
            }

            ListeDurumunuYaz(okunan, sira, atlananlar);

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

                // Temizlik ekranda duran her dosyayı korur (bkz.
                // KullanilmayanlariSil). Önceden olağan yazımda ekrana bakılmıyor,
                // yalnız bu sürecin son yazdıkları korunuyordu: Explorer'ın
                // listesi bozukken (2026-10-17) ekranda duran görüntü silindi.
                CleanUnusedFiles(wallpaper);
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
    /// Bu turda hangi ekranların, hangi sırayla yazılacağı.
    ///
    /// Olağan durumda değişen ekranlar kendi yollarıyla yazılır; kaydı
    /// bulunamayan ekran atlanır. Çok ekranda bir ekranın kaydı kimliksizse ona
    /// yalnız aygıt yolu verilmeden ulaşılabiliyor; bu da bütün ekranlara aynı
    /// görüntüyü basar. Bu yüzden önce o ekran aygıt yolu verilmeden, ardından
    /// öbür bütün ekranlar (değişmemiş olsalar da) kendi kayıtlarına yeniden
    /// yazılır. Bu yalnız tam bir kimliksiz ekran varsa, bu turda bütün bağlı
    /// ekranlar yazılıyorsa ve öbür ekranların hepsinin kaydı bulunduysa
    /// yapılır: yoksa geri yazılamayan bir ekranda başka ekranın görüntüsü
    /// kalırdı. Öyle değilse kimliksiz ekran atlanır.
    /// </summary>
    /// <param name="hedefler">Bu turdaki bütün ekranlar ve yazım yolları.</param>
    /// <param name="degisenler">İçeriği değişen ekranların kimlikleri.</param>
    /// <param name="atlananlar">Yazılamayan değişmiş ekranlar buraya eklenir.</param>
    private static List<(CanvasScreen Screen, DuvarKagidiHedefi Hedef)> YazimSirasi(
        IReadOnlyList<(CanvasScreen Screen, DuvarKagidiHedefi Hedef)> hedefler,
        HashSet<string> degisenler,
        int bagliEkranSayisi,
        List<MonitorTarget> atlananlar)
    {
        var sira = new List<(CanvasScreen Screen, DuvarKagidiHedefi Hedef)>();
        var kimliksizler = hedefler.Where(h => h.Hedef.Tur == HedefTuru.Kimliksiz).ToList();

        if (kimliksizler.Count == 1
            && degisenler.Contains(kimliksizler[0].Screen.Monitor.StableId)
            && hedefler.Count >= bagliEkranSayisi
            && hedefler.All(h => h.Hedef.Tur is HedefTuru.Kimlik or HedefTuru.Kimliksiz))
        {
            sira.Add(kimliksizler[0]);
            sira.AddRange(hedefler.Where(h => h.Hedef.Tur == HedefTuru.Kimlik));
            return sira;
        }

        foreach ((CanvasScreen Screen, DuvarKagidiHedefi Hedef) h in hedefler)
        {
            if (!degisenler.Contains(h.Screen.Monitor.StableId))
            {
                continue;
            }

            if (h.Hedef.Tur is HedefTuru.Kimlik or HedefTuru.Hepsi)
            {
                sira.Add(h);
            }
            else
            {
                atlananlar.Add(h.Screen.Monitor);
            }
        }

        return sira;
    }

    /// <summary>Yazım yolunun günlükteki adı.</summary>
    private static string YolAdi(DuvarKagidiHedefi hedef) => hedef.Tur switch
    {
        HedefTuru.Kimlik => "kendi kaydına",
        HedefTuru.Hepsi => "aygıt yolu verilmeden, bütün ekranlara",
        HedefTuru.Kimliksiz => "aygıt yolu verilmeden, kimliksiz kayda",
        _ => "yazılmadı",
    };

    /// <summary>
    /// Windows'un listesi tutarsızsa bunu günlüğe yazar: bozukluk dönemi başına
    /// bir kez, hata olarak (günlüğe varsayılan olarak yalnız hatalar yazılıyor;
    /// sonradan "takvim neden eskidi?" diye bakınca görünsün). Liste düzelince
    /// de bir kez. Liste tutarlıyken kaydı bulunamayan ekran yalnız uyarıdır
    /// (önceden de böyleydi; ekran yeni takıldıysa Windows'un listesi gecikebiliyor).
    /// </summary>
    /// <param name="okunan">Okunan kayıtlar; okunamadıysa null (bir şey yazılmaz).</param>
    private void ListeDurumunuYaz(
        IReadOnlyList<DuvarKagidiKaydi>? okunan,
        IReadOnlyList<(CanvasScreen Screen, DuvarKagidiHedefi Hedef)> yazilanlar,
        IReadOnlyList<MonitorTarget> atlananlar)
    {
        if (okunan is null || DuvarKagidiKayitlari.Saglikli(okunan))
        {
            if (okunan is not null && _listeBozukYazildi)
            {
                _listeBozukYazildi = false;
                Log.Error("Windows'un duvar kâğıdı ekran listesi düzeldi; takvim yeniden ekranların kendi kayıtlarına yazılıyor. Kayıtlar: "
                    + DuvarKagidiKayitlari.Ozet(okunan));
            }

            foreach (MonitorTarget ekran in atlananlar)
            {
                Log.Warn($"Monitörün duvar kâğıdı kaydı bulunamadı, atlandı: {ekran}");
            }

            return;
        }

        if (_listeBozukYazildi)
        {
            return;
        }

        _listeBozukYazildi = true;
        string yollar = yazilanlar.Count == 0
            ? "yok"
            : string.Join(", ", yazilanlar.Select(y => $"{y.Screen.Monitor.DeviceName} {YolAdi(y.Hedef)}"));
        string atlanan = atlananlar.Count == 0
            ? string.Empty
            : "; yazılamayan ekran: " + string.Join(", ", atlananlar.Select(e => e.DeviceName));
        Log.Error($"Windows'un duvar kâğıdı ekran listesi tutarsız; takvimin yazım yolu: {yollar}{atlanan}. Kayıtlar: "
            + DuvarKagidiKayitlari.Ozet(okunan));
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
    ///
    /// Ekranda ne durduğu yazarken kullanılan kayıttan değil, Windows'un
    /// listesinin şimdiki hâlinden okunur. 1.14 yazdığı kayda bakıyordu:
    /// Explorer'ın listesi bozulup o kayıt hiçbir ekrana karşılık gelmez
    /// olunca görüntü ekrana çıkmıyor, denetim yine "yerinde" diyordu. Listeye
    /// göre yazım yolu değiştiyse (liste bozuldu ya da düzeldi) ancak iki ardışık
    /// denetimde aynı yeni yol görülünce yeniden yazılır: Windows'un listesi
    /// ekran açılıp kapanırken bir anlığına farklı görünebilir. Aygıt yolu
    /// verilmeden yazılan görüntü de denetlenir. Ayrıntı için
    /// <see cref="EkraniDenetle"/>.
    /// </summary>
    public bool EkrandakilerBizimMi() => EkraniDenetle() is EkranDenetimi.Yerinde or EkranDenetimi.Bilinmiyor;

    /// <summary>
    /// <see cref="EkrandakilerBizimMi"/>'nin sonucu, nedeniyle: masaüstü katmanı
    /// geri çekilmede (bkz. <see cref="DuvarKagidiNobeti"/>) yalnız "yol aynı,
    /// görüntü yok"u sayar. Birden çok ekranda en ağır sonuç döner (görüntü yok,
    /// yol değişti, bilinmiyor, yerinde sırasıyla).
    ///
    /// Bir kaydın dosyası okunamazsa o ekran bilinmiyor sayılır: "ekranda
    /// görüntü yok" sayılsaydı Explorer'ın bir anlık okuma hatası gereksiz bir
    /// tam yazıma (masaüstü yenilenmesi, büyük dosya) yol açardı; 1.14'te de
    /// okuma hatası "yerinde" sayılıyordu. Yazım anında kaydı bulunamayan, bu yüzden atlanan ekranın
    /// (dosyası boş) yalnız yazım yolu izlenir: kaydı canlı olunca yeniden yazılır.
    /// </summary>
    internal EkranDenetimi EkraniDenetle()
    {
        if (_sonYazilanlar.Count == 0)
        {
            return EkranDenetimi.Yerinde;
        }

        try
        {
            IReadOnlyList<DuvarKagidiKaydi>? kayitlar = DuvarKagidiKayitlari.Oku(DuvarKagidiKayitlari.Ac());
            if (kayitlar is null)
            {
                return EkranDenetimi.Bilinmiyor;
            }

            SonDenetimKayitlari = DuvarKagidiKayitlari.Ozet(kayitlar);
            EkranDenetimi sonuc = EkranDenetimi.Yerinde;
            foreach (KeyValuePair<string, (MonitorTarget Ekran, DuvarKagidiHedefi Hedef, string Dosya)> kayit in _sonYazilanlar)
            {
                (MonitorTarget ekran, DuvarKagidiHedefi yazilan, string dosya) = kayit.Value;
                DuvarKagidiHedefi simdi = DuvarKagidiKayitlari.Coz(kayitlar, ekran, _yazimdakiEkranSayisi);

                if (simdi != yazilan)
                {
                    if (_bekleyenDegisimler.TryGetValue(kayit.Key, out DuvarKagidiHedefi onceki) && onceki == simdi)
                    {
                        Log.Warn($"Ekranın duvar kâğıdı kaydı değişti ({YolAdi(yazilan)} -> {YolAdi(simdi)}); yeniden yazılacak: {ekran}");
                        sonuc = AgirOlan(sonuc, EkranDenetimi.YolDegisti);
                    }
                    else
                    {
                        // İlk görüş: bir sonraki denetimde de böyleyse yazılır.
                        _bekleyenDegisimler[kayit.Key] = simdi;
                        sonuc = AgirOlan(sonuc, EkranDenetimi.Bilinmiyor);
                    }

                    continue;
                }

                _bekleyenDegisimler.Remove(kayit.Key);

                // Yazım anında atlanan ekran: yalnız yol izlenir (bkz. Apply).
                if (dosya.Length == 0)
                {
                    continue;
                }

                if (DuvarKagidiKayitlari.GorunenBilinmiyor(kayitlar, simdi, ekran))
                {
                    sonuc = AgirOlan(sonuc, EkranDenetimi.Bilinmiyor);
                    continue;
                }

                string gorunen = DuvarKagidiKayitlari.Gorunen(kayitlar, simdi, ekran) ?? string.Empty;
                if (!string.Equals(gorunen, dosya, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warn("Ekranda Kalender'in görüntüsü yok ("
                        + (gorunen.Length == 0 ? "boş" : Path.GetFileName(gorunen))
                        + "); yeniden yazılacak.");
                    sonuc = EkranDenetimi.Gorunmuyor;
                }
            }

            return sonuc;
        }
        catch (Exception ex)
        {
            Log.Debug("Ekrandaki duvar kâğıdı okunamadı: " + ex.Message);
            return EkranDenetimi.Bilinmiyor;
        }
    }

    /// <summary>Son denetimde okunan kayıtların günlük özeti (bkz. <see cref="DuvarKagidiKayitlari.Ozet"/>).</summary>
    public string SonDenetimKayitlari { get; private set; } = string.Empty;

    /// <summary>İki denetim sonucundan ağır olanı (enum sırası ağırlık sırasıdır).</summary>
    private static EkranDenetimi AgirOlan(EkranDenetimi a, EkranDenetimi b) => a > b ? a : b;

    /// <summary>
    /// Bu sürecin en son yazdığı görüntü hâlâ hangi ekranlarda duruyor?
    /// Takvim duvar kâğıdından kaldırılırken (kapanış, geri yükleme) takvimsiz
    /// zemin yalnız bu ekranlara yazılır: önceki duvar kâğıdı geri gelen ya da
    /// kullanıcının başka bir duvar kâğıdı seçtiği ekrana dokunulmaz. Okunamayan
    /// ekran sayılmaz; emin olamadığımız yere yazmıyoruz. Ekranda ne durduğu
    /// Windows'un listesinin şimdiki hâlinden okunur (bkz.
    /// <see cref="EkrandakilerBizimMi"/>); hayalet kaydın dosyası sayılmaz.
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
            IReadOnlyList<DuvarKagidiKaydi>? kayitlar = DuvarKagidiKayitlari.Oku(DuvarKagidiKayitlari.Ac());
            if (kayitlar is null)
            {
                return duranlar;
            }

            // Dosyası boş olan, yazım anında atlanan ekrandır: ona bu süreç yazmadı.
            foreach (KeyValuePair<string, (MonitorTarget Ekran, DuvarKagidiHedefi Hedef, string Dosya)> kayit in
                     _sonYazilanlar.Where(k => k.Value.Dosya.Length > 0))
            {
                MonitorTarget ekran = kayit.Value.Ekran;
                DuvarKagidiHedefi hedef = DuvarKagidiKayitlari.Coz(kayitlar, ekran, _yazimdakiEkranSayisi);
                if (string.Equals(DuvarKagidiKayitlari.Gorunen(kayitlar, hedef, ekran), kayit.Value.Dosya,
                        StringComparison.OrdinalIgnoreCase))
                {
                    duranlar.Add(kayit.Key);
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
    /// <param name="ekranlar">
    /// Bütün bağlı ekranlar: sayıları yazım yolunu belirler (bkz.
    /// <see cref="DuvarKagidiKayitlari.Coz"/>). Yalnız görüntüsü üretilebilen
    /// ekranlar verilirse iki ekranlı düzen tek ekran sanılır, takvimin durduğu
    /// ekran bulunamaz.
    /// </param>
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
            IReadOnlyList<DuvarKagidiKaydi>? kayitlar = DuvarKagidiKayitlari.Oku(wallpaper);
            if (kayitlar is null)
            {
                return gosterenler;
            }

            foreach (MonitorTarget ekran in ekranlar)
            {
                // Ekranın kaydı yazımdaki gibi seçilir; bulunamayan ekran sayılmaz.
                DuvarKagidiHedefi hedef = DuvarKagidiKayitlari.Coz(kayitlar, ekran, ekranlar.Count);
                if (hedef.Tur == HedefTuru.Yok)
                {
                    continue;
                }

                string? gorunen = DuvarKagidiKayitlari.Gorunen(kayitlar, hedef, ekran);
                if (WallpaperService.ZeminDosyasiMi(gorunen) && !TakvimsizZeminMi(gorunen))
                {
                    gosterenler.Add(ekran.StableId);
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
    /// <param name="wallpaper">Ekrandakileri okumak için; null ise burada açılır.</param>
    private void CleanUnusedFiles(IDesktopWallpaper? wallpaper = null)
    {
        bool okundu;
        try
        {
            wallpaper ??= DuvarKagidiKayitlari.Ac();
            okundu = KullanilmayanlariSil(
                Paths.CanvasFolder, _sonYazilanlar.Values.Select(v => v.Dosya), wallpaper, DateTime.UtcNow - FileGrace);
        }
        catch (Exception ex)
        {
            // Ekranda ne durduğu bilinmiyor: bu tur hiçbir dosya silinmez.
            Log.Debug("Eski zemin dosyaları silinmedi, duvar kâğıdı okunamadı: " + ex.Message);
            okundu = false;
        }

        // Ekran okunamadıkça hiçbir dosya silinmiyor; takvim yazıldıkça her
        // yazım büyük bir dosya bırakıyor. Sürerse görünsün diye dönem başına
        // bir kez hata olarak yazılır (günlüğe varsayılan olarak yalnız hatalar
        // yazılıyor).
        if (okundu)
        {
            _temizlikOkunamadiYazildi = false;
        }
        else if (!_temizlikOkunamadiYazildi)
        {
            _temizlikOkunamadiYazildi = true;
            Log.Error("Eski zemin dosyaları silinemiyor: Windows'un duvar kâğıdı listesi okunamıyor; ekranda duran "
                + "bir görüntü silinmesin diye okunabilene dek hiçbir zemin dosyasına dokunulmuyor.");
        }
    }

    /// <summary>
    /// Artık kullanılmayan görüntüleri siler. Windows dosyayı uygulamadan
    /// hemen sonra hâlâ okuyor olabildiği için yeni yazılanlara dokunmuyoruz.
    /// Ekranda duran görüntüler de korunur: Explorer yeniden başladığında ya
    /// da çözünürlük değiştiğinde Windows onları yeniden okuyor.
    /// </summary>
    /// <param name="klasor">Zemin klasörü.</param>
    /// <param name="sonYazilanlar">Bu sürecin ekranlara en son yazdığı dosyalar.</param>
    /// <param name="wallpaper">
    /// Ekranda duran dosyaları okumak için: Windows'un listesindeki bütün
    /// kayıtların (hayalet, bağlı olmayan, kimliği boş olanlar dâhil) ve aygıt
    /// yolu verilmeden okunan dosya korunur. Ekran okunamazsa o turda hiçbir
    /// dosya silinmez. Önceden olağan yazımda ekrana bakılmıyordu: ekranda bu
    /// sürecin yazmadığı bir zemin (açılışta önceki oturumunki, Explorer'ın
    /// listesi bozukken ekranda kalan) silinebiliyordu.
    /// </param>
    /// <param name="esik">Bundan sonra yazılan dosyalara dokunulmaz.</param>
    /// <returns>Ekranda duranlar okunamadığı için hiçbir dosya silinmediyse false.</returns>
    internal static bool KullanilmayanlariSil(
        string klasor, IEnumerable<string> sonYazilanlar, IDesktopWallpaper wallpaper, DateTime esik)
    {
        try
        {
            HashSet<string>? gorunenler = WallpaperService.EkrandakiDosyalar(wallpaper);
            if (gorunenler is null)
            {
                return false;
            }

            var ekrandakiler = new HashSet<string>(sonYazilanlar, StringComparer.OrdinalIgnoreCase);
            ekrandakiler.UnionWith(gorunenler);

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
            // Klasör ya da dosya hatası; ekran okundu, bir sonraki tur yeniden dener.
            Log.Debug("Eski zemin dosyaları silinemedi: " + ex.Message);
        }

        return true;
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
