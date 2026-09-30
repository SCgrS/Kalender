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
- Ay adının hizasındaki **‹ • ›** düğmeleriyle önceki ya da sonraki aya geçilir, nokta bu aya
  döndürür; takvime 30 saniye dokunulmazsa kendiliğinden bu aya döner.
- Takvimin üzerine sağ tıklayınca güne gitme, ay değiştirme ve yeni kayıt açma menüsü çıkar.
- Zemin düz renk, uygulamayla gelen dört fotoğraftan biri ya da kendi fotoğrafınız olabilir;
  konum, boyut ve fotoğraf her monitör için ayrı ayarlanır.
- Takvim ilk açılışta her ekranın sağ alt köşesinde durur: sağında bir masaüstü simgesi (ör. bir
  klasör) sığacak kadar boşluk kalır, görev çubuğunun biraz üstündedir.
- Kalender kapalıyken duvar kağıdında takvimsiz zemin kalır; ekran farklı görünmez.

## Kurulum

### [⬇ Kalender-Kurulum.exe indir](https://github.com/SCgrS/Kalender/releases/latest/download/Kalender-Kurulum.exe)

Tek dosya, yaklaşık 144 MB. Çalışması için gereken her şey içinde; ayrıca .NET kurmanız gerekmez.
Windows 10 (20H2 ve üzeri) ya da Windows 11, 64 bit; yönetici hakkı gerekmez.

> Depo özel (private) olduğu için indirme bağlantısı yalnızca GitHub'da oturumu açık olan
> yetkili hesaplarda çalışır. Tüm sürümler: [Sürümler sayfası](https://github.com/SCgrS/Kalender/releases)

1. Yukarıdaki bağlantıya tıklayıp dosyayı indirin.
2. İndirilen `Kalender-Kurulum.exe` dosyasına **çift tıklayın**.
3. Dosya imzalı olmadığı için Windows SmartScreen uyarı gösterebilir: **Daha fazla bilgi**
   yazısına tıklayın, sonra **Yine de çalıştır** düğmesine basın. Bu uyarı yalnızca kurucu
   için ve bir kez çıkar: kurulan kopya "internetten indirildi" işaretini taşımaz, Windows
   sonraki açılışlarda yeniden sormaz.
4. Açılan pencerede **Kur**'a basın. Kurulum birkaç saniye sürer, ardından Kalender başlar.

Kurulum sistem klasörlerine hiçbir şey yazmaz:

- Uygulama `%LOCALAPPDATA%\Kalender\app` klasörüne kopyalanır.
- Başlat menüsüne **Kalender** kısayolu eklenir.
- Windows açılışında başlaması için yalnızca kendi kullanıcı kaydınıza (`HKCU\...\Run`) yazılır.
- Windows'un **Uygulamalar** listesine kaydedilir; oradan da kaldırabilirsiniz.

### Güncelleme

1.8.0'dan itibaren Kalender yeni sürümü kendisi bulur ve haber verir. Açılıştan bir dakika sonra,
ardından birkaç saatte bir yeni sürüm olup olmadığına bakar; varsa kurulum penceresine benzeyen bir
pencere açılır: **Kalender'in yeni sürümü çıktı**. **Güncelle**'ye basınca kurulum dosyası
indirilir, SHA-256 özeti ve sürüm bilgisi doğrulanır, Kalender kapanıp yeni sürüm kurulur ve
kendiliğinden yeniden açılır; takvim bu sırada masaüstünden kalkmaz. **Sonra**'ya basarsanız pencere
kapanır ve bir hafta sonra yeniden hatırlatılır.

Beklemek istemezseniz ayarlar penceresinin sol altındaki **Yeni sürüm: …** bildirimine tıklayarak
da güncelleyebilirsiniz. Ayarlarınız, Google bağlantınız ve yerel veri kopyanız uygulama klasörünün
dışında durduğu için güncellemede korunur. GitHub'a sürüm numarası dışında bir bilgi gitmez. Denetim
**Ayarlar > Genel > Güncellemeleri denetle** ile kapatılabilir; o zaman pencere de açılmaz.

Elle güncellemek için yeni sürümün `Kalender-Kurulum.exe` dosyasını indirip çalıştırın, açılan
pencerede **Güncelle**'ye basın. 1.7.0 ve öncesinde bildirim yoktur; bu sürümlerden 1.8.0'a bir kez
elle geçilir.

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
5. Sol menüden **Branding**'e girin. **Application home page** kutusuna
   `https://github.com/SCgrS/Kalender`, **Application privacy policy link** kutusuna
   `https://github.com/SCgrS/Kalender#verileriniz-nerede-duruyor` yazın. **Authorized
   domains** altında **+ Add domain** ile `github.com` ekleyip **Save**'e basın. Logo
   eklemeyin; logo, Google'ın uygulamayı ayrıca incelemesini gerektirir.
6. Sol menüden **Audience**'a girip **Publish app** → **Confirm**'e basın; durum **In
   production** olur. Yayınlamazsanız uygulama **Testing** durumunda kalır: kendinizi **Test
   users** bölümüne eklemeniz gerekir ve Google bağlantıyı 7 günde bir düşürür.

### 4. İstemci kimliğini oluşturun

1. Sol menüden **Clients** → **+ Create client**.
2. Application type: **Desktop app**. Ad: `Kalender` → **Create**.
3. Çıkan pencerede **Client ID** ve **Client secret** yazar; ikisini de kopyalayın. Pencereyi
   kapatsanız bile Clients listesinden yeniden açabilirsiniz.

### 5. Kalender'e yapıştırın

1. Tepsi simgesine tıklayıp **Ayarlar**'ı açın, soldan **Google** sayfasına geçin.
2. İki değeri **İstemci kimliği (Client ID)** ve **İstemci gizli anahtarı (Client secret)**
   kutularına yapıştırın.
3. **Google'a bağlan**'a basın; tarayıcıda Google onay ekranı açılır.
4. "Google bu uygulamayı doğrulamadı" uyarısı çıkarsa **Gelişmiş > Kalender'e git (güvenli
   değil)** yolunu izleyin; uygulama Google'ca incelenmediği için bu uyarı beklenen bir durum.
