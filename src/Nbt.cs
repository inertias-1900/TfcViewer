using System.IO.Compression;
using System.Text;

namespace TfcViewer.Storage;

/// <summary>极简 NBT（Named Binary Tag）读取器：只读、只支持标准 tag 类型，够读 level.dat / tfc_calendar.dat。</summary>
public static class Nbt
{
    /// <summary>读取 .dat 文件（自动识别 gzip 与未压缩两种），返回顶层 Compound</summary>
    public static Dictionary<string, object> ReadFile(string path)
    {
        byte[] raw = File.ReadAllBytes(path);
        using Stream source = raw.Length > 2 && raw[0] == 0x1F && raw[1] == 0x8B
            ? new GZipStream(new MemoryStream(raw), CompressionMode.Decompress)
            : new MemoryStream(raw);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        using var reader = new BinaryReader(buffer);
        var (type, _, value) = ReadTag(reader);
        if (type != TagCompound || value is not Dictionary<string, object> root)
            throw new InvalidDataException("NBT 根标签不是 Compound: " + path);
        return root;
    }

    private const int TagEnd = 0, TagByte = 1, TagShort = 2, TagInt = 3, TagLong = 4, TagFloat = 5,
        TagDouble = 6, TagByteArray = 7, TagString = 8, TagList = 9, TagCompound = 10, TagIntArray = 11, TagLongArray = 12;

    private static (int Type, string Name, object? Value) ReadTag(BinaryReader reader)
    {
        int type = reader.ReadByte();
        if (type == TagEnd) return (TagEnd, string.Empty, null);
        string name = ReadString(reader);
        object? value = type switch
        {
            TagByte => reader.ReadByte(),
            TagShort => ReadShort(reader),
            TagInt => ReadInt(reader),
            TagLong => ReadLong(reader),
            TagFloat => ReadFloat(reader),
            TagDouble => ReadDouble(reader),
            TagByteArray => ReadBytes(reader),
            TagString => ReadString(reader),
            TagList => ReadList(reader),
            TagCompound => ReadCompound(reader),
            TagIntArray => ReadIntArray(reader),
            TagLongArray => ReadLongArray(reader),
            _ => throw new InvalidDataException("未知 NBT tag: " + type),
        };
        return (type, name, value);
    }

    private static Dictionary<string, object> ReadCompound(BinaryReader reader)
    {
        var map = new Dictionary<string, object>();
        while (true)
        {
            var (type, name, value) = ReadTag(reader);
            if (type == TagEnd) break;
            map[name] = value!;
        }
        return map;
    }

