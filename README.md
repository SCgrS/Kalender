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
silmez. Bağlantı doğrudan sizinle Google arasındadır, arada başka bir sunucu yoktur.

Bunun için kendi Google Cloud istemci kimliğinizi oluşturmanız gerekiyor. Bir kerelik bir
işlem, yaklaşık beş dakika sürüyor. Aynı adımlar Kalender'in Ayarlar penceresinde de yazılı.

### 1. Google Cloud projesi oluşturun

1. [console.cloud.google.com](https://console.cloud.google.com) adresine kendi Google
   hesabınızla girin.
2. Sayfanın üstündeki proje seçicisine (Google Cloud yazısının sağındaki açılır kutu)
   tıklayın, açılan pencerede **Yeni proje**'ye basın.
3. Proje adına `Kalender` yazıp **Oluştur**'a basın. Proje oluşunca yine üstteki seçiciden
   bu projeyi seçin.

### 2. Gerekli iki API'yi açın

1. Sol üstteki menüden **API'ler ve Hizmetler > Kitaplık**'a girin.
2. Arama kutusuna `Google Calendar API` yazın, çıkan sonuca tıklayın, **Etkinleştir**'e basın.
3. Kitaplığa geri dönüp bu kez `Google Tasks API` arayın ve onu da **Etkinleştir**'e basın.

### 3. İzin ekranını hazırlayın

Google bu bölümü yeniledi; artık adı **Google Auth Platform**. Sol menüden
**API'ler ve Hizmetler > OAuth izin ekranı**'na girdiğinizde bu sayfaya yönlendirilirsiniz.

1. Sayfanın ortasındaki **Get started** (Başlayın) düğmesine basın.
2. **App Information**: uygulama adına `Kalender`, destek e-postası olarak kendi adresinizi
   seçin → **Next**.
3. **Audience**: **External** (Harici) → **Next**.
4. **Contact Information**: kendi e-posta adresinizi yazın → **Next**.
5. **Finish**: kullanıcı verisi ilkesini kabul eden kutuyu işaretleyin → **Continue** →
   **Create**.
6. Sol menüden **Audience**'a girip aşağıdaki **Test users** bölümünde **+ Add users** ile
   kendi e-posta adresinizi ekleyin ve kaydedin. Bu önemli: eklemezseniz Google onay
   ekranında sizi reddeder.

### 4. İstemci kimliğini oluşturun

1. Sol menüden **Clients** (İstemciler) → **+ Create client**.
2. Application type (Uygulama türü): **Desktop app** (Masaüstü uygulaması).
3. Ada `Kalender` yazıp **Create**'e basın.
4. Çıkan pencerede **Client ID** ve **Client secret** yazar. İkisini de kopyalayın
   (pencereyi kapatsanız bile Clients listesinden yeniden açabilirsiniz).

> Daha eski bir Google Cloud arayüzü görüyorsanız aynı işler **API'ler ve Hizmetler >
> OAuth izin ekranı** ve **API'ler ve Hizmetler > Kimlik bilgileri > OAuth istemci kimliği**
> altındadır.

### 5. Kalender'e yapıştırın

1. Tepsi simgesine tıklayıp **Ayarlar**'ı açın, **Google Takvim ve Görevler** bölümüne inin.
2. İki değeri ilgili kutulara yapıştırın.
3. **Google'a bağlan**'a basın. Tarayıcıda Google onay ekranı açılır.
4. "Google bu uygulamayı doğrulamadı" uyarısı çıkarsa **Gelişmiş > Kalender'e git (güvenli
   değil)** yolunu izleyin. Bu uyarı, uygulamanın Google tarafından incelenmemiş olmasından
   kaynaklanır; kendi projenizde kendi hesabınızla çalıştığınız için beklenen bir durumdur.
5. İzin verdikten sonra tarayıcıda "Kalender bağlandı" yazısını görünce sekmeyi
   kapatabilirsiniz.

Bağlandıktan sonra Ayarlar penceresinde hangi takvimlerin ve hangi görev listelerinin
gösterileceğini, verinin ne sıklıkla yenileneceğini (30 saniye ile 24 saat arası) ve görev
noktacıklarının rengini seçebilirsiniz.

