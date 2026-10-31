using System.Globalization;

namespace SteamAchievementManager;

public static class Loc
{
    public static bool Turkish => Settings.Language == "tr";

    /// <summary>Language key used inside Steam's schema files.</summary>
    public static string SteamLanguage => Turkish ? "turkish" : "english";

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Turkish ? "tr-TR" : "en-US");

    public static string T(string key) =>
        Strings.TryGetValue(key, out var s) ? (Turkish ? s.Tr : s.En) : key;

    public static string F(string key, params object[] args) => string.Format(Culture, T(key), args);

    static readonly Dictionary<string, (string Tr, string En)> Strings = new()
    {
        ["games.count.one"] = ("1 oyun", "1 game"),
        ["games.count"] = ("{0} oyun", "{0} games"),
        ["games.loading"] = ("Oyunlar yükleniyor…", "Loading games…"),
        ["games.none"] = ("Kütüphanende başarımı olan oyun bulunamadı.", "No games with achievements found in your library."),
        ["games.noMatch"] = ("Aramana uyan oyun yok.", "No games match your search."),
        ["search.games"] = ("Oyun ara", "Search games"),
        ["appId"] = ("Oyun numarası", "App ID"),
        ["open"] = ("Aç", "Open"),
        ["refresh"] = ("Yenile", "Refresh"),
        ["retry"] = ("Tekrar dene", "Try again"),

        ["steam.NotInstalled"] = ("Steam bulunamadı. Bu bilgisayarda kurulu olduğundan emin ol.", "Steam couldn't be found. Make sure it's installed on this PC."),
        ["steam.LoadFailed"] = ("Steam'e bağlanılamadı.", "Couldn't connect to Steam."),
        ["steam.NotRunning"] = ("Steam açık değil. Steam'i açıp tekrar dene.", "Steam isn't running. Open Steam and try again."),
        ["steam.NotLoggedIn"] = ("Steam'de oturum açık değil. Giriş yapıp tekrar dene.", "You're not signed in to Steam. Sign in and try again."),

        ["tab.achievements"] = ("Başarımlar", "Achievements"),
        ["tab.stats"] = ("İstatistikler", "Stats"),
        ["filter.all"] = ("Tümü", "All"),
        ["filter.unlocked"] = ("Açık", "Unlocked"),
        ["filter.locked"] = ("Kilitli", "Locked"),
        ["search.achievements"] = ("Başarım ara", "Search achievements"),
        ["search.stats"] = ("İstatistik ara", "Search stats"),
        ["unlockAll"] = ("Hepsini aç", "Unlock all"),
        ["lockAll"] = ("Hepsini kilitle", "Lock all"),
        ["invert"] = ("Tersine çevir", "Invert"),
        ["save"] = ("Kaydet", "Save"),
        ["resetStats"] = ("Sıfırla", "Reset"),

        ["progress"] = ("{0} / {1} başarım açık", "{0} of {1} achievements unlocked"),
        ["unsaved.one"] = ("1 kaydedilmemiş değişiklik", "1 unsaved change"),
        ["unsaved"] = ("{0} kaydedilmemiş değişiklik", "{0} unsaved changes"),
        ["hidden"] = ("Gizli başarım", "Hidden achievement"),
        ["protected"] = ("Korumalı: bu başarımı sadece oyunun kendisi değiştirebilir.", "Protected: only the game itself can change this achievement."),
        ["protectedStat"] = ("Korumalı", "Protected"),
        ["incrementOnly"] = ("Sadece artabilir", "Can only increase"),
        ["readOnly"] = ("Değiştirilemez", "Read-only"),
        ["loading"] = ("Yükleniyor…", "Loading…"),
        ["noAchievements"] = ("Bu oyunun başarımı yok.", "This game has no achievements."),
        ["noStats"] = ("Bu oyunun istatistiği yok.", "This game has no stats."),
        ["noMatch"] = ("Aramana uyan sonuç yok.", "Nothing matches your search."),
        ["loadFailed"] = ("Başarımlar Steam'den alınamadı. Bu oyun kütüphanende mi?", "Couldn't get achievements from Steam. Is this game in your library?"),
        ["schemaMissing"] = ("Bu oyunun başarım bilgisi bulunamadı.", "Couldn't find achievement data for this game."),
        ["saving"] = ("Kaydediliyor…", "Saving…"),
        ["saved"] = ("Değişiklikler kaydedildi.", "Changes saved."),
        ["saveFailed"] = ("Steam bazı değişiklikleri kabul etmedi.", "Steam didn't accept some of the changes."),
        ["resetDone"] = ("İstatistikler sıfırlandı.", "Stats were reset."),
        ["resetFailed"] = ("Sıfırlama başarısız oldu.", "Reset failed."),

        ["confirm.close.title"] = ("Kaydedilmemiş değişiklikler", "Unsaved changes"),
        ["confirm.close.text"] = ("Kaydetmediğin değişiklikler var. Kaydetmeden kapatılsın mı?", "You have changes you haven't saved. Close without saving?"),
        ["confirm.close.ok"] = ("Kaydetmeden kapat", "Close without saving"),
        ["confirm.reset.title"] = ("İstatistikleri sıfırla", "Reset stats"),
        ["confirm.reset.text"] = ("Bu oyundaki tüm istatistikler sıfırlanacak. Bu işlem geri alınamaz.", "All stats for this game will be reset. This can't be undone."),
        ["confirm.reset.stats"] = ("Sadece istatistikler", "Stats only"),
        ["confirm.reset.all"] = ("Başarımlar dahil", "Including achievements"),
        ["cancel"] = ("Vazgeç", "Cancel"),
        ["error.title"] = ("Bir hata oluştu", "Something went wrong"),
    };
}