    private static List<object> ReadList(BinaryReader reader)
    {
        int elementType = reader.ReadByte();
        int count = ReadInt(reader);
        var list = new List<object>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            object? value = elementType switch
            {
                TagByte => reader.ReadByte(),
                TagShort => ReadShort(reader),
                TagInt => ReadInt(reader),
                TagLong => ReadLong(reader),
                TagFloat => ReadFloat(reader),
                TagDouble => ReadDouble(reader),
                TagByteArray => ReadBytes(reader),
                TagString => ReadString(reader),
                TagList => ReadList(reader),
                TagCompound => ReadCompound(reader),
                TagIntArray => ReadIntArray(reader),
                TagLongArray => ReadLongArray(reader),
                _ => throw new InvalidDataException("未知 NBT list 元素类型: " + elementType),
            };
            list.Add(value!);
        }
        return list;
    }

    private static short ReadShort(BinaryReader r) => (short)((r.ReadByte() << 8) | r.ReadByte());

    private static int ReadInt(BinaryReader r) => (r.ReadByte() << 24) | (r.ReadByte() << 16) | (r.ReadByte() << 8) | r.ReadByte();

    private static long ReadLong(BinaryReader r) => ((long)ReadInt(r) << 32) | (uint)ReadInt(r);

    private static float ReadFloat(BinaryReader r)
    {
        int bits = ReadInt(r);
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static double ReadDouble(BinaryReader r)
    {
        long bits = ReadLong(r);
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static byte[] ReadBytes(BinaryReader r)
    {
        int len = ReadInt(r);
        return r.ReadBytes(Math.Max(0, len));
    }

    private static int[] ReadIntArray(BinaryReader r)
    {
        int len = ReadInt(r);
        var arr = new int[Math.Max(0, len)];
        for (int i = 0; i < arr.Length; i++) arr[i] = ReadInt(r);
        return arr;
    }

    private static long[] ReadLongArray(BinaryReader r)
    {
        int len = ReadInt(r);
        var arr = new long[Math.Max(0, len)];
        for (int i = 0; i < arr.Length; i++) arr[i] = ReadLong(r);
        return arr;
    }

    private static string ReadString(BinaryReader r)
    {
        int len = (r.ReadByte() << 8) | r.ReadByte();
        return Encoding.UTF8.GetString(r.ReadBytes(len));
    }

    // ---- 取值辅助 ----

    public static Dictionary<string, object>? Compound(Dictionary<string, object> map, params string[] path)
    {
        object? cur = map;
        foreach (string key in path)
        {
            if (cur is not Dictionary<string, object> compound || !compound.TryGetValue(key, out var next)) return null;
            cur = next;
        }
        return cur as Dictionary<string, object>;
    }

    public static long? Long(Dictionary<string, object> map, params string[] path)
        => Find(map, path) is long v ? v : null;

    public static int? Int(Dictionary<string, object> map, params string[] path)
        => Find(map, path) switch
        {
            int v => v,
            byte b => b,
            long v => (int)v,
            short s => s,
            _ => null,
        };

    public static string? String(Dictionary<string, object> map, params string[] path)
        => Find(map, path) as string;

    private static object? Find(Dictionary<string, object> map, string[] path)
    {
        object? cur = map;
        foreach (string key in path)
        {
            if (cur is not Dictionary<string, object> compound || !compound.TryGetValue(key, out var next)) return null;
            cur = next;
        }
        return cur;
    }
}

/// <summary>从存档里读出的 TFC 日历状态</summary>
public sealed record TfcSaveState(
    string SavePath,
    long Seed,
    long CalendarTicks,
    int DaysInMonth,
    long PlayerTime,
    bool DoDaylightCycle,
    bool ArePlayersLoggedOn)
{
    public bool HasCalendar => CalendarTicks > 0 || DaysInMonth > 0;
}

/// <summary>
/// 直接读存档（不启动游戏）：世界种子取自 level.dat，日历状态取自 data/tfc_calendar.dat。
/// 这两个文件是 TFC 运行时气候的全部外部输入。
/// </summary>
public static class TfcSave
{
    public const string CalendarFileName = "tfc_calendar.dat";
    public const int DefaultDaysInMonth = 8;

    /// <summary>接受「存档目录」或「level.dat 路径」</summary>
    public static TfcSaveState Read(string path)
    {
        string dir = ResolveSaveDirectory(path);
        string levelDat = Path.Combine(dir, "level.dat");
        if (!File.Exists(levelDat)) throw new FileNotFoundException("找不到 level.dat: " + levelDat);

        var level = Nbt.ReadFile(levelDat);
        long seed = Nbt.Long(level, "Data", "WorldGenSettings", "seed")
                    ?? Nbt.Long(level, "Data", "seed")
                    ?? throw new InvalidDataException("level.dat 里找不到世界种子（Data.WorldGenSettings.seed）");

        long calendarTicks = 0;
        int daysInMonth = DefaultDaysInMonth;
        long playerTime = 0;
        bool doDaylightCycle = true;
        bool arePlayersLoggedOn = false;

        string calendarPath = Path.Combine(dir, "data", CalendarFileName);
        bool hasCalendar = File.Exists(calendarPath);
        if (hasCalendar)
        {
            var root = Nbt.ReadFile(calendarPath);
            var calendar = Nbt.Compound(root, "data", "calendar");
            if (calendar != null)
            {
                calendarTicks = Nbt.Long(calendar, "calendarTime") ?? 0;
                daysInMonth = Nbt.Int(calendar, "daysInMonth") ?? DefaultDaysInMonth;
                playerTime = Nbt.Long(calendar, "playerTime") ?? 0;
                doDaylightCycle = (Nbt.Int(calendar, "doDaylightCycle") ?? 1) != 0;
                arePlayersLoggedOn = (Nbt.Int(calendar, "arePlayersLoggedOn") ?? 0) != 0;
            }
        }

        return new TfcSaveState(dir, seed, calendarTicks, daysInMonth, playerTime, doDaylightCycle, arePlayersLoggedOn);
    }

    /// <summary>接受存档目录、level.dat 文件、或 saves 父目录（自动取其中唯一的存档）</summary>
    public static string ResolveSaveDirectory(string path)
    {
        if (File.Exists(path)) return Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException("找不到存档目录: " + path);
        if (File.Exists(Path.Combine(path, "level.dat"))) return Path.GetFullPath(path);

        var candidates = Directory.GetDirectories(path)
            .Where(d => File.Exists(Path.Combine(d, "level.dat")))
            .ToArray();
        if (candidates.Length == 1) return Path.GetFullPath(candidates[0]);
        if (candidates.Length > 1)
            throw new InvalidOperationException("该目录下有多个存档，请指定其一: " + string.Join(", ", candidates.Select(Path.GetFileName)));
        throw new DirectoryNotFoundException("目录里没有 level.dat: " + path);
    }
}
