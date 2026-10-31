using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SteamAchievementManager.Steam;

public enum SteamError { NotInstalled, LoadFailed, NotRunning, NotLoggedIn }

public sealed class SteamUnavailableException(SteamError error) : Exception(error.ToString())
{
    public SteamError Error { get; } = error;
}

/// <summary>
/// Talks to the running Steam client through steamclient64.dll's versioned interfaces.
/// Steam binds the connection to the app id announced before connecting, so one instance
/// (and one process) serves one game.
/// </summary>
public sealed unsafe class SteamClient : IDisposable
{
    public const int UserStatsReceivedId = 1101;
    public const int UserStatsStoredId = 1102;

    [StructLayout(LayoutKind.Sequential)]
    struct CallbackMsg
    {
        public int User;
        public int Id;
        public IntPtr Param;
        public int ParamSize;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern bool SetDllDirectoryW(string? path);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);

    const uint LoadWithAlteredSearchPath = 0x8;

    readonly IntPtr _client, _user, _stats, _apps, _appsData;
    readonly int _pipe, _hUser;
    readonly delegate* unmanaged<int, CallbackMsg*, int*, byte> _getCallback;
    readonly delegate* unmanaged<int, byte> _freeCallback;
    bool _disposed;

    public string InstallPath { get; }
    public uint AppId { get; }

    /// <summary>Raised from <see cref="RunCallbacks"/>; the payload pointer is only valid during the call.</summary>
    public event Action<int, IntPtr>? Callback;

    public SteamClient(uint appId)
    {
        InstallPath = FindInstallPath() ?? throw new SteamUnavailableException(SteamError.NotInstalled);
        AppId = appId;
        if (appId != 0)
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());

        SetDllDirectoryW(InstallPath);
        IntPtr lib = LoadLibraryExW(Path.Combine(InstallPath, "steamclient64.dll"), IntPtr.Zero, LoadWithAlteredSearchPath);
        if (lib == IntPtr.Zero)
            throw new SteamUnavailableException(SteamError.LoadFailed);

        var create = (delegate* unmanaged<byte*, int*, IntPtr>)(void*)GetProcAddress(lib, "CreateInterface");
        _getCallback = (delegate* unmanaged<int, CallbackMsg*, int*, byte>)(void*)GetProcAddress(lib, "Steam_BGetCallback");
        _freeCallback = (delegate* unmanaged<int, byte>)(void*)GetProcAddress(lib, "Steam_FreeLastCallback");
        if (create == null || _getCallback == null || _freeCallback == null)
            throw new SteamUnavailableException(SteamError.LoadFailed);

        fixed (byte* name = Z("SteamClient018"))
            _client = create(name, null);
        if (_client == IntPtr.Zero)
            throw new SteamUnavailableException(SteamError.LoadFailed);

        _pipe = ((delegate* unmanaged<IntPtr, int>)Fn(_client, 0))(_client);
        if (_pipe == 0)
            throw new SteamUnavailableException(SteamError.NotRunning);

        _hUser = ((delegate* unmanaged<IntPtr, int, int>)Fn(_client, 2))(_client, _pipe);
        if (_hUser == 0)
        {
            Dispose();
            throw new SteamUnavailableException(SteamError.NotLoggedIn);
        }

