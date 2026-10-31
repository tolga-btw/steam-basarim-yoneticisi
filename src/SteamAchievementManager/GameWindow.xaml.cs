using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager;

public partial class GameWindow : Window
{
    static readonly TimeSpan SteamTimeout = TimeSpan.FromSeconds(10);

    readonly uint _appId;
    readonly ObservableCollection<AchievementItem> _achievements = [];
    readonly ObservableCollection<StatItem> _stats = [];
    readonly ICollectionView _achievementView, _statView;
    readonly DispatcherTimer _pump = new() { Interval = TimeSpan.FromMilliseconds(100) };
    readonly TaskCompletionSource _ready = new();
    readonly bool _initialized;
    SteamClient? _steam;
    TaskCompletionSource<int>? _statsReceived, _statsStored;
    bool _busy, _closeConfirmed;
    string? _blockingMessage = "loading";
    string? _statusMessage;

    /// <summary>Completes once the first load attempt has finished (used by the self-test renderer).</summary>
    public Task Ready => _ready.Task;

    public GameWindow(uint appId)
    {
        _appId = appId;
        InitializeComponent();
        WindowTheme.Apply(this);

        _achievementView = CollectionViewSource.GetDefaultView(_achievements);
        _achievementView.Filter = o => o is AchievementItem a && Matches(a);
        _statView = CollectionViewSource.GetDefaultView(_stats);
        _statView.Filter = o => o is StatItem s && Matches(s);
        AchievementList.ItemsSource = _achievementView;
        StatList.ItemsSource = _statView;

        _pump.Tick += (_, _) => _steam?.RunCallbacks();
        GameName.Text = $"App {appId}";
        _initialized = true;
        ApplyLanguage();
        SetBusy(true);

        Loaded += async (_, _) => await StartAsync();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _pump.Stop();
            _steam?.Dispose();
        };
    }

    async Task StartAsync()
    {
        try
        {
            _steam = new SteamClient(_appId);
        }
        catch (SteamUnavailableException e)
        {
            ShowBlocking("steam." + e.Error);
            _ready.TrySetResult();
            return;
        }
        _steam.Callback += OnSteamCallback;
        _pump.Start();

        string name = _steam.GetAppData(_appId, "name");
        if (name.Length > 0)
        {
            GameName.Text = name;
            Title = $"{name} · Steam Achievement Manager";
        }
        var info = GameList.Describe(_steam.InstallPath, _appId, name);
        HeaderImage.ImageSource = await ImageLoader.LoadAsync(info.LocalImage, info.ImageUrl, 400);

        await ReloadAsync();
        _ready.TrySetResult();
    }

    async Task ReloadAsync()
    {
        if (_steam == null)
            return;
        SetBusy(true);
        int result = await WaitAsync(_statsReceived = NewSignal(), _steam.RequestStats);

        if (!Schema.TryLoad(_steam.InstallPath, _appId, Loc.SteamLanguage, out var achievementDefs, out var statDefs))
        {
            ShowBlocking(result == 1 ? "schemaMissing" : "loadFailed");
            return;
        }

        var knownAchievements = _achievements.ToDictionary(a => a.Id);
        var achievements = new List<AchievementItem>();
        foreach (var def in achievementDefs)
        {
            if (!_steam.TryGetAchievement(def.Id, out bool achieved, out var unlockedAt))
                continue;
            if (knownAchievements.TryGetValue(def.Id, out var item))
            {
                item.Update(achieved, unlockedAt);
            }
            else
            {
                item = new AchievementItem(def, achieved, unlockedAt);
                item.PropertyChanged += Item_PropertyChanged;
                _ = LoadIconsAsync(item);
            }
            achievements.Add(item);
        }

        if (achievements.Count == 0 && achievementDefs.Count > 0)
        {
            ShowBlocking("loadFailed");
            return;
        }

        var knownStats = _stats.ToDictionary(s => s.Id);
        var stats = new List<StatItem>();
        foreach (var def in statDefs)
        {
            string? value = def.Kind == StatKind.Int
                ? _steam.TryGetStat(def.Id, out int i) ? StatItem.Format(i) : null
                : _steam.TryGetStat(def.Id, out float f) ? StatItem.Format(f) : null;
            if (value == null)
                continue;
            if (knownStats.TryGetValue(def.Id, out var item))
            {
                item.Update(value);
            }
            else
            {
                item = new StatItem(def, value);
                item.PropertyChanged += Item_PropertyChanged;
            }
            stats.Add(item);
        }

        Replace(_achievements, achievements);
        Replace(_stats, stats);
        _blockingMessage = null;
        SetBusy(false);
        UpdateListMessage();
    }

    async Task LoadIconsAsync(AchievementItem item)
    {
        var color = item.Def.Icon is { } icon
            ? ImageLoader.LoadAsync(null, GameList.AchievementIconUrl(_appId, icon), 96)
            : Task.FromResult<System.Windows.Media.Imaging.BitmapImage?>(null);
        var grey = item.Def.IconLocked is { } iconLocked
            ? ImageLoader.LoadAsync(null, GameList.AchievementIconUrl(_appId, iconLocked), 96)
            : Task.FromResult<System.Windows.Media.Imaging.BitmapImage?>(null);
        item.IconImage = await color;
        item.IconLockedImage = await grey;
    }

    // ---- Steam callbacks ----

    static TaskCompletionSource<int> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Starts a Steam request and waits for its callback; returns the EResult, or 0 on timeout.</summary>
    static async Task<int> WaitAsync(TaskCompletionSource<int> signal, Action start)
    {
        start();
        var done = await Task.WhenAny(signal.Task, Task.Delay(SteamTimeout));
        return done == signal.Task ? signal.Task.Result : 0;
    }

    void OnSteamCallback(int id, IntPtr payload)
    {
        if (id is not (SteamClient.UserStatsReceivedId or SteamClient.UserStatsStoredId))
            return;
        var (appId, result) = SteamClient.ReadStatsResult(payload);
        if (appId != _appId)
            return;
        (id == SteamClient.UserStatsReceivedId ? _statsReceived : _statsStored)?.TrySetResult(result);
    }

    // ---- Actions ----

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_steam == null || _busy)
            return;
        SetBusy(true);
        StatusText.Text = Loc.T("saving");

        var achievements = _achievements.Where(a => a.IsModified).Select(a => (a.Id, a.IsAchieved)).ToList();
        var stats = _stats.Where(s => s.IsModified && s.TryGetValue(out _))
            .Select(s => { s.TryGetValue(out var v); return (Item: s, Value: v); }).ToList();

        foreach (var (id, achieved) in achievements)
            _steam.SetAchievement(id, achieved);
        foreach (var (item, value) in stats)
        {
            if (item.Def.Kind == StatKind.Int)
                _steam.SetStat(item.Id, (int)value);
            else
                _steam.SetStat(item.Id, (float)value);
        }

        var steam = _steam;
        await WaitAsync(_statsStored = NewSignal(), () => steam.StoreStats());
        await ReloadAsync();

        // Judge success by what a fresh fetch from Steam reports, not by the call results.
        bool applied = achievements.All(c => _achievements.FirstOrDefault(a => a.Id == c.Id)?.SavedAchieved == c.IsAchieved)
            && stats.All(c => c.Item.TryGetValue(out var now) && Math.Abs(now - c.Value) <= 1e-4 * Math.Max(1, Math.Abs(c.Value)));
        SetStatus(applied ? "saved" : "saveFailed");
    }

    async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (_steam == null || _busy)
            return;
        int choice = ConfirmDialog.Show(this, Loc.T("confirm.reset.title"), Loc.T("confirm.reset.text"), Loc.T("cancel"),
            (Loc.T("confirm.reset.stats"), true), (Loc.T("confirm.reset.all"), true));
        if (choice == 0)
            return;

        SetBusy(true);
        var steam = _steam;
        bool ok = false;
        // ResetAllStats stores on its own; its completion arrives as a stored callback.
        int result = await WaitAsync(_statsStored = NewSignal(), () => ok = steam.ResetAllStats(choice == 2));
        await ReloadAsync();
        SetStatus(ok && result == 1 ? "resetDone" : "resetFailed");
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        SetStatus(null);
        await ReloadAsync();
    }

    void UnlockAll_Click(object sender, RoutedEventArgs e) => ForEachVisible(a => a.IsAchieved = true);
    void LockAll_Click(object sender, RoutedEventArgs e) => ForEachVisible(a => a.IsAchieved = false);
    void Invert_Click(object sender, RoutedEventArgs e) => ForEachVisible(a => a.IsAchieved = !a.IsAchieved);

    /// <summary>Bulk actions apply to what the current filter and search show.</summary>
    void ForEachVisible(Action<AchievementItem> action)
    {
        foreach (var item in _achievementView.Cast<AchievementItem>().Where(a => a.CanEdit).ToList())
            action(item);
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !HasChanges)
            return;
        int choice = ConfirmDialog.Show(this, Loc.T("confirm.close.title"), Loc.T("confirm.close.text"), Loc.T("cancel"),
            (Loc.T("confirm.close.ok"), true));
        if (choice == 0)
            e.Cancel = true;
        else
            _closeConfirmed = true;
    }

    // ---- Filtering ----

    bool Matches(AchievementItem a)
    {
        if (FilterUnlocked.IsChecked == true && !a.IsAchieved || FilterLocked.IsChecked == true && a.IsAchieved)
            return false;
        string q = SearchBox.Text.Trim();
        return q.Length == 0
            || a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.Description.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || a.Id.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    bool Matches(StatItem s)
    {
        string q = SearchBox.Text.Trim();
        return q.Length == 0
            || s.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
            || s.Id.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;
        bool achievements = TabAchievements.IsChecked == true;
        AchievementList.Visibility = achievements ? Visibility.Visible : Visibility.Collapsed;
        StatList.Visibility = achievements ? Visibility.Collapsed : Visibility.Visible;
        FilterPanel.Visibility = achievements ? Visibility.Visible : Visibility.Collapsed;
        BulkPanel.Visibility = achievements ? Visibility.Visible : Visibility.Collapsed;
        SearchBox.Tag = Loc.T(achievements ? "search.achievements" : "search.stats");
        UpdateListMessage();
    }

    void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (!_initialized)
            return;
        _achievementView.Refresh();
        UpdateListMessage();
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _achievementView.Refresh();
        _statView.Refresh();
        UpdateListMessage();
    }

    // ---- State ----

    bool HasChanges => _achievements.Any(a => a.IsModified) || _stats.Any(s => s.IsModified);

    void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AchievementItem.IsAchieved) or nameof(StatItem.Text))
        {
            _statusMessage = null;
            UpdateState();
        }
    }

    void SetBusy(bool busy)
    {
        _busy = busy;
        bool usable = !busy && _steam != null && _blockingMessage == null;
        RefreshButton.IsEnabled = !busy && _steam != null;
        ResetButton.IsEnabled = usable;
        UnlockAllButton.IsEnabled = LockAllButton.IsEnabled = InvertButton.IsEnabled = usable;
        AchievementList.IsHitTestVisible = StatList.IsHitTestVisible = !busy;
        UpdateState();
    }

    void SetStatus(string? key)
    {
        _statusMessage = key;
        UpdateState();
    }

    void UpdateState()
    {
        int total = _achievements.Count, unlocked = _achievements.Count(a => a.IsAchieved);
        ProgressText.Text = total == 0 ? "" : Loc.F("progress", unlocked, total);
        ProgressScale.ScaleX = total == 0 ? 0 : (double)unlocked / total;

        int changes = _achievements.Count(a => a.IsModified) + _stats.Count(s => s.IsModified);
        SaveButton.IsEnabled = !_busy && _steam != null && changes > 0 && _stats.All(s => s.IsValid);
        if (_busy)
            return;
        StatusText.Text = changes switch
        {
            0 => _statusMessage == null ? "" : Loc.T(_statusMessage),
            1 => Loc.T("unsaved.one"),
            _ => Loc.F("unsaved", changes),
        };
    }

    void ShowBlocking(string key)
    {
        _blockingMessage = key;
        _achievements.Clear();
        _stats.Clear();
        SetBusy(false);
        UpdateListMessage();
    }

    void UpdateListMessage()
    {
        string? key = _blockingMessage;
        if (key == null)
        {
            key = TabAchievements.IsChecked == true
                ? _achievements.Count == 0 ? "noAchievements" : _achievementView.IsEmpty ? "noMatch" : null
                : _stats.Count == 0 ? "noStats" : _statView.IsEmpty ? "noMatch" : null;
        }
        MessageText.Text = key == null ? "" : Loc.T(key);
    }

    void ApplyLanguage()
    {
        TabAchievements.Content = Loc.T("tab.achievements");
        TabStats.Content = Loc.T("tab.stats");
        FilterAll.Content = Loc.T("filter.all");
        FilterUnlocked.Content = Loc.T("filter.unlocked");
        FilterLocked.Content = Loc.T("filter.locked");
        UnlockAllButton.Content = Loc.T("unlockAll");
        LockAllButton.Content = Loc.T("lockAll");
        InvertButton.Content = Loc.T("invert");
        ResetButton.Content = Loc.T("resetStats");
        RefreshButton.Content = Loc.T("refresh");
        SaveButton.Content = Loc.T("save");
        SearchBox.Tag = Loc.T(TabAchievements.IsChecked == true ? "search.achievements" : "search.stats");
        UpdateListMessage();
    }

    static void Replace<T>(ObservableCollection<T> target, List<T> items)
    {
        if (target.SequenceEqual(items))
            return;
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