**Bağlantıyı kesmek için:** Ayarlar > Google bölümü > **Bağlantıyı kes**. Bu, bilgisayarınızdaki
yetki dosyasını ve yerel veri kopyasını siler.

### Verileriniz nerede duruyor?

- Google yetki anahtarı `%LOCALAPPDATA%\Kalender\google-token.dat` dosyasında, Windows'un
  DPAPI'siyle **yalnızca sizin kullanıcı hesabınızın çözebileceği** şekilde şifrelenmiş durur.
- Etkinlik ve görev kopyası `%LOCALAPPDATA%\Kalender\cache\google.json` dosyasındadır ve
  yalnızca görünen ayı kapsar.
- İstemci kimliği ve gizli anahtar uygulamanın içine gömülü **değildir**; sizin projenize aittir.

## Ayarlar

Tepsi (saatin yanındaki bildirim alanı) simgesine **tıklayın** ya da sağ tıklayıp
**Ayarlar**'ı seçin. Kalender zaten açıkken `Kalender.exe`'yi tekrar çalıştırmak da
ayarları açar.

Yaptığınız her değişiklik anında masaüstüne yansır; "Tamam"a basmanız gerekmez.

### Zemin

- **Hazır renkler** — koyudan açığa sekiz renk. Birine tıklayın, hemen uygulanır.
- **Kendi rengim** — renk çarkından ton ve doygunluk, yandaki şeritten parlaklık
  seçilir. Renk kodunu (`#2E3238` gibi) elle de yazabilirsiniz.
- **Fotoğraf** — kendi resminizi zemin yapar. Yerleşim seçenekleri: Kapla, Sığdır,
  Ortala, Döşe.

Seçtiğiniz zemin aynı zamanda Windows duvar kağıdına da yazılır; böylece açılışta
Kalender devreye girene kadar ekran farklı görünmez. Bunu geri almak için tepsi
menüsündeki **Eski duvar kağıdıma dön**'ü kullanın.

### Yazı

- **Yazı rengini zemine göre otomatik seç** — düz renk zeminde açık/koyu yazı
  kendiliğinden seçilir. Fotoğraf zeminde otomatik seçim yapılamaz, rengi siz
  belirlersiniz.
- **Cumartesi de pazar gibi kırmızı olsun** — varsayılan olarak açık.
- **Takvimin arkasını karart** — yalnızca fotoğraf zeminde. Yazılar okunmuyorsa
  takvimin arkasına yumuşak bir gölge koyar.

### Ekranlar

- Takvimin **tüm ekranlarda**, **yalnız ana ekranda** ya da **seçtiğiniz
  ekranlarda** görünmesini seçebilirsiniz.
- Her ekran için ayrı ayrı:
  - **Takvimi taşı** — o ekranı kaplayan bir yerleştirme ekranı açar. Takvimi
    sürükleyip bırakın; kenarlara ve ortaya yaklaşınca kendiliğinden hizalanır.
    Aynı ekranda boyutu da (%80-150) ayarlayabilirsiniz. Esc: vazgeç.
  - **Boyut** — takvimin büyüklüğü.
  - **Bu ekrana fotoğraf…** — o ekrana özel zemin fotoğrafı.

Konum, boyut ve fotoğraf her monitör için ayrı hatırlanır. Ekranlarınızın
çözünürlüğü ya da ölçeği değişirse Kalender kendini yeniden kurar.

### Genel

- **Windows açılışında Kalender'i başlat** — varsayılan olarak açık. Kayıt yalnızca kendi
  kullanıcı hesabınıza yazılır (`HKCU\...\Run`), yönetici hakkı istenmez. Kutunun altında
  hangi dosyanın başlatılacağı yazar.
- **Seçilen zemini Windows duvar kağıdı olarak da ayarla** — kapatırsanız Windows duvar
  kağıdınıza hiç dokunulmaz.
- **Günlük ayrıntısı** — normalde yalnızca hatalar yazılır. Bir sorun ararken *Bilgi* ya da
  *Ayrıntılı* seçebilirsiniz. **Günlük klasörünü aç** düğmesi dosyaları gösterir.
- **Sürüm** ve **Sürümler sayfası** — Kalender kendini otomatik güncellemez.