5. İzin verince tarayıcıda "Kalender bağlandı" yazısını görürsünüz; sekmeyi kapatabilirsiniz.

Bağlanırken durum satırının yanında dönen bir çember görünür: önce tarayıcıdaki onay
beklenir, sonra takvimler ve görevler ilk kez alınır; bu birkaç saniye sürebilir. Bekleyişi
bırakmak için aynı düğmeye (**Vazgeç**) basın. Bir sorunla karşılaşırsanız **Bağlantıyı kes**'e
basın, ardından **Google'a bağlan** ile yeniden bağlanıp bir süre bekleyin.

Bağlandıktan sonra aynı sayfada **Ne sıklıkla yenilensin?** (30 saniye – 24 saat),
**Gösterilecek takvimler**, **Gösterilecek görev listeleri** ve **Görev noktacığı rengi**
seçenekleri açılır. Bağlantıyı koparmak için **Bağlantıyı kes**: yetki dosyası ve yerel veri
kopyası silinir.

Google izni kendiliğinden geçersiz sayarsa (izni geri aldıysanız ya da uygulama hâlâ
**Testing** durumundaysa) Kalender bağlantıyı keser ve tepside bir uyarı balonu gösterir;
**Google'a bağlan** ile yeniden bağlanın.

## Ayarlar

Tepsi (saatin yanındaki bildirim alanı) simgesine **tıklayın** ya da sağ tıklayıp **Ayarlar**'ı
seçin. Kalender açıkken `Kalender.exe`'yi yeniden çalıştırmak da ayarları açar. Her değişiklik
anında masaüstüne uygulanır; kaydetmeniz gerekmez. Pencerenin solunda beş sayfa vardır:

- **Arka plan** — **Hazır renkler** (koyudan açığa sekiz renk: Siyah, Kuzgun, Mürekkep, Gece
  Mavisi, Kömür, Kum Taşı, Kum, Kağıt), **Kendi rengim** (düğmeye tıklayınca renk çarkı
  açılır; `#2E3238` gibi kodu elle de yazabilirsiniz), **Hazır fotoğraflar** (uygulamayla gelen
  dört koyu fotoğraf; tıklayınca zemin o olur) ya da **Fotoğraf seç…**. Fotoğrafta
  **Yerleşim**: Kapla, Sığdır, Ortala, Döşe. **Takvimin arkasını karart** (yalnız fotoğraf
  zeminde) takvimi ortalayan bir kare koyar: değer arttıkça koyulaşır, köşeleri düzleşir ve
  kenarı keskinleşir; %100'de köşesiz, tam opak bir kare olur.
