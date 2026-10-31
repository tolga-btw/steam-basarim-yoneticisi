using System.Globalization;
using System.Windows.Media;
using SteamAchievementManager.Steam;

namespace SteamAchievementManager;

public sealed class GameItem(GameInfo info) : Observable
{
    ImageSource? _image;

    public GameInfo Info { get; } = info;
    public string Name => Info.Name;
    public string AppIdText => Info.AppId.ToString(CultureInfo.InvariantCulture);

    public ImageSource? Image
    {
        get => _image;
        set => Set(ref _image, value);
    }
}

public sealed class AchievementItem(AchievementDef def, bool achieved, DateTime? unlockedAt) : Observable
{
    bool _achieved = achieved;
    ImageSource? _icon, _iconLocked;

    public AchievementDef Def { get; } = def;
    public string Id => Def.Id;
    public string Name => Def.Name;
    public bool Protected => Def.Protected;
    public bool CanEdit => !Def.Protected;
    public string ProtectedTip => Loc.T("protected");

    public string Description =>
        Def.Description.Length > 0 ? Def.Description : Def.Hidden ? Loc.T("hidden") : "";

    public bool SavedAchieved { get; private set; } = achieved;
    public DateTime? UnlockedAt { get; private set; } = unlockedAt;

    public bool IsAchieved
    {
        get => _achieved;
        set
        {
            if (Protected || !Set(ref _achieved, value))
                return;
            Raise(nameof(IsModified));
            Raise(nameof(Icon));
            Raise(nameof(IconOpacity));
            Raise(nameof(DateText));
        }
    }

    public bool IsModified => _achieved != SavedAchieved;

    public string DateText => _achieved && !IsModified && UnlockedAt is { } t
        ? t.ToString("d MMM yyyy", Loc.Culture)
        : "";

    public ImageSource? IconImage
    {
        set { _icon = value; Raise(nameof(Icon)); Raise(nameof(IconOpacity)); }
    }

    public ImageSource? IconLockedImage
    {
        set { _iconLocked = value; Raise(nameof(Icon)); Raise(nameof(IconOpacity)); }
    }

    public ImageSource? Icon => _achieved ? _icon : _iconLocked ?? _icon;

    // Without a grey icon from the schema, dim the colour one instead.
    public double IconOpacity => !_achieved && _iconLocked == null ? 0.4 : 1;

    public void Update(bool achieved, DateTime? unlockedAt)
    {
        SavedAchieved = achieved;
        UnlockedAt = unlockedAt;
        _achieved = achieved;
        Raise(string.Empty);
    }
}

public sealed class StatItem : Observable
{
    string _text;

    public StatItem(StatDef def, string value)
    {
        Def = def;
        SavedText = value;
        _text = value;
    }

    public StatDef Def { get; }
    public string Id => Def.Id;
    public string Name => Def.Name;
    public bool ShowId => Def.Name != Def.Id;
    public bool ReadOnly => Def.Protected || Def.Kind == StatKind.AvgRate;
    public string SavedText { get; private set; }

    public string Note =>
        Def.Protected ? Loc.T("protectedStat")
        : Def.Kind == StatKind.AvgRate ? Loc.T("readOnly")
        : Def.IncrementOnly ? Loc.T("incrementOnly")
        : "";

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value))
                return;
            Raise(nameof(IsValid));
            Raise(nameof(IsModified));
        }
    }

    public bool IsValid => TryGetValue(out _);

    public bool IsModified => IsValid && Normalize(_text) != Normalize(SavedText);

    /// <summary>Parses the edited text and checks it against the schema's limits.</summary>
    public bool TryGetValue(out double value)
    {
        value = 0;
        string s = Normalize(_text);
        if (Def.Kind == StatKind.Int)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return false;
            value = i;
        }
        else if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !float.IsFinite((float)value))
        {
            return false;
        }
        if (value < Def.Min || value > Def.Max)
            return false;
        if (Def.IncrementOnly && double.TryParse(Normalize(SavedText), NumberStyles.Float, CultureInfo.InvariantCulture, out var saved) && value < saved)
            return false;
        return true;
    }

    public void Update(string value)
    {
        SavedText = value;
        _text = value;
        Raise(string.Empty);
    }

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
    public static string Format(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    static string Normalize(string s) => s.Trim().Replace(',', '.');
}
