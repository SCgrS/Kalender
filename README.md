# Kalender

Kalender, Windows masaüstünde doğrudan duvar kağıdına işlenen aylık bir takvimdir. Zemin ve
takvim tek bir görüntüde birleştirilip Windows'un kendi duvar kağıdı olarak uygulanır; ortada
bir pencere olmadığı için masaüstünde klasör açmak, simge sürüklemek, çoklu seçim yapmak
hiçbir şekilde etkilenmez.

Fareyle bir günün üzerinde kısa süre beklediğinizde o günün Google Takvim etkinlikleri ile
görevleri küçük bir kutuda listelenir. Google hesabına yalnızca okuma izniyle bağlanılır.

![Ekran görüntüsü](docs/ekran-goruntusu.png)

## Ne yapar

- Zemini ve aylık takvimi tek görüntüde birleştirip Windows duvar kağıdı olarak uygular.
- Pazar günleri, isteğe bağlı olarak cumartesiler ve Türkiye resmi tatilleri kırmızı gösterilir.
- Gün rakamının altında, o güne düşen etkinlik ve görevler için renkli noktacıklar çıkar.
- Bir günün üzerinde beklediğinizde o günün tatili, etkinlikleri ve görevleri listelenir.
- Takvimin üzerine sağ tıklayınca güne gitme, ay değiştirme ve yeni kayıt açma menüsü çıkar.
- Zemin düz renk ya da kendi fotoğrafınız olabilir; konum, boyut ve fotoğraf her monitör için
  ayrı ayarlanır.
- Kalender kapalıyken duvar kağıdında takvimsiz zemin kalır; ekran farklı görünmez.

## Kurulum

### [⬇ Kalender-Kurulum.exe indir](https://github.com/SCgrS/Kalender/releases/latest/download/Kalender-Kurulum.exe)

Tek dosya, 63 MB. Çalışması için gereken her şey içinde; ayrıca .NET kurmanız gerekmez.
Windows 10 (20H2 ve üzeri) ya da Windows 11, 64 bit; yönetici hakkı gerekmez.

