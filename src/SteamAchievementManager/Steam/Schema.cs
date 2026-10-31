namespace SteamAchievementManager.Steam;

public sealed record AchievementDef(
    string Id, string Name, string Description, string? Icon, string? IconLocked, bool Hidden, bool Protected);

public enum StatKind { Int, Float, AvgRate }

public sealed record StatDef(
    string Id, string Name, StatKind Kind, double Min, double Max, bool IncrementOnly, bool Protected);

/// <summary>Reads appcache/stats/UserGameStatsSchema_{appid}.bin, which Steam keeps up to date after a stats request.</summary>
public static class Schema
{
    public static bool TryLoad(string steamPath, uint appId, string language,
        out List<AchievementDef> achievements, out List<StatDef> stats)
    {
        achievements = [];
        stats = [];
        string file = Path.Combine(steamPath, "appcache", "stats", $"UserGameStatsSchema_{appId}.bin");
        var root = File.Exists(file) ? KeyValue.LoadBinary(file) : null;
        if (root == null || !root["stats"].Exists)
            return false;

        foreach (var stat in root["stats"].Children)
        {
            switch (stat["type"].AsString().ToUpperInvariant())
            {
                case "4" or "5" or "ACHIEVEMENTS" or "GROUPACHIEVEMENTS":
                    foreach (var bit in stat["bits"].Children)
                    {
                        string id = bit["name"].AsString();
                        if (id.Length == 0)
                            continue;
                        var display = bit["display"];
                        achievements.Add(new AchievementDef(
                            id,
                            Localized(display["name"], language, id),
                            Localized(display["desc"], language, ""),
                            NullIfEmpty(display["icon"].AsString()),
                            NullIfEmpty(display["icon_gray"].AsString()),
                            display["hidden"].AsBool(),
                            IsProtected(bit["permission"])));
                    }
                    break;

                case "1" or "INT":
                case "2" or "FLOAT":
                case "3" or "AVGRATE":
                    string statId = stat["name"].AsString();
                    if (statId.Length == 0)
                        continue;
                    var kind = stat["type"].AsString().ToUpperInvariant() switch
                    {
                        "1" or "INT" => StatKind.Int,
                        "2" or "FLOAT" => StatKind.Float,
                        _ => StatKind.AvgRate,
                    };
                    stats.Add(new StatDef(
                        statId,
                        Localized(stat["display"]["name"], language, statId),
                        kind,
                        stat["min"].Exists ? stat["min"].AsDouble() : double.MinValue,
                        stat["max"].Exists ? stat["max"].AsDouble() : double.MaxValue,
                        stat["incrementonly"].AsBool(),
                        IsProtected(stat["permission"])));
                    break;
            }
        }
        return true;
    }

    // Permission bits 1 and 2 mean only the game's servers may write the value.
    static bool IsProtected(KeyValue permission) => (permission.AsInt() & 3) != 0;

    static string Localized(KeyValue kv, string language, string fallback)
    {
        if (!kv.Exists)
            return fallback;
        if (kv.Value is string s)
            return s.Length > 0 ? s : fallback;
        foreach (var key in new[] { language, "english" })
        {
            string text = kv[key].AsString();
            if (text.Length > 0)
                return text;
        }
        return kv.Children.FirstOrDefault(c => c.Name != "token" && c.Value is string { Length: > 0 })?.AsString() ?? fallback;
    }

    static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
