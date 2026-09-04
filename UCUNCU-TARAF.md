# Üçüncü taraf bileşenler

Bu dosyada, Kalender'in kullandığı üçüncü taraf ve bağımsız geliştirici paketleri listelenir.
Microsoft'un ve Google'ın kendi resmi kütüphaneleri bu listeye girmez.

README'deki teşekkür/atıf bölümü bu listeden hazırlanacaktır.

## Şu an kullanılanlar

| Paket | Ne işe yarıyor | Lisans | Proje |
| --- | --- | --- | --- |
| _(henüz yok)_ | | | |

2. aşama sonunda proje hâlâ hiçbir üçüncü taraf pakete bağlı değil. Bilerek böyle yapıldı:

- **Tepsi simgesi** için hazır bir kütüphane yerine doğrudan `Shell_NotifyIcon` çağrıldı.
  Böylece Explorer yeniden başladığında simgeyi geri koymak gibi ayrıntılar elimizde kaldı
  ve uygulamaya fazladan bağımlılık girmedi.
- **Renk çarkı** (`Ui/ColorWheel`) elle yazıldı. Ton/doygunluk çarkı bir kez üretilip
  önbelleğe alınıyor, parlaklık üstteki siyah örtünün saydamlığıyla gösteriliyor.
  Hazır bir denetim eklemeye değecek kadar karmaşık değildi.
- **Çizim** için `System.Drawing.Common` kullanılıyor; bu Microsoft'un kendi paketi.

## İlerideki aşamalarda beklenenler

Aşağıdakiler henüz eklenmedi; eklendiklerinde bu dosyada gerçek satırlar hâline gelecek.

- **ICS ayrıştırma** (3. aşama): resmi tatil takvimini okumak için. Basit bir okuyucu kendimiz
  yazılabilir; hazır bir paket kullanılırsa buraya yazılacak.
- **SQLite** (4. aşama): yerel önbellek için. JSON yeterli gelirse hiç kullanılmayacak.