> Depo özel (private) olduğu için indirme bağlantısı yalnızca GitHub'da oturumu açık olan
> yetkili hesaplarda çalışır. Tüm sürümler: [Sürümler sayfası](https://github.com/SCgrS/Kalender/releases)

1. Yukarıdaki bağlantıya tıklayıp dosyayı indirin.
2. İndirilen `Kalender-Kurulum.exe` dosyasına **çift tıklayın**.
3. Dosya imzalı olmadığı için Windows SmartScreen uyarı gösterebilir: **Daha fazla bilgi**
   yazısına tıklayın, sonra **Yine de çalıştır** düğmesine basın.
4. Açılan pencerede **Kur**'a basın. Kurulum birkaç saniye sürer, ardından Kalender başlar.

Kurulum sistem klasörlerine hiçbir şey yazmaz:

- Uygulama `%LOCALAPPDATA%\Kalender\app` klasörüne kopyalanır.
- Başlat menüsüne **Kalender** kısayolu eklenir.
- Windows açılışında başlaması için yalnızca kendi kullanıcı kaydınıza (`HKCU\...\Run`) yazılır.
- Windows'un **Uygulamalar** listesine kaydedilir; oradan da kaldırabilirsiniz.

### Güncelleme

Kalender kendini otomatik güncellemez. Yeni sürümün `Kalender-Kurulum.exe` dosyasını indirip
çalıştırın, açılan pencerede **Güncelle**'ye basın. Çalışan Kalender kendiliğinden kapatılır,
dosya değiştirilir; ayarlarınız uygulama klasörünün dışında durduğu için korunur.

## Google Takvim ve Görevler bağlantısı

Kalender Google hesabınıza **yalnızca okuma** izniyle bağlanır; etkinlik veya görev eklemez,
silmez. Bağlantı doğrudan sizinle Google arasındadır, arada başka bir sunucu yoktur. Bunun için
kendi Google Cloud istemci kimliğinizi oluşturmanız gerekir (bir kerelik, yaklaşık beş dakika).
Aynı adımlar Ayarlar penceresinde **Client ID nasıl alınır?** başlığı altında da yazılıdır.

### 1. Google Cloud projesi oluşturun

1. [console.cloud.google.com](https://console.cloud.google.com) adresine Google hesabınızla girin.
2. Üstteki proje seçicisine (Google Cloud yazısının sağındaki açılır kutu) tıklayın, **Yeni
   proje**'ye basın, ada `Kalender` yazıp **Oluştur**'a basın. Sonra seçiciden bu projeyi seçin.

### 2. İki API'yi açın

1. Sol menüden **API'ler ve Hizmetler > Kitaplık**'a girin.
2. Arama kutusuna `Google Calendar API` yazın, sonuca tıklayın, **Etkinleştir**'e basın.
3. Kitaplığa dönüp `Google Tasks API` arayın, onu da **Etkinleştir**'e basın.

### 3. İzin ekranını hazırlayın

Sol menüden **API'ler ve Hizmetler > OAuth izin ekranı**'na girin; Google sizi **Google Auth
Platform** sayfasına yönlendirir.

1. **Get started**'a basın.
2. **App Information**: uygulama adı `Kalender`, destek e-postası kendi adresiniz → **Next**.
3. **Audience**: **External** → **Next**. **Contact Information**: e-posta adresiniz → **Next**.
4. **Finish**: ilkeyi kabul eden kutuyu işaretleyin → **Continue** → **Create**.
5. Sol menüden **Audience**'a girin, **Test users** bölümünde **+ Add users** ile kendi
   e-posta adresinizi ekleyip kaydedin. Eklemezseniz Google onay ekranında sizi reddeder.

### 4. İstemci kimliğini oluşturun

1. Sol menüden **Clients** → **+ Create client**.
2. Application type: **Desktop app**. Ad: `Kalender` → **Create**.
3. Çıkan pencerede **Client ID** ve **Client secret** yazar; ikisini de kopyalayın. Pencereyi
   kapatsanız bile Clients listesinden yeniden açabilirsiniz.

### 5. Kalender'e yapıştırın

1. Tepsi simgesine tıklayıp **Ayarlar**'ı açın, **Google Takvim ve Görevler** bölümüne inin.
2. İki değeri **İstemci kimliği (Client ID)** ve **İstemci gizli anahtarı (Client secret)**
   kutularına yapıştırın.
3. **Google'a bağlan**'a basın; tarayıcıda Google onay ekranı açılır.
4. "Google bu uygulamayı doğrulamadı" uyarısı çıkarsa **Gelişmiş > Kalender'e git (güvenli
   değil)** yolunu izleyin; uygulama Google'ca incelenmediği için bu uyarı beklenen bir durum.
5. İzin verince tarayıcıda "Kalender bağlandı" yazısını görürsünüz; sekmeyi kapatabilirsiniz.

Bağlandıktan sonra aynı bölümde **Ne sıklıkla yenilensin?** (30 saniye – 24 saat),
**Gösterilecek takvimler**, **Gösterilecek görev listeleri** ve **Görev noktacığı rengi**
seçenekleri açılır. Bağlantıyı koparmak için **Bağlantıyı kes**: yetki dosyası ve yerel veri
kopyası silinir.

## Ayarlar

Tepsi (saatin yanındaki bildirim alanı) simgesine **tıklayın** ya da sağ tıklayıp **Ayarlar**'ı
seçin. Kalender açıkken `Kalender.exe`'yi yeniden çalıştırmak da ayarları açar. Her değişiklik
anında masaüstüne uygulanır; kaydetmeniz gerekmez.

- **Zemin** — **Hazır renkler** (koyudan açığa sekiz renk), **Kendi rengim** (renk çarkı;
  `#2E3238` gibi kodu elle de yazabilirsiniz) ya da **Fotoğraf seç…**. Fotoğrafta **Yerleşim**:
  Kapla, Sığdır, Ortala, Döşe.
- **Yazı** — **Yazı rengini zemine göre otomatik seç** (fotoğraf zeminde rengi siz seçersiniz),
  **Cumartesi de pazar gibi kırmızı olsun**, **Takvimin arkasını karart (fotoğraf zeminde)**
  kaydırıcısı.
- **Takvim renkleri** — **Hafta günleri ve ay adı**, **Tatil ve hafta sonu günleri**, **Pasif
  günler**, **Bugünün arkasındaki daire**. Hepsi **Otomatik** başlar; işareti kaldırınca renk
  çarkı açılır. Gün altındaki noktacıklar buraya dahil değildir; renkleri Google'dan gelir.
- **Resmi tatiller** — **Resmi tatilleri kırmızı göster**.
- **Google Takvim ve Görevler** — yukarıdaki bölümde anlatıldı.
- **Ekranlar** — **Tüm ekranlarda**, **Yalnız ana ekranda** ya da **Seçtiğim ekranlarda**
  (**Bu ekranda göster** kutusu). Her ekran için **Takvimi taşı** (sürükleyip bırakın;
  kenarlara ve ortaya yaklaşınca hizalanır, %80–150 boyut, Esc vazgeçer), **Boyut** ve
  **Bu ekrana fotoğraf…**.
- **Genel** — **Windows açılışında Kalender'i başlat**, **Takvimi Windows duvar kağıdına
  işle** (kapatırsanız duvar kağıdınıza dokunulmaz ama takvim de görünmez), **Günlük
  ayrıntısı** (Yalnız hatalar / Bilgi / Ayrıntılı) ve **Günlük klasörünü aç**, **Sürümler
  sayfası**, **Kalender'i kaldır**.

Pencerenin altındaki **Karanlık kip / Aydınlık kip** düğmesi yalnızca ayarlar penceresinin
görünümünü değiştirir; ilk açılışta Windows'un uygulama teması izlenir.

## Tepsi menüsü

Saatin yanındaki Kalender simgesine sağ tıklayın:

| Satır | Ne yapar |
| --- | --- |
| Ayarlar | Ayarlar penceresini açar (simgeye sol tıklamak da açar) |
| Şimdi yenile | Tatil listesini ve Google verisini hemen tazeler |
| Takvimi gizle / Takvimi göster | Takvimi çizmeyi durdurur; zemin kalır |
| Takvimi yeniden çiz | Görüntüyü sıfırdan üretip duvar kağıdına yeniden yazar |
| Eski duvar kağıdıma dön | Kalender'den önceki duvar kağıdını geri yükler; takvim artık çizilmez |
| Çıkış | Kalender'i kapatır |

Simgenin ipucunda durum yazar: *Google bağlı*, *Google bağlı (çevrimdışı)*, *Google bağlı değil*.

## Gün kutusu

Farenizi bir günün üzerine getirip 0,4 saniye beklerseniz küçük bir kutu açılır: önce varsa
resmi tatil, sonra tüm gün süren etkinlikler, sonra saatli etkinlikler (`09:30`), en altta
saati bilinmeyen görevler (`Tüm gün`); saati bilinen görevler saat sırasında yerini alır.
Ön ek ve yanındaki yuvarlak kaydın Google'daki rengini taşır; tamamlanmış görevler gri yazılır.

Kutu yalnızca imlecin altında masaüstünün kendisi varken, fare tuşu basılı değilken ve tam
ekran/sunum kipi yokken açılır. Gösterilecek bir şey yoksa hiç açılmaz; hücreden çıkınca
kapanır. Kutu tıklanmaz, sürüklenmez; ekran kenarına taşacaksa konumunu kendisi düzeltir.

## Takvimin üzerinde sağ tık

Masaüstündeki takvimin üzerine sağ tıklayınca Kalender'in kendi menüsü açılır. Masaüstünün geri
kalanında Windows'un kendi menüsü çıkmaya devam eder.

| Satır | Ne yapar |
| --- | --- |
| *(gün) gününe git* | O günü Google Takvim'de gün görünümünde açar |
| Önceki ay: … / Sonraki ay: … | Masaüstündeki takvimi bir ay geri ya da ileri alır |
| Bu aya dön | İçinde bulunduğunuz aya döner (zaten oradaysanız soluk durur) |
| Yeni etkinlik… | Google Takvim'in etkinlik ekranını o güne kurulu açar |
| Yeni görev… | Google Takvim'i o günün görünümünde açar (görev için doğrudan adres yok) |
| Ayarlar | Ayarlar penceresini açar |

Başka bir aya gittiğinizde takvim **2 dakika sonra kendiliğinden bu aya döner**. Ay adının ya
da gün harflerinin üzerine sağ tıklarsanız ilk satır *(ay) ayını Google Takvim'de aç* olur.

## Resmi tatiller

Tatil listesi Google'ın herkese açık Türkiye resmi tatil takviminden (ICS) alınır; hesap ya da
anahtar gerekmez. Liste bilgisayarınızda saklanır, 30 günde bir yenilenir; internet yokken en
son indirilen liste sessizce kullanılır. Tam gün tatiller kırmızıdır; arifeler (yarım günler)
kırmızı değildir, adları yalnızca gün kutusunda görünür; Anneler Günü gibi kutlama günleri
tatil sayılmaz. Listeyi elle yenilemek için tepsi menüsündeki **Şimdi yenile**.

> Dini bayram tarihleri idari kararla değişebilir; lütfen resmi kaynaklardan teyit ediniz.

## Verileriniz nerede duruyor?

Her şey `%LOCALAPPDATA%\Kalender` altındadır; başka hiçbir yere yazılmaz:

- `ayarlar.json` — ayarlarınız (Google istemci kimliği ve gizli anahtar dahil).
- `google-token.dat` — Google yetki anahtarı; Windows DPAPI ile **yalnızca sizin kullanıcı
  hesabınızın çözebileceği** biçimde şifrelidir.
- `cache\google.json` — görünen ayın etkinlik ve görev kopyası; `cache\tatiller.json` — tatiller.
- `zemin\` — duvar kağıdı olarak uygulanan görüntüler; `onceki-duvar-kagidi.json` — Kalender'den
  önceki duvar kağıdınızın yedeği; `logs\` — günlükler.

Ağa yalnızca Google'ın Takvim ve Görevler API'lerine ve tatil ICS adresine çıkılır. Kayıt
defterinde `HKCU\...\Run` (açılışta başlatma), `HKCU\...\Uninstall\Kalender` (uygulamalar
listesi) ve `HKCU\Control Panel\Desktop\JPEGImportQuality` (yedeklenir, geri alınır) yazılır.

## Kaldırma

Üç yoldan biriyle:

- **Ayarlar > Genel > Kalender'i kaldır**
- Windows **Ayarlar > Uygulamalar** listesinden Kalender'i seçip **Kaldır**
- Doğrudan: `%LOCALAPPDATA%\Kalender\app\Kalender.exe --kaldir`

Açılan pencerede **Ayarlarımı ve yerel veri kopyamı da sil** kutusu vardır; işaretlemezseniz
ayarlarınız kalır ve yeniden kurduğunuzda her şey yerinde olur. Kaldırma, Kalender'den önceki
duvar kağıdınızı geri yükler; kısayolu, açılış kaydını, uygulamalar listesindeki kaydı ve
birkaç saniye sonra dosyaları siler.

## Bilinen sınırlar

- **Yinelenen görevler tek gün görünür.** Google Görevler API'si yinelenen bir görevin yalnızca
  en son kopyasını verir; ileri tarihli kopyalar ve yineleme kuralı API'de yoktur. Görevlerde
  saat de gelmez: Kalender saati ancak Google o görevi takvim etkinliği olarak da döndürüyorsa
  gösterebilir, yoksa görev "Tüm gün" görünür. Tekrar eden işleri görev yerine **yinelenen
  etkinlik** olarak kurun; etkinliklerin bütün kopyaları eksiksiz gelir.
- **Takvim duvar kağıdının kendisidir.** Windows 11'in yeni yapılarında masaüstü katmanına
  çizim sessizce çalışmadığı için Kalender zemini ve takvimi tek görüntüde birleştirip
  `%LOCALAPPDATA%\Kalender\zemin` altına yazar ve duvar kağıdı yapar; görüntü yalnızca içeriği
  değiştiğinde yenilenir. Windows duvar kağıdını JPEG olarak önbelleğe aldığı için
  `JPEGImportQuality` 100'e çekilir. Duvar kağıdı bir ilkeyle kilitlenmişse takvim görünmez;
  tepside bir uyarı balonu çıkar.
