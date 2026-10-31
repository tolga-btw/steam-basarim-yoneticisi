# Steam Achievement Manager

Steam oyunlarındaki başarımları ve istatistikleri açıp kapatmaya yarayan Windows uygulaması.
Koyu tema, Türkçe / İngilizce arayüz.

## Nasıl çalışır

- Steam açık ve oturum açılmış olmalı.
- Açılan pencere kütüphanendeki başarımlı oyunları listeler. Bir oyuna tıklayınca o oyun ayrı bir
  pencerede (ayrı bir süreçte) açılır; Steam bağlantısı oyun başına kurulduğu için böyle.
- Başarımları anahtarla aç/kapat, istatistikleri kutudan değiştir, sonra **Kaydet**.
- Korumalı (kilit simgeli) başarım ve istatistikleri sadece oyunun kendisi değiştirebilir.

## Derleme

.NET 10 SDK gerekir.

```
dotnet publish src/SteamAchievementManager -c Release -o publish
```

Çıktı `publish/SteamAchievementManager.exe` tek dosyadır. Çalışması için bilgisayarda
.NET 10 Masaüstü Çalışma Zamanı (Windows Desktop Runtime) kurulu olmalı.

## Klasörler

- `src/SteamAchievementManager` — uygulama
  - `Steam/` — Steam istemcisiyle konuşan kısım (steamclient64.dll arayüzleri, şema dosyası okuma, oyun listesi)
  - `Theme.xaml` — renkler ve kontrol stilleri
  - `Loc.cs` — Türkçe / İngilizce metinler
- `tools/SelfTest` — geliştirme sırasında kullanılan test aracı (oyunları listeleme, başarım okuma,
  pencereleri resme çizme). Uygulamaya dahil değildir.

## Notlar

- Oyun listesi, SAM'in de kullandığı https://gib.me/sam/games.xml listesinden ve Steam'in
  `appcache/stats` klasöründen gelir; sonra hangilerine sahip olduğun Steam'e sorulur.
- Ayarlar `%APPDATA%\SteamAchievementManager`, görsel önbelleği `%LOCALAPPDATA%\SteamAchievementManager` içinde tutulur.
