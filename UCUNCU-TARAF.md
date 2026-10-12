# Üçüncü taraf bileşenler

Bu dosyada, Kalender'in kullandığı üçüncü taraf ve bağımsız geliştirici paketleri listelenir.
Microsoft'un ve Google'ın kendi resmî kütüphaneleri bu listeye girmez.

README'deki teşekkür/atıf bölümü bu listeden hazırlanacaktır.

## Şu an kullanılanlar

| Paket | Ne işe yarıyor | Lisans | Proje |
| --- | --- | --- | --- |
| _(henüz yok)_ | | | |

7. aşama sonunda proje hâlâ hiçbir üçüncü taraf pakete bağlı değil. Bilerek böyle yapıldı:

- **Tepsi simgesi** için hazır bir kütüphane yerine doğrudan `Shell_NotifyIcon` çağrıldı.
  Böylece Explorer yeniden başladığında simgeyi geri koymak gibi ayrıntılar elimizde kaldı
  ve uygulamaya fazladan bağımlılık girmedi.
- **Renk çarkı** (`Ui/ColorWheel`) elle yazıldı. Ton/doygunluk çarkı bir kez üretilip
  önbelleğe alınıyor, parlaklık üstteki siyah örtünün saydamlığıyla gösteriliyor.
  Hazır bir denetim eklemeye değecek kadar karmaşık değildi.
- **ICS okuyucu** (`Calendar/Holidays/IcsParser`) elle yazıldı. Tam bir takvim kütüphanesi
  değil; yalnızca resmî tatil akışındaki tüm gün süren kayıtları okuyacak kadarını yapıyor
  (satır katlaması, kaçış dizileri, DTSTART/DTEND/SUMMARY/DESCRIPTION). İhtiyaç bu kadar dar
  olduğu için paket eklenmedi.
- **Google bağlantısı** için Google'ın .NET kütüphaneleri yerine doğrudan REST çağrıları ve
  kendi yazdığımız OAuth 2.0 + PKCE akışı kullanıldı. Üç nedenle: tek dosya yayın küçük kalıyor,
  isteklerde `fields` parametresiyle veriyi kırpabiliyoruz (şartnamedeki az veri kuralı) ve
  uygulamaya hiçbir gizli anahtar gömmek zorunda kalmıyoruz. Google'ın kendi kütüphaneleri
  zaten bu listeye girmezdi; mesele boyut ve denetim.
- **Çizim** için `System.Drawing.Common` kullanılıyor; Microsoft'un kendisi, .NET 10'da çerçevenin
  parçası olarak geliyor (ayrı paket değil). Uygulama .NET 10 ve WPF üzerinde çalışıyor; çalışma
  ortamı tek dosya yayının içine gömülü (.NET 8'in desteği 10 Kasım 2026'da bittiği için .NET 10'a
  geçildi).
- **Takvimin üstündeki masaüstü simgesini tanıma** (`Desktop/MasaustuSimgeleri`) Windows'un
  erişilebilirlik arayüzüyle (oleacc, MSAA) yapılıyor; ekran okuyucuların da kullandığı yol,
  paket gerekmiyor. Varsayılan yerdeki simge sütunu hesabı (`Desktop/MasaustuSimgeIzgarasi`)
  masaüstü listesinin kendi simge aralığını okuyor.

## Hazır zemin fotoğrafları

Paket değil, içerik: `src/Kalender.App/Assets/DuvarKagitlari` altındaki dört fotoğraf exe'nin
içine gömülür; seçilince `%LOCALAPPDATA%\Kalender\duvar-kagitlari` klasörüne çıkarılır.

| Dosya | Ayarlardaki adı | Fotoğrafçı | Kaynak |
| --- | --- | --- | --- |
| `sisli-cam-ormani.jpg` | Sisli çam ormanı | Dan Otis | https://unsplash.com/photos/OYFHT4X5isg |
| `sisli-dag-sirtlari.jpg` | Sisli dağ sırtları | Chris wu | https://unsplash.com/photos/xyCpmrP_0Qo |
| `yildizli-karli-daglar.jpg` | Yıldızlı gökte karlı dağlar | Patrick Carr | https://unsplash.com/photos/pAoo1Rs1Yy8 |
| `gece-ovasi.jpg` | Gece ovası | Artur Adilkhanian | https://unsplash.com/photos/P-kcxScUBhU |

Lisans: [Unsplash Lisansı](https://unsplash.com/license). Ücretsizdir, ticari kullanım dâhil;
fotoğrafçıdan izin almak ya da ad vermek gerekmez (yine de burada anılıyor). Fotoğraflar
değiştirilmeden satılamaz ve Unsplash'e benzer ya da rakip bir hizmet kurmak için derlenemez.

Fotoğraflar 3840 × 2160'a kırpılıp küçültüldü: sisli çam ormanı ve yıldızlı dağlar, takvimin
durduğu sağ alt köşe sakin kalsın diye üst kısımdan kırpıldı. `-kucuk.jpg` dosyaları ayarlar
penceresindeki 320 × 180 küçük resimlerdir. Dosyalarda kamera, konum ya da kişi bilgisi (EXIF,
XMP) yoktur; yalnız renk profili vardır.

## İlerideki aşamalarda beklenenler

Aşağıdakiler henüz eklenmedi; eklendiklerinde bu dosyada gerçek satırlar hâline gelecek.

- Şu an bir ihtiyaç görünmüyor. Önbellekler küçük JSON dosyaları olarak yeterli oldu
  (tatiller ~2 KB, Google verisi görünen ay kadar), bu yüzden SQLite'a gerek kalmadı.
