using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SteamAchievementManager;
using SteamAchievementManager.Steam;

// Developer checks against the real Steam client:
//   games                         list owned games with achievements
//   ach <appid>                   print achievements and stats
//   toggle <appid> <achievement>  flip one achievement, verify, flip it back, verify
//   render picker <png> [tr|en]   draw the library window to an image
//   render game <appid> <png> [tr|en]
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "games" => Games(),
                "ach" => Achievements(uint.Parse(args[1])),
                "toggle" => Toggle(uint.Parse(args[1]), args[2]),
                "render" => Render(args[1..]),
                _ => Usage(),
            };
        }
        catch (SteamUnavailableException e)
        {
            Console.WriteLine($"STEAM UNAVAILABLE: {e.Error}");
            return 2;
        }
    }

    static int Usage()
    {
        Console.WriteLine("games | ach <appid> | toggle <appid> <id> | render picker <png> [lang] | render game <appid> <png> [lang]");
        return 1;
    }

    static int Games()
    {
        var sw = Stopwatch.StartNew();
        using var steam = new SteamClient(0);
        Console.WriteLine($"Steam: {steam.InstallPath}, SteamID {steam.SteamId}");
        var ids = GameList.CandidateIdsAsync(steam.InstallPath, default).GetAwaiter().GetResult();
        Console.WriteLine($"candidates: {ids.Count} ({sw.ElapsedMilliseconds} ms)");
        var games = GameList.Owned(steam, ids);
        Console.WriteLine($"owned: {games.Count} ({sw.ElapsedMilliseconds} ms)");
        foreach (var g in games)
            Console.WriteLine($"  {g.AppId,-8} {g.Name}  [{(g.LocalImage != null ? "local image" : "cdn image")}]");
        return games.Count > 0 ? 0 : 3;
    }

    static int Achievements(uint appId)
    {
        using var steam = new SteamClient(appId);
        int result = RequestStats(steam);
        Console.WriteLine($"stats received: {result}");
        if (!Schema.TryLoad(steam.InstallPath, appId, "turkish", out var achievements, out var stats))
        {
            Console.WriteLine("schema missing");
            return 3;
        }
        int read = 0, unlocked = 0;
        foreach (var a in achievements)
        {
            if (!steam.TryGetAchievement(a.Id, out bool achieved, out var at))
            {
                Console.WriteLine($"  ?? {a.Id} (not readable)");
                continue;
            }
            read++;
            if (achieved) unlocked++;
            Console.WriteLine($"  [{(achieved ? "x" : " ")}] {a.Id} | {a.Name} | {at:yyyy-MM-dd HH:mm} | hidden={a.Hidden} protected={a.Protected} icon={(a.Icon != null)}/{(a.IconLocked != null)}");
        }
        Console.WriteLine($"achievements: {read}/{achievements.Count} readable, {unlocked} unlocked");
        foreach (var s in stats)
        {
            string value = s.Kind == StatKind.Int
                ? steam.TryGetStat(s.Id, out int i) ? i.ToString() : "??"
                : steam.TryGetStat(s.Id, out float f) ? f.ToString("R") : "??";
            Console.WriteLine($"  stat {s.Id} ({s.Kind}) = {value} | {s.Name} | incOnly={s.IncrementOnly} protected={s.Protected}");
        }
        return read == achievements.Count ? 0 : 4;
    }

    static int Toggle(uint appId, string id)
    {
        using var steam = new SteamClient(appId);
        Console.WriteLine($"stats received: {RequestStats(steam)}");
        if (!steam.TryGetAchievement(id, out bool original, out var originalTime))
        {
            Console.WriteLine("achievement not readable");
            return 3;
        }
        Console.WriteLine($"start: {id} achieved={original} at {originalTime}");

        bool ok = SetAndVerify(steam, id, !original) && SetAndVerify(steam, id, original);
        steam.TryGetAchievement(id, out bool final, out var finalTime);
        Console.WriteLine($"end: {id} achieved={final} at {finalTime}");
        Console.WriteLine(ok && final == original ? "TOGGLE OK" : "TOGGLE FAILED");
        return ok && final == original ? 0 : 5;
    }

    static bool SetAndVerify(SteamClient steam, string id, bool achieved)
    {
        bool set = steam.SetAchievement(id, achieved);
        bool store = steam.StoreStats();
        int stored = Wait(steam, SteamClient.UserStatsStoredId);
        int fetched = RequestStats(steam);
        steam.TryGetAchievement(id, out bool now, out var at);
        Console.WriteLine($"  set {achieved}: call={set} store={store} stored={stored} refetch={fetched} -> achieved={now} at {at}");
        return now == achieved;
    }

    static int RequestStats(SteamClient steam)
    {
        steam.RequestStats();
        return Wait(steam, SteamClient.UserStatsReceivedId);
    }

    static int Wait(SteamClient steam, int callbackId)
    {
        int result = 0;
        void Handler(int id, IntPtr payload)
        {
            if (id != callbackId) return;
            var (app, res) = SteamClient.ReadStatsResult(payload);
            if (app == steam.AppId) result = res;
        }
        steam.Callback += Handler;
        var sw = Stopwatch.StartNew();
        while (result == 0 && sw.Elapsed < TimeSpan.FromSeconds(10))
        {
            steam.RunCallbacks();
            Thread.Sleep(50);
        }
        steam.Callback -= Handler;
        return result;
    }

    static int Render(string[] args)
    {
        bool game = args[0] == "game";
        uint appId = game ? uint.Parse(args[1]) : 0;
        string output = args[game ? 2 : 1];
        string? lang = args.Length > (game ? 3 : 2) ? args[game ? 3 : 2] : null;
        if (lang != null)
            Settings.Language = lang;
        // Extra words after the language: control names to click, or "text:<Name>=<value>" to type.
        var actions = args.Skip(game ? 4 : 3).ToList();

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SteamAchievementManager;component/Theme.xaml"),
        });
        int exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                Window window = game ? new GameWindow(appId) : new PickerWindow();
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -32000;
                window.Top = -32000;
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Show();
                await (window is GameWindow g ? g.Ready : ((PickerWindow)window).Ready);
                await Task.Delay(4000); // let images arrive
                foreach (var action in actions)
                {
                    if (action.StartsWith("sleep:"))
                    {
                        await Task.Delay(int.Parse(action[6..]));
                        continue;
                    }
                    if (action.StartsWith("text:"))
                    {
                        var (name, value) = (action[5..].Split('=')[0], action[(action.IndexOf('=') + 1)..]);
                        ((System.Windows.Controls.TextBox)window.FindName(name)).Text = value;
                    }
                    else if (window.FindName(action) is System.Windows.Controls.Primitives.ToggleButton toggle)
                        toggle.IsChecked = toggle is System.Windows.Controls.RadioButton || toggle.IsChecked != true;
                    else
                        ((System.Windows.Controls.Button)window.FindName(action)).RaiseEvent(
                            new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    await Task.Delay(300);
                }
                Save(window, output);
                window.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                exit = 6;
            }
            app.Shutdown();
        };
        app.Run();
        return exit;
    }

    static void Save(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        double w = content.ActualWidth, h = content.ActualHeight;
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
        var bitmap = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        Console.WriteLine($"rendered {w}x{h} -> {path}");
    }
}