- **Renkler** — **Takvim renkleri**: **Yazı rengi**, **Hafta günleri ve ay adı**, **Tatil ve
  hafta sonu günleri**, **Pasif günler**, **Bugünün dairesi**. Hepsi **Otomatik** başlar; renk
  düğmesinden seçince o satır elle seçilmiş olur (fotoğraf zeminde yazı rengini siz seçersiniz).
  Gün altındaki noktacıklar buraya dahil değildir; renkleri Google'dan gelir. **Tatiller**:
  **Resmi tatilleri kırmızı göster**, **Cumartesi de pazar gibi kırmızı olsun**. **Gün
  kutusu**: **Kutunun opaklığı** (%40–100); yazılar her değerde tam opak kalır.
- **Boyut ve Konum** — **Tüm ekranlarda**, **Yalnız ana ekranda** ya da **Seçtiğim ekranlarda**
  (**Bu ekranda göster** kutusu). Her ekran için **Takvimi taşı** (sürükleyip bırakın;
  kenarlara ve ortaya yaklaşınca hizalanır, Esc vazgeçer), **Boyut** (%80–150) ve **Bu ekrana
  fotoğraf…**. **Varsayılana dön** takvimi her ekranda sağ alt köşedeki ilk yerine ve %100
  boyutuna getirir (önce sorar; ekran seçimi ve ekran fotoğrafları değişmez).
- **Google** — hesap bağlantısı, istemci bilgileri ve bağlandıktan sonraki seçenekler;
  yukarıdaki bölümde anlatıldı.
