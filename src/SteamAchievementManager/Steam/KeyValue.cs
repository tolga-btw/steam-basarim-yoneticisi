using System.Globalization;
using System.Text;

namespace SteamAchievementManager.Steam;

/// <summary>Node of Valve's binary KeyValues format (used by the stats schema files).</summary>
public sealed class KeyValue
{
    public static readonly KeyValue Invalid = new();

    public string Name { get; private set; } = "";
    public object? Value { get; private set; }
    public List<KeyValue> Children { get; } = [];

    public bool Exists => !ReferenceEquals(this, Invalid);

    public KeyValue this[string key] =>
        Children.FirstOrDefault(c => string.Equals(c.Name, key, StringComparison.OrdinalIgnoreCase)) ?? Invalid;

    public string AsString(string fallback = "") => Value switch
    {
        null => fallback,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Value.ToString() ?? fallback,
    };

    public double AsDouble(double fallback = 0) => Value switch
    {
        int i => i,
        long l => l,
        ulong u => u,
        float f => f,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => fallback,
    };

    public int AsInt(int fallback = 0) => Value is string s
        ? int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : fallback
        : (int)AsDouble(fallback);

    public bool AsBool() => AsInt() != 0;

    public static KeyValue? LoadBinary(string path)
    {
        try
        {
            using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
            var root = new KeyValue();
            ReadChildren(reader, root);
            return root.Children.FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    static void ReadChildren(BinaryReader r, KeyValue parent)
    {
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            byte type = r.ReadByte();
            if (type is 8 or 11)
                return;

            var kv = new KeyValue { Name = ReadString(r) };
            switch (type)
            {
                case 0: ReadChildren(r, kv); break;
                case 1: kv.Value = ReadString(r); break;
                case 2: kv.Value = r.ReadInt32(); break;
                case 3: kv.Value = r.ReadSingle(); break;
                case 4: kv.Value = r.ReadInt32(); break;
                case 5: kv.Value = ReadWideString(r); break;
                case 6: kv.Value = r.ReadInt32(); break;
                case 7: kv.Value = r.ReadUInt64(); break;
                case 10: kv.Value = r.ReadInt64(); break;
                default: throw new InvalidDataException($"Unknown KeyValue type {type}");
            }
            parent.Children.Add(kv);
        }
    }

    static string ReadString(BinaryReader r)
    {
        var bytes = new List<byte>();
        for (byte b; (b = r.ReadByte()) != 0;)
            bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    static string ReadWideString(BinaryReader r)
    {
        var sb = new StringBuilder();
        for (char c; (c = (char)r.ReadUInt16()) != 0;)
            sb.Append(c);
        return sb.ToString();
    }
}