## Tepsi menüsü

Saatin yanındaki Kalender simgesine sağ tıklayın:

| Satır | Ne yapar |
| --- | --- |
| Ayarlar | Ayarlar penceresini açar (simgeye sol tıklamak da açar) |
| Şimdi yenile | Tatil listesini ve Google verisini hemen tazeler |
| Takvimi gizle / göster | Takvimi çizmeyi durdurur; zemin kalır |
| Takvimi yeniden çiz | Masaüstü katmanını sıfırdan kurar (kabuk takılırsa işe yarar) |
| Eski duvar kağıdıma dön | Kalender'den önceki duvar kağıdını geri yükler ve eşitlemeyi kapatır |
| Çıkış | Kalender'i kapatır |

Simgenin üzerine gelince ipucunda Google bağlantı durumu yazar (bağlı / bağlı, çevrimdışı /
bağlı değil).

## Gün kutusu

Masaüstündeyken farenizi bir günün üzerine getirip kısa süre beklerseniz, Windows'un kendi
ipucu balonlarına benzeyen küçük bir kutu açılır ve o günün içeriğini listeler:

- Önce varsa resmi tatil,
- sonra tüm gün süren etkinlikler,
- sonra saatli etkinlikler (saate göre sıralı, `09:30 · Başlık`),
- en altta görevler (`Görev · Başlık`).

Kutu şu koşulların **hepsi** sağlanmadan açılmaz:

1. İmlecin altındaki pencere masaüstünün kendisi olmalı (üstte Word, Chrome gibi bir pencere
   varsa açılmaz).
2. Fare tuşlarından hiçbiri basılı olmamalı (seçim yaparken ya da simge sürüklerken açılmaz).
3. Tam ekran, sunum ya da oyun kipi olmamalı.
4. İmleç aynı günün üzerinde en az 0,4 saniye durmuş olmalı.

Hücreden çıkınca kutu yaklaşık 0,15 saniye içinde kapanır. O günde gösterilecek hiçbir şey
yoksa kutu hiç açılmaz. Kutu tıklanmaz, sürüklenmez; ekran kenarına taşacaksa konumunu
kendisi düzeltir.

## Resmi tatiller

Tatil listesi Google'ın herkese açık Türkiye resmi tatil takviminden (ICS) alınır; hesap
ya da anahtar gerekmez. Liste bilgisayarınızda `%LOCALAPPDATA%\Kalender\cache` altında
saklanır ve ayda bir yenilenir. İnternet olmadığında en son indirilen liste sessizce
kullanılmaya devam eder, hata penceresi çıkmaz.

- **Tam gün resmi tatiller** kırmızı gösterilir.
- **Arifeler (yarım günler)** kırmızı gösterilmez, normal gün gibi durur. Adları gün
  kutusunda görünür (5. aşamada).
- **Anneler Günü gibi kutlama/anma günleri** tatil sayılmaz, kırmızıya boyanmaz.

Listeyi elle yenilemek için tepsi menüsünde **Şimdi yenile** ya da Ayarlar penceresindeki
**Listeyi şimdi güncelle** düğmesi.

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
| 2 | Çoklu monitör, DPI, konum/boyut, temalar, renk seçici, fotoğraf | Tamam |
| 3 | Resmi tatil listesi (ICS), önbellek, kırmızı günler | Tamam |
| 4 | Google OAuth, takvim ve görev çekme, artımlı yoklama | Tamam |
| 5 | Noktacıklar, fare üstünde açılan gün kutusu | Tamam |
| 6 | Ayarlar penceresi, tepsi menüsü, açılışta başlatma | Tamam |
| 7 | Kurulum dosyası, README, sürüm etiketi | Sırada |

## Sürüm geçmişi

### 0.6.0 (geliştirme)

- Ayarlara "Genel" bölümü eklendi: Windows açılışında başlatma, duvar kağıdı eşitlemesini
  kapatma, günlük ayrıntısı, günlük klasörünü açma, sürüm numarası ve sürümler sayfası
  bağlantısı.
- Açılışta başlatma yalnızca kendi kullanıcı hesabınızın kaydına yazılır; yönetici hakkı
  istenmez.