        _user = UserInterface(5, "SteamUser012");
        _stats = UserInterface(13, "STEAMUSERSTATS_INTERFACE_VERSION013");
        _apps = UserInterface(15, "STEAMAPPS_INTERFACE_VERSION008");
        _appsData = UserInterface(15, "STEAMAPPS_INTERFACE_VERSION001");
        if (_user == IntPtr.Zero || _stats == IntPtr.Zero || _apps == IntPtr.Zero || _appsData == IntPtr.Zero)
        {
            Dispose();
            throw new SteamUnavailableException(SteamError.LoadFailed);
        }
    }

    public static string? FindInstallPath()
    {
        var places = new[]
        {
            (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
        };
        foreach (var (hive, key, value) in places)
        {
            using var k = hive.OpenSubKey(key);
            if (k?.GetValue(value) is string p && File.Exists(Path.Combine(p, "steamclient64.dll")))
                return Path.GetFullPath(p);
        }
        return null;
    }

    // ---- ISteamUser ----

    public ulong SteamId
    {
        get
        {
            // CSteamID is returned through a hidden pointer in the x64 member-function ABI.
            ulong id = 0;
            ((delegate* unmanaged<IntPtr, ulong*, ulong*>)Fn(_user, 2))(_user, &id);
            return id;
        }
    }

    // ---- ISteamApps ----

    public bool IsSubscribedApp(uint appId) =>
        ((delegate* unmanaged<IntPtr, uint, byte>)Fn(_apps, 6))(_apps, appId) != 0;

    public string GetAppData(uint appId, string key)
    {
        byte* buffer = stackalloc byte[1024];
        int length;
        fixed (byte* k = Z(key))
            length = ((delegate* unmanaged<IntPtr, uint, byte*, byte*, int, int>)Fn(_appsData, 0))(_appsData, appId, k, buffer, 1024);
        return length <= 0 ? "" : Marshal.PtrToStringUTF8((IntPtr)buffer) ?? "";
    }

    // ---- ISteamUserStats ----

    /// <summary>Asks Steam for fresh stats; completion arrives as a <see cref="UserStatsReceivedId"/> callback.</summary>
    public void RequestStats() =>
        ((delegate* unmanaged<IntPtr, ulong, ulong>)Fn(_stats, 15))(_stats, SteamId);

    public bool TryGetAchievement(string id, out bool achieved, out DateTime? unlockedAt)
    {
        byte a = 0;
        uint time = 0;
        byte ok;
        fixed (byte* n = Z(id))
            ok = ((delegate* unmanaged<IntPtr, byte*, byte*, uint*, byte>)Fn(_stats, 8))(_stats, n, &a, &time);
        achieved = a != 0;
        unlockedAt = achieved && time > 0 ? DateTimeOffset.FromUnixTimeSeconds(time).LocalDateTime : null;
        return ok != 0;
    }

    public bool SetAchievement(string id, bool achieved)
    {
        fixed (byte* n = Z(id))
            return ((delegate* unmanaged<IntPtr, byte*, byte>)Fn(_stats, achieved ? 6 : 7))(_stats, n) != 0;
    }

    public bool TryGetStat(string id, out int value)
    {
        int v = 0;
        byte ok;
        fixed (byte* n = Z(id))
            ok = ((delegate* unmanaged<IntPtr, byte*, int*, byte>)Fn(_stats, 1))(_stats, n, &v);
        value = v;
        return ok != 0;
    }

    public bool TryGetStat(string id, out float value)
    {
        float v = 0;
        byte ok;
        fixed (byte* n = Z(id))
            ok = ((delegate* unmanaged<IntPtr, byte*, float*, byte>)Fn(_stats, 0))(_stats, n, &v);
        value = v;
        return ok != 0;
    }

    public bool SetStat(string id, int value)
    {
        fixed (byte* n = Z(id))
            return ((delegate* unmanaged<IntPtr, byte*, int, byte>)Fn(_stats, 3))(_stats, n, value) != 0;
    }

    public bool SetStat(string id, float value)
    {
        fixed (byte* n = Z(id))
            return ((delegate* unmanaged<IntPtr, byte*, float, byte>)Fn(_stats, 2))(_stats, n, value) != 0;
    }

    /// <summary>Uploads pending changes; completion arrives as a <see cref="UserStatsStoredId"/> callback.</summary>
    public bool StoreStats() =>
        ((delegate* unmanaged<IntPtr, byte>)Fn(_stats, 9))(_stats) != 0;

    public bool ResetAllStats(bool achievementsToo) =>
        ((delegate* unmanaged<IntPtr, byte, byte>)Fn(_stats, 20))(_stats, achievementsToo ? (byte)1 : (byte)0) != 0;

    // ---- callbacks ----

    public void RunCallbacks()
    {
        if (_disposed)
            return;
        CallbackMsg msg;
        int call;
        while (_getCallback(_pipe, &msg, &call) != 0)
        {
            try { Callback?.Invoke(msg.Id, msg.Param); }
            finally { _freeCallback(_pipe); }
        }
    }

    /// <summary>Reads UserStatsReceived_t / UserStatsStored_t: (app id, EResult; 1 = OK).</summary>
    public static (uint AppId, int Result) ReadStatsResult(IntPtr payload) =>
        ((uint)(Marshal.ReadInt64(payload) & 0xFFFFFF), Marshal.ReadInt32(payload, 8));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_hUser != 0)
            ((delegate* unmanaged<IntPtr, int, int, void>)Fn(_client, 4))(_client, _pipe, _hUser);
        if (_pipe != 0)
            ((delegate* unmanaged<IntPtr, int, byte>)Fn(_client, 1))(_client, _pipe);
    }

    IntPtr UserInterface(int index, string version)
    {
        fixed (byte* v = Z(version))
            return ((delegate* unmanaged<IntPtr, int, int, byte*, IntPtr>)Fn(_client, index))(_client, _hUser, _pipe, v);
    }

    static void* Fn(IntPtr obj, int index) => (*(void***)obj)[index];

    static byte[] Z(string s) => Encoding.UTF8.GetBytes(s + "\0");
}
