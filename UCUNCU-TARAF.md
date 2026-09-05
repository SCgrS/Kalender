# Üçüncü taraf bileşenler

Bu dosyada, Kalender'in kullandığı üçüncü taraf ve bağımsız geliştirici paketleri listelenir.
Microsoft'un ve Google'ın kendi resmi kütüphaneleri bu listeye girmez.

README'deki teşekkür/atıf bölümü bu listeden hazırlanacaktır.

## Şu an kullanılanlar

| Paket | Ne işe yarıyor | Lisans | Proje |
| --- | --- | --- | --- |
| _(henüz yok)_ | | | |

6. aşama sonunda proje hâlâ hiçbir üçüncü taraf pakete bağlı değil. Bilerek böyle yapıldı:

- **Tepsi simgesi** için hazır bir kütüphane yerine doğrudan `Shell_NotifyIcon` çağrıldı.
  Böylece Explorer yeniden başladığında simgeyi geri koymak gibi ayrıntılar elimizde kaldı
  ve uygulamaya fazladan bağımlılık girmedi.
- **Renk çarkı** (`Ui/ColorWheel`) elle yazıldı. Ton/doygunluk çarkı bir kez üretilip
  önbelleğe alınıyor, parlaklık üstteki siyah örtünün saydamlığıyla gösteriliyor.
  Hazır bir denetim eklemeye değecek kadar karmaşık değildi.
- **ICS okuyucu** (`Calendar/Holidays/IcsParser`) elle yazıldı. Tam bir takvim kütüphanesi
  değil; yalnızca resmi tatil akışındaki tüm gün süren kayıtları okuyacak kadarını yapıyor
  (satır katlaması, kaçış dizileri, DTSTART/DTEND/SUMMARY/DESCRIPTION). İhtiyaç bu kadar dar
  olduğu için paket eklenmedi.
- **Google bağlantısı** için Google'ın .NET kütüphaneleri yerine doğrudan REST çağrıları ve
  kendi yazdığımız OAuth 2.0 + PKCE akışı kullanıldı. Üç nedenle: tek dosya yayın küçük kalıyor,
  isteklerde `fields` parametresiyle veriyi kırpabiliyoruz (şartnamedeki az veri kuralı) ve
  uygulamaya hiçbir gizli anahtar gömmek zorunda kalmıyoruz. Google'ın kendi kütüphaneleri
  zaten bu listeye girmezdi; mesele boyut ve denetim.
- **Çizim** için `System.Drawing.Common` kullanılıyor; bu Microsoft'un kendi paketi.

## İlerideki aşamalarda beklenenler

Aşağıdakiler henüz eklenmedi; eklendiklerinde bu dosyada gerçek satırlar hâline gelecek.

- Şu an bir ihtiyaç görünmüyor. Önbellekler küçük JSON dosyaları olarak yeterli oldu
  (tatiller ~2 KB, Google verisi görünen ay kadar), bu yüzden SQLite'a gerek kalmadı.