- Ayarlar penceresinin başlığı "Kalender - Ayarlar" oldu.

### 0.5.1 (geliştirme)

- Gün kutusunda başlıklar artık kısaltılmıyor; sığmazsa alt satıra sarkıyor ve alt satır
  saatin altına değil başlığın hizasına geliyor.
- Aynı kayıt iki kez görünmüyordu düzeltildi: Google, süresi saatli olan Görevler'i Takvim
  API'sinde de sıradan bir etkinlik olarak döndürebiliyor. Aynı gün ve aynı başlıktaki
  etkinlik, görev kaydı varsa gösterilmiyor.
- Çalışma konumu (workingLocation) kayıtları takvimde gösterilmiyor.

### 0.5.0 (geliştirme)

- Gün kutusu: fare bir günün üzerinde 0,4 saniye beklediğinde o günün tatili, etkinlikleri ve
  görevleri listeleniyor. Kutu odak almıyor, tıklanmıyor, ekran kenarında konumunu düzeltiyor.
- Kutu yalnızca masaüstü görünürken, fare tuşu basılı değilken ve tam ekran/sunum kipi
  yokken açılıyor. O günde gösterilecek bir şey yoksa hiç açılmıyor.
- Başlıklardaki sekme ve satır sonları tekleştirilip 40 karakterde kırpılıyor.

### 0.4.0 (geliştirme)

- Google Takvim ve Görevler bağlantısı: OAuth 2.0 + PKCE, loopback yönlendirme, yalnız okuma
  izinleri. Uygulamaya hiçbir gizli anahtar gömülmedi; kullanıcı kendi istemci kimliğini verir.
- Yetki anahtarı DPAPI ile şifreli saklanıyor; ayarlardan bağlantı kesilebiliyor.
- Yalnızca görünen ay çekiliyor; yoklamalar artımlı (updatedMin) ve yanıtlar "fields" ile
  kırpılıyor. Değişiklik yoksa yanıt birkaç yüz bayt.
- Hangi takvimlerin ve görev listelerinin gösterileceği, yenileme aralığı (30 sn - 24 saat) ve
  görev noktacığı rengi ayarlanabiliyor.
- Gün rakamının altında noktacıklar: renk etkinliğin ya da takvimin kendi renginden geliyor,
  dörtten fazlaysa 3 nokta + "+N".
- Çevrimdışıyken hata gösterilmiyor; yerel kopya kullanılıyor ve durum tepsi ipucunda yazıyor.

### 0.3.0 (geliştirme)

- Resmi tatiller Google'ın açık ICS akışından indiriliyor, yerel olarak saklanıyor ve
  ayda bir yenileniyor. İnternet yoksa önbellek sessizce kullanılıyor.
- Tam gün tatiller kırmızı; arifeler normal, kutlama/anma günleri hariç.
- Ayarlara "Resmi tatiller" bölümü ve dini bayram teyit uyarısı eklendi.
- Tepsi menüsündeki "Şimdi yenile" artık çalışıyor.

### 0.2.0 (geliştirme)

- Görünüm ayarları penceresi: hazır renkler, renk çarkı, fotoğraf zemin, yazı rengi,
  cumartesi rengi, takvim arkası karartma.
- Takvim sürükleyerek yerleştirilebiliyor; kenarlara ve ortaya yapışıyor.
- Konum, boyut ve fotoğraf her monitör için ayrı hatırlanıyor.
- Ekran ölçeği (DPI), çözünürlük, monitör tak/çıkar, uzak masaüstü ve kilit açma
  durumlarında katman kendini yeniden kuruyor.
- Takvimin boyutu ekran ölçüsüyle orantılı: çözünürlük değişince takvim de
  birlikte küçülüp büyüyor, ekrandaki oranı sabit kalıyor.

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

Belirli bir ayın takvimini arayüz açmadan bir PNG'ye çizdirmek için (görünüm ve tatil
denetimlerinde işe yarıyor):

```
Kalender.exe --onizleme 2026-10 C:\gecici\ekim.png
```

Kullanılan üçüncü taraf bileşenler için [UCUNCU-TARAF.md](UCUNCU-TARAF.md) dosyasına bakın.