- **Genel** — **Windows açılışında Kalender'i başlat**, **Takvimi Windows duvar kağıdına
  işle** (kapatırsanız duvar kağıdınıza dokunulmaz ama takvim de görünmez), **Günlük
  ayrıntısı** (Yalnız hatalar / Bilgi / Ayrıntılı) ve **Günlük klasörünü aç**; **Hakkında**
  bölümünde sürüm ve güncelleme durumu, geliştirici bağlantısı
  ([x.com/CgrShn](https://x.com/CgrShn)), **Sürümler sayfası**, **Güncellemeleri denetle** ve
  **Kalender'i kaldır**.

Sol alttaki **güneş / ay** anahtarı yalnızca ayarlar penceresinin görünümünü değiştirir; ilk
açılışta Windows'un uygulama teması izlenir.

## Tepsi menüsü

Saatin yanındaki Kalender simgesine sağ tıklayın; simge görünmüyorsa görev çubuğundaki **^**
(gizli simgeler) okunun altındadır:

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

Kutunun zemini varsayılan olarak %95 opaktır. **Ayarlar > Renkler > Gün kutusu > Kutunun
opaklığı** ile %40'a kadar indirebilirsiniz; yazılar her değerde tam opak kalır.

## Ay düğmeleri

Ay adının hizasında, sağda üç düğme durur: **‹** Cuma ile Cumartesi'nin, **›** Cumartesi ile
Pazar'ın tam üstünde, aralarındaki **•** nokta Cumartesi'nin üstündedir. Oklar takvimi bir ay
geri ya da ileri götürür, nokta içinde bulunduğunuz aya döndürür (bu aydayken soluk durur). Ay,
fare tuşuna basınca hemen değişir. Düğmeye tıklamak masaüstüne geçmez (simge seçimi ya da seçim
kutusu başlamaz); takvim duvar kağıdının parçası olduğu için düğmeler üzerine gelince değişmez.

Yeni ayın etkinlikleri Google'dan alınır. Son gösterilen birkaç ayın verisi bellekte tutulur:
o aylara (ve 30 saniye sonra bu aya) dönünce noktacıklar beklemeden görünür, ardından yalnız
değişenler Google'dan tazelenir. Google'dan yine yalnızca görünen ay istenir.

Takvimin üstüne bir masaüstü simgesi (klasör, kısayol) koyarsanız tıklama simgeye gider:
simgenin altında kalan düğme çalışmaz, simgeye sağ tıklayınca simgenin kendi menüsü açılır,
simgenin altındaki günün kutusu açılmaz.

Başka bir aya gittiğinizde takvime **30 saniye dokunulmazsa kendiliğinden bu aya döner**. Ay
düğmesine ya da sağ tık menüsüne tıklamak ve fareyi takvimin üzerinde gezdirmek (örneğin gün
kutusunu okurken) dokunmak sayılır; süre her seferinde baştan başlar.

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

Ay adının ya da gün harflerinin üzerine sağ tıklarsanız ilk satır *(ay) ayını Google Takvim'de
aç* olur.

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
- `duvar-kagitlari\` — hazır fotoğraflardan seçtiğiniz, uygulamanın içinden çıkarılan fotoğraf
  (seçmezseniz klasör oluşmaz).

Güncelleme sırasında indirilen kurulum dosyası geçici olarak `%TEMP%\Kalender guncelleme`
klasöründe durur; sonraki denetimde silinir.

Ağa yalnızca Google'ın Takvim ve Görevler API'lerine, tatil ICS adresine ve yeni sürüm denetimi
için GitHub'daki sürümler sayfasına çıkılır (sonuncusu kapatılabilir). Kayıt
defterinde `HKCU\...\Run` (açılışta başlatma), `HKCU\...\Uninstall\Kalender` (uygulamalar
listesi) ve `HKCU\Control Panel\Desktop\JPEGImportQuality` (yedeklenir, geri alınır) yazılır.

## Kaldırma

Üç yoldan biriyle:

- **Ayarlar > Genel > Kalender'i kaldır**
- Windows **Ayarlar > Uygulamalar** listesinden Kalender'i seçip **Kaldır**
- Doğrudan: `%LOCALAPPDATA%\Kalender\app\Kalender.exe --kaldir`

Açılan pencerede **Ayarlarımı ve yerel veri kopyamı da sil** kutusu vardır; işaretlemezseniz
ayarlarınız kalır ve yeniden kurduğunuzda her şey yerinde olur. Kaldırma, Kalender'den önceki
duvar kağıdınızı geri yükler; kısayolu, açılış kaydını ve uygulamalar listesindeki kaydı siler.
Program dosyaları ve Windows'un geçici klasörüne açtığı kütüphaneler, kaldırma penceresi
kapanınca silinir (kullanıcı adında Türkçe harf olsa da). Bu silme yarım kalırsa Windows kalanı
bir sonraki oturum açılışında tamamlar.

## Bilinen sınırlar

- **Yinelenen görevler tek gün görünür.** Google Görevler API'si yinelenen bir görevin yalnızca
  en son kopyasını verir; ileri tarihli kopyalar ve yineleme kuralı API'de yoktur. Görevlerde
  saat de gelmez: Kalender saati ancak Google o görevi takvim etkinliği olarak da döndürüyorsa
  gösterebilir, yoksa görev "Tüm gün" görünür. Tekrar eden işleri görev yerine **yinelenen
  etkinlik** olarak kurun; etkinliklerin bütün kopyaları eksiksiz gelir.
- **Takvim duvar kağıdının kendisidir.** Windows 11'in yeni yapılarında masaüstü katmanına
  çizim sessizce çalışmadığı için Kalender zemini ve takvimi tek görüntüde birleştirip
  `%LOCALAPPDATA%\Kalender\zemin` altına yazar ve duvar kağıdı yapar; görüntü içeriği
  değiştiğinde yenilenir. Kalender ayrıca yarım dakikada bir Windows'un gerçekten bu görüntüyü
  gösterdiğine bakar, başka bir şey görünüyorsa yeniden yazar. Windows açılışta ya da uykudan
  uyanınca ilk yazılan görüntüyü bazen göstermediği için Kalender açılıştan bir dakika, uyanmadan
  20 saniye sonra görüntüyü bir kez daha yazar. Ekran kapanıp açılırken ya da
  bilgisayar uykudan uyanırken Windows monitörü birkaç saniye tanımayabilir; Kalender bu arada
  çizmez, monitör tanınınca takvimi kayıtlı yerine koyar. Windows duvar kağıdını JPEG olarak önbelleğe aldığı için
  `JPEGImportQuality` 100'e çekilir. Duvar kağıdı bir ilkeyle kilitlenmişse takvim görünmez;
  tepside bir uyarı balonu çıkar.
