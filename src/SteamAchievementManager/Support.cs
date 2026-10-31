using System.ComponentModel;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace SteamAchievementManager;

public static class AppPaths
{
    public static string Data { get; } = Ensure(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    public static string Cache { get; } = Ensure(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cache");

    static string Ensure(string root, string? sub = null)
    {
        string path = Path.Combine(root, "SteamAchievementManager", sub ?? "");
        Directory.CreateDirectory(path);
        return path;
    }
}

public static class Http
{
    public static HttpClient Client { get; } = Create();

    static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamAchievementManager/1.0");
        return client;
    }
}

public static class Settings
{
    public static string Language { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    static string FilePath => Path.Combine(AppPaths.Data, "settings.json");

    public static void Load()
    {
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath));
            if (data != null && data.TryGetValue("language", out var lang) && lang is "tr" or "en")
                Language = lang;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Dictionary<string, string> { ["language"] = Language }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

public static class ImageLoader
{
    static readonly SemaphoreSlim Downloads = new(6);

    /// <summary>Loads a local file, or downloads <paramref name="url"/> into the cache first. Returns null on any failure.</summary>
    public static async Task<BitmapImage?> LoadAsync(string? localPath, string? url, int decodeWidth)
    {
        try
        {
            string? path = localPath != null && File.Exists(localPath) ? localPath : null;
            if (path == null)
            {
                if (url == null)
                    return null;
                string dir = Path.Combine(AppPaths.Cache, "img");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, Hash(url) + Path.GetExtension(new Uri(url).AbsolutePath));
                if (!File.Exists(path))
                {
                    await Downloads.WaitAsync();
                    try
                    {
                        byte[] bytes = await Http.Client.GetByteArrayAsync(url);
                        await File.WriteAllBytesAsync(path, bytes);
                    }
                    finally { Downloads.Release(); }
                }
            }

            return await Task.Run(() =>
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = decodeWidth;
                image.UriSource = new Uri(path);
                image.EndInit();
                image.Freeze();
                return image;
            });
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..16];
}

public static class WindowTheme
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    const int UseImmersiveDarkMode = 20, CaptionColor = 35, TextColor = 36;

    /// <summary>Gives the native title bar the app's dark colors.</summary>
    public static void Apply(Window window) => window.SourceInitialized += (_, _) =>
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        int on = 1, caption = 0x00161114, text = 0x00F2EEF4; // COLORREF is 0x00BBGGRR
        DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, CaptionColor, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, TextColor, ref text, sizeof(int));
    };
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Raise(name);
        return true;
    }
}
