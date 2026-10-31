using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace SteamAchievementManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        Settings.Load();

        // The library window starts each game in its own process: "--app <id> --lang <tr|en>".
        uint appId = 0;
        for (int i = 0; i + 1 < e.Args.Length; i++)
        {
            if (e.Args[i] == "--app")
                uint.TryParse(e.Args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out appId);
            else if (e.Args[i] == "--lang" && e.Args[i + 1] is "tr" or "en")
                Settings.Language = e.Args[i + 1];
        }

        MainWindow = appId != 0 ? new GameWindow(appId) : new PickerWindow();
        MainWindow.Show();
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.Data, "error.log"), $"[{DateTime.Now:u}] {e.Exception}\n\n");
        }
        catch (IOException) { }
        ConfirmDialog.Show(MainWindow, Loc.T("error.title"), e.Exception.Message, "OK");
        e.Handled = true;
    }
}
