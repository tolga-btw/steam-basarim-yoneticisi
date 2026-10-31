using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager;

public partial class PickerWindow : Window
{
    readonly ObservableCollection<GameItem> _games = [];
    readonly ICollectionView _view;
    readonly TaskCompletionSource _ready = new();
    SteamClient? _steam;
    CancellationTokenSource? _loading;
    string? _messageKey;
    bool _canRetry;

    /// <summary>Completes once the first load attempt has finished (used by the self-test renderer).</summary>
    public Task Ready => _ready.Task;

    public PickerWindow()
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        _view = CollectionViewSource.GetDefaultView(_games);
        _view.Filter = o => o is GameItem g && Matches(g);
        GamesList.ItemsSource = _view;

        (Loc.Turkish ? LangTr : LangEn).IsChecked = true;
        ApplyLanguage();
        Loaded += async (_, _) => await LoadGamesAsync();
        Closed += (_, _) => _steam?.Dispose();
    }

    async Task LoadGamesAsync()
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        _games.Clear();
        ShowMessage("games.loading", retry: false);
        RefreshButton.IsEnabled = false;

        try
        {
            // Reconnect on every refresh so a restarted Steam is picked up.
            _steam?.Dispose();
            _steam = null;
            var steam = _steam = new SteamClient(0);
            var games = await Task.Run(async () =>
                GameList.Owned(steam, await GameList.CandidateIdsAsync(steam.InstallPath, cts.Token)), cts.Token);
            if (cts.IsCancellationRequested)
                return;

            foreach (var game in games)
            {
                var item = new GameItem(game);
                _games.Add(item);
                _ = LoadImageAsync(item);
            }
            UpdateCount();
            if (_games.Count == 0)
                ShowMessage("games.none", retry: true);
            else
                UpdateFilterMessage();
        }
        catch (SteamUnavailableException e)
        {
            ShowMessage("steam." + e.Error, retry: true);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_loading == cts)
                RefreshButton.IsEnabled = true;
            _ready.TrySetResult();
        }
    }

    static async Task LoadImageAsync(GameItem item) =>
        item.Image = await ImageLoader.LoadAsync(item.Info.LocalImage, item.Info.ImageUrl, 464);

    bool Matches(GameItem game)
    {
        string q = SearchBox.Text.Trim();
        return q.Length == 0
            || game.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || game.AppIdText.StartsWith(q, StringComparison.Ordinal);
    }

    void OpenGame(uint appId)
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
            $"--app {appId.ToString(CultureInfo.InvariantCulture)} --lang {Settings.Language}") { UseShellExecute = false });
    }

    void Game_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is GameItem game)
            OpenGame(game.Info.AppId);
    }

    void OpenButton_Click(object sender, RoutedEventArgs e) => OpenTypedId();

    void AppIdBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OpenTypedId();
    }

    void OpenTypedId()
    {
        if (uint.TryParse(AppIdBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0)
            OpenGame(id);
        else
            AppIdBox.Focus();
    }

    async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadGamesAsync();

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view.Refresh();
        UpdateFilterMessage();
    }

    void Lang_Checked(object sender, RoutedEventArgs e)
    {
        string lang = sender == LangTr ? "tr" : "en";
        if (Settings.Language == lang)
            return;
        Settings.Language = lang;
        Settings.Save();
        ApplyLanguage();
    }

    void ApplyLanguage()
    {
        SearchBox.Tag = Loc.T("search.games");
        AppIdBox.Tag = Loc.T("appId");
        OpenButton.Content = Loc.T("open");
        RefreshButton.ToolTip = Loc.T("refresh");
        RetryButton.Content = Loc.T("retry");
        UpdateCount();
        if (_messageKey != null)
            ShowMessage(_messageKey, _canRetry);
    }

    void UpdateCount() =>
        CountText.Text = _games.Count == 0 ? "" : _games.Count == 1 ? Loc.T("games.count.one") : Loc.F("games.count", _games.Count);

    void UpdateFilterMessage()
    {
        if (_games.Count == 0)
            return;
        if (_view.IsEmpty)
            ShowMessage("games.noMatch", retry: false);
        else
            ShowMessage(null, retry: false);
    }

    void ShowMessage(string? key, bool retry)
    {
        _messageKey = key;
        _canRetry = retry;
        MessagePanel.Visibility = key == null ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = key == null ? "" : Loc.T(key);
        RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
    }
}
