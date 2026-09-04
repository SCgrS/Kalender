# Kalender

Windows masaüstünde, duvar kağıdının üstüne ama masaüstü simgelerinin altına çizilen aylık
takvim. Takvim duvar kağıdının bir parçasıymış gibi görünür: üzerine klasör koyabilir,
tıklayabilir, sürükleyerek çoklu seçim yapabilirsiniz — takvim hiçbir şeye karışmaz.

Günlerin üzerine gelince, Windows'un kendi ipucu balonları tarzında küçük bir kutu açılır ve
o günün Google Takvim etkinlikleri ile görevleri listelenir.

![Ekran görüntüsü](docs/ekran-goruntusu.png)

## Ne yapar

- Aylık takvimi doğrudan masaüstü katmanına (WorkerW) çizer; ayrı bir pencere açmaz.
- Pazar günleri, isteğe bağlı olarak cumartesiler ve resmi tatiller kırmızı gösterilir.
- Gün rakamının altında, o güne düşen etkinlikler için küçük renkli noktacıklar çıkar.
- Fareyle bir günün üzerinde kısa süre beklediğinizde o günün etkinlikleri ve görevleri listelenir.
- Zemin düz renk ya da kendi fotoğrafınız olabilir; her monitör için ayrı ayarlanabilir.
- Seçilen zemin aynı zamanda Windows duvar kağıdına da yazılır; böylece açılışta Kalender
  devreye girene kadar ekran farklı görünmez.

## Gereksinimler

- Windows 10 (20H2 ve üzeri) veya Windows 11
- 64 bit (x64) işlemci
- Kurulum için yönetici hakkı **gerekmez**

## Kurulum

> Bu bölüm ilk sürüm yayımlandığında doğrudan indirme bağlantısıyla güncellenecektir.

1. `Kalender-Kurulum.exe` dosyasını indirin.
2. Çift tıklayın.
3. Dosya imzalı olmadığı için Windows SmartScreen bir uyarı gösterebilir:
   **Daha fazla bilgi** yazısına tıklayın, sonra çıkan **Yine de çalıştır** düğmesine basın.
4. Kurulum `%LOCALAPPDATA%\Kalender` klasörüne yapılır, Başlat menüsüne kısayol eklenir ve
   Windows açılışında kendiliğinden başlaması ayarlanır.

## Google Takvim ve Görevler bağlantısı

Kalender, Google hesabınıza **yalnızca okuma** izniyle bağlanır; etkinlik veya görev eklemez,
silmez. Bağlantı için kendi Google Cloud istemci kimliğinizi oluşturmanız gerekir. Böylece
verileriniz doğrudan sizin Google hesabınızla Kalender arasında kalır, arada başka bir
sunucu olmaz.

> Bu rehber, Google bağlantısı eklendiğinde (4. aşama) adım adım doldurulacaktır.

## Ayarlar

> Ayarlar penceresi 6. aşamada eklenecek; bu bölüm o zaman doldurulacaktır.

## Resmi tatiller

Tatil listesi Google'ın Türkiye resmi tatil takviminden alınır ve yerel olarak saklanır.
İnternet olmadığında en son indirilen liste kullanılır.

> **Uyarı:** Dini bayram tarihleri idari kararla değişebilir; lütfen resmi kaynaklardan
> teyit ediniz.

## Kaldırma

`Kalender-Kaldir.exe` dosyasını çalıştırın (ya da Ayarlar penceresindeki **Kaldır**
düğmesini kullanın). Kaldırma sırasında:

- Kalender kurulmadan önceki duvar kağıdınız geri yüklenir,
- Ayar ve önbellek klasörünün silinip silinmeyeceği sorulur.

## Şu anki durum

Proje aşama aşama geliştiriliyor.

| Aşama | İçerik | Durum |
| --- | --- | --- |
| 1 | İskelet, WorkerW katmanı, düz renk zemin, statik takvim çizimi | Tamam |
| 2 | Çoklu monitör, DPI, konum/boyut, temalar, renk seçici, fotoğraf | Sırada |
| 3 | Resmi tatil listesi (ICS), önbellek, kırmızı günler | Bekliyor |
| 4 | Google OAuth, takvim ve görev çekme, artımlı yoklama | Bekliyor |
| 5 | Noktacıklar, fare üstünde açılan gün kutusu | Bekliyor |
| 6 | Ayarlar penceresi, tepsi menüsü, açılışta başlatma | Bekliyor |
| 7 | Kurulum dosyası, README, sürüm etiketi | Bekliyor |

## Sürüm geçmişi

### 0.1.0 (geliştirme)

- İlk iskelet: masaüstü katmanı, düz renk zemin, aylık takvim çizimi, tepsi simgesi.

## Geliştirme

```
dotnet build Kalender.sln -c Debug
```

Tek dosya, kendi kendine yeten yayın için:

```
powershell -ExecutionPolicy Bypass -File yayin.ps1
```

Kullanılan üçüncü taraf bileşenler için [UCUNCU-TARAF.md](UCUNCU-TARAF.md) dosyasına bakın.
