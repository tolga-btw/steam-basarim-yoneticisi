using System.Xml.Linq;

namespace SteamAchievementManager.Steam;

public sealed record GameInfo(uint AppId, string Name, string? LocalImage, string ImageUrl);

public static class GameList
{
    // The same community-maintained list of app ids with achievements that SAM uses.
    const string ListUrl = "https://gib.me/sam/games.xml";

    public static async Task<HashSet<uint>> CandidateIdsAsync(string steamPath, CancellationToken ct)
    {
        var ids = new HashSet<uint>();
        string cacheFile = Path.Combine(AppPaths.Cache, "games.xml");
        string? xml = null;
        try
        {
            xml = await Http.Client.GetStringAsync(ListUrl, ct);
            await File.WriteAllTextAsync(cacheFile, xml, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            if (File.Exists(cacheFile))
                xml = await File.ReadAllTextAsync(cacheFile, ct);
        }

        if (xml != null)
        {
            try
            {
                foreach (var game in XDocument.Parse(xml).Root?.Elements("game") ?? [])
                    if ((string?)game.Attribute("type") != "junk" && uint.TryParse(game.Value, out var id))
                        ids.Add(id);
            }
            catch (System.Xml.XmlException) { }
        }

        // Games whose stats Steam has already fetched, in case the online list misses them.
        string statsDir = Path.Combine(steamPath, "appcache", "stats");
        if (Directory.Exists(statsDir))
        {
            const string prefix = "UserGameStatsSchema_";
            foreach (var file in Directory.EnumerateFiles(statsDir, prefix + "*.bin"))
                if (uint.TryParse(Path.GetFileNameWithoutExtension(file)[prefix.Length..], out var id))
                    ids.Add(id);
        }
        return ids;
    }

    public static List<GameInfo> Owned(SteamClient steam, IEnumerable<uint> ids)
    {
        var games = new List<GameInfo>();
        foreach (var id in ids)
        {
            if (!steam.IsSubscribedApp(id))
                continue;
            string name = steam.GetAppData(id, "name");
            games.Add(Describe(steam.InstallPath, id, string.IsNullOrWhiteSpace(name) ? $"App {id}" : name));
        }
        games.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return games;
    }

    public static GameInfo Describe(string steamPath, uint appId, string name)
    {
        string cache = Path.Combine(steamPath, "appcache", "librarycache");
        string? local = new[]
        {
            Path.Combine(cache, appId.ToString(), "header.jpg"),
            Path.Combine(cache, $"{appId}_header.jpg"),
        }.FirstOrDefault(File.Exists);
        return new GameInfo(appId, name, local,
            $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/header.jpg");
    }

    public static string AchievementIconUrl(uint appId, string icon) =>
        $"https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/{appId}/{icon}";
}
