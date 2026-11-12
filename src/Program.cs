using System.Reflection;
using TfcViewer;
using TfcViewer.Climate;
using TfcViewer.Core;
using TfcViewer.Storage;
using TfcViewer.World;
using TfcViewer.World.Region;

// TFC 离线群系/气候查看器 CLI
// 用法: tfcviewer <种子> [大小] [块每像素] [图层] [输出.png]

if (args.Length == 1 && args[0] == "--lt3")
{
    LayerTest3Main.Run();
    return;
}

if (args.Length == 1 && args[0] == "--div")
{
    DivTestMain.Run();
    return;
}

if (args.Length == 1 && args[0] == "--debug")
{
    DebugMain.Run();
    return;
}

if (args.Length >= 1 && args[0] == "--selftest")
{
    // 自洽验证：ClimateProfile（按小时预计算的季节包络）与 ClimateModel.GetTemperature 逐时刻暴力扫描必须一致
    long seedT = args.Length > 1 ? ParseSeed(args[1]) : 12345L;
    int dims = args.Length > 2 ? int.Parse(args[2]) : 8;
    var m = new ClimateModel(seedT, Defaults.DefaultSettings().TemperatureScale);
    var pf = new ClimateProfile(m, dims);
    var points = new (int z, int y)[] { (0, 64), (40, 64), (-1234, 64), (999, 70), (500, 120), (0, 20), (-300, -20) };
    long yearTicks = (long)dims * TfcCalendar.MonthsInYear * TfcCalendar.TicksInDay;
    int bad = 0;
    foreach (var (z, pty) in points)
    {
        var fast = pf.Range(z, pty, 0f);
        float lo = float.MaxValue, hi = float.MinValue, sum = 0f;
        for (long t = 0; t < yearTicks; t += TfcCalendar.TicksInHour)
        {
            float v = m.GetTemperature(0f, pty, z, t, dims);
            if (v < lo) lo = v;
            if (v > hi) hi = v;
            sum += v;
        }
        float mean = sum / (yearTicks / TfcCalendar.TicksInHour);
        float dMin = Math.Abs(fast.Min - lo), dMax = Math.Abs(fast.Max - hi), dMean = Math.Abs(fast.Mean - mean);
        bool ok = dMin < 0.01f && dMax < 0.01f && dMean < 0.01f;
        if (!ok) bad++;
        Console.WriteLine($"z={z,6} y={pty,4}  包络=[{fast.Min,7:F2},{fast.Max,7:F2}] 均值={fast.Mean,7:F2}"
            + $"  暴力=[{lo,7:F2},{hi,7:F2}] 均值={mean,7:F2}  d=({dMin:F4},{dMax:F4},{dMean:F4}) {(ok ? "OK" : "FAIL")}");
    }
    Console.WriteLine(bad == 0 ? $"自洽验证通过（{points.Length} 个点）" : $"自洽验证失败：{bad} 个点不一致");
    return;
}

if (args.Length >= 4 && args[0] == "--info")
{
    int ibx = int.Parse(args[2]);
    int ibz = int.Parse(args[3]);

    // --save=<存档目录>= 直接用存档里的种子与日历；否则用命令行种子 + --ticks / --day / --hour
    TfcSaveState? save = null;
    string? saveOpt = OptValue(args, "save");
    if (saveOpt != null)
    {
        try { save = TfcSave.Read(saveOpt); }
        catch (Exception ex) { Console.Error.WriteLine("读存档失败: " + ex.Message); return; }
    }

    long iseed = save?.Seed ?? ParseSeed(args[1]);
    int daysInMonth = OptInt(args, "days", save?.DaysInMonth ?? TfcSave.DefaultDaysInMonth);
    int iy = OptInt(args, "y", ClimateModel.SeaLevelY);

    var irg = new RegionGenerator(Defaults.DefaultSettings(), Seed.Of(iseed));
    var info = ViewerQuery.GetInfoAt(irg, ibx, ibz);

    var climate = new ClimateModel(iseed, irg.Settings.TemperatureScale);
    var profile = new ClimateProfile(climate, daysInMonth);
    float avgTemp = ClimateQuery.AverageTempAt(irg, ibx, ibz);
    float avgRain = ClimateQuery.RainfallAt(irg, ibx, ibz);
    var range = profile.Range(ibz, iy, avgTemp);

    Console.WriteLine($"block=({info.BlockX},{info.BlockZ}) chunk=({info.ChunkX},{info.ChunkZ}) y={iy}（默认海平面 {ClimateModel.SeaLevelY}）");
    Console.WriteLine($"seed={iseed} temperatureScale={climate.TemperatureScale} climateSeed={climate.ClimateSeed} daysInMonth={daysInMonth}");
    Console.WriteLine($"biome={info.BiomeId}");
    Console.WriteLine($"temp={info.Temperature:F2}（格点，旧口径）  avgTemp={avgTemp:F2}（128格网格插值，游戏实际用值）");
    Console.WriteLine($"rain={info.Rainfall:F2}（格点）  avgRain={avgRain:F2}（插值）");
    Console.WriteLine($"预测群系类型={ViewerQuery.ClimateLocalized(info.Climate, false)}（世界生成阈值线）");
    Console.WriteLine($"气候分类(Köppen)={KoppenClimate.Name(KoppenClimate.Classify(avgTemp, avgRain), false)} 置信度={info.ConfidencePercent}%");
    Console.WriteLine($"年温度范围 = {range.Min:F1} ~ {range.Max:F1} ℃（年均 {range.Mean:F1}，年温差 {range.Span:F1}）");
    Console.WriteLine($"阈值: 雪 < {ClimateModel.SnowFreezeTemperature}℃ 落雪 / > {ClimateModel.SnowMeltTemperature}℃ 融雪；冰 < {ClimateModel.IceFreezeTemperature}℃ 冻结");

    // 可选：给了存档或 --ticks 时，额外给出「那一时刻」的实际温度（UI 不暴露，仅命令行需要时用）
    if (save != null || OptValue(args, "ticks") != null)
    {
        long calendarTicks = save != null ? save.CalendarTicks : ResolveCalendarTicks(args, daysInMonth);
        float actual = climate.GetTemperature(avgTemp, iy, ibz, calendarTicks, daysInMonth);
        Console.WriteLine($"指定时刻 {TfcCalendar.Describe(calendarTicks, daysInMonth, false)}（ticks={calendarTicks}）实际温度 = {actual:F2} ℃");
    }

    return;
}

if (args.Length >= 1 && args[0] == "--dump")
{
    DumpCompare.Run(args);
    return;
}

if (args.Length < 1)
{
    Console.WriteLine("用法: tfcviewer <种子> [大小] [块每像素] [图层] [输出.png]");
    Console.WriteLine("  种子: 数字，或字符串（按 MC 规则取 hashCode，如 testseed2）");
    Console.WriteLine("  大小: 512 = 512x512 chunk（默认，中心 0,0）");
    Console.WriteLine("  块每像素: 16 = 1px/chunk（默认），8 = 2px/chunk，4 = 4px/chunk 高清");
    Console.WriteLine("  图层: biome(默认) | temperature(=年均温) | temp_min(年最低) | temp_max(年最高) | rainfall");
    Console.WriteLine("  可选: --y=<海拔，默认 64>  --days=<每月天数，默认 8>");
    return;
}

string seedText = args[0];
int size = args.Length > 1 ? int.Parse(args[1]) : 512;
int bpp = args.Length > 2 ? int.Parse(args[2]) : 16;
string layer = args.Length > 3 ? args[3] : "biome";
string prefix = layer switch { "temp_min" => "tmin_", "temp_max" => "tmax_", "temperature" => "temp_", "rainfall" => "rain_", _ => "biome_" };
string outPath = args.Length > 4 ? args[4] : prefix + seedText + ".png";

long seed = ParseSeed(seedText);
if (!IsNumeric(seedText))
{
    Console.WriteLine($"字符串种子 -> hashCode = {seed}");
}

Console.WriteLine("初始化引擎 ...");
long t0 = Environment.TickCount64;
var settings = Defaults.DefaultSettings();
var s = Seed.Of(seed);
var rg = new RegionGenerator(settings, s);
long t1 = Environment.TickCount64;
Console.WriteLine($"初始化完成: {t1 - t0} ms");

var colors = LoadBiomeColors();

// 季节包络：与坐标无关的部分预先算好（年最低/年最高/年均）。图层按行复用，每像素只做一次乘加。
var seasonProfile = new ClimateProfile(new ClimateModel(seed, settings.TemperatureScale), TfcSave.DefaultDaysInMonth);
int layerY = OptInt(args, "y", ClimateModel.SeaLevelY);

int blocks = size * 16;
int minX = -blocks / 2;
int minZ = -blocks / 2;

Console.WriteLine($"采样 {size}x{size} 像素（1px={bpp}块, 图层={layer}, 海拔 y={layerY}）...");
int[] image = new int[size * size];
int rowZ = int.MinValue;
TemperatureRange rowRange = default;
for (int iz = 0; iz < size; iz++)
{
    for (int ix = 0; ix < size; ix++)
    {
        int bx = minX + ix * bpp;
        int bz = minZ + iz * bpp;
        int color;
        switch (layer)
        {
            case "temp_min":
            case "temp_max":
            case "temperature":
            {
                // 纬度项只与 Z 有关：同一行复用包络，避免每像素重算 2304 个时刻
                if (rowZ != bz)
                {
                    rowRange = seasonProfile.Range(bz, layerY, 0f);
                    rowZ = bz;
                }
                float baseTemp = ClimateQuery.AverageTempAt(rg, bx, bz);
                float value = layer switch
                {
                    "temp_min" => baseTemp + rowRange.Min,
                    "temp_max" => baseTemp + rowRange.Max,
                    _ => baseTemp + rowRange.Mean,
                };
                color = TemperatureColor(value);
                break;
            }
            case "rainfall":
            {
                color = RainfallColor(ClimateQuery.RainfallAt(rg, bx, bz));
                break;
            }
            default:
            {
                // 与 Java Main 同款取数：floorDiv(bx,16)*4+1 = chunk 中心 quart；含河流检测（等价 BiomeSourceExtension.getBiomeExtension）
                int quartX = Mth.FloorDiv(bx, 16) * 4 + 1;
                int quartZ = Mth.FloorDiv(bz, 16) * 4 + 1;
                int biomeId = ViewerQuery.GetBiomeId(rg, quartX, quartZ);
                var biomeName = TfcViewer.World.Layer.TFCLayers.GetFromLayerId(biomeId);
                color = BiomeColor(biomeName, colors);
                break;
            }
        }
        image[iz * size + ix] = color; // 行序：iz=0 → bz=minZ（北），顶部=北，与 Java PPM 一致
    }
}

long t2 = Environment.TickCount64;
Console.WriteLine($"采样完成: {t2 - t1} ms");

// image[iz] 行：iz=0 → bz=minZ（北），直接输出即为 顶部=北（与 Java PPM 一致）
PngWriter.Write(outPath, size, size, (x, y) => image[y * size + x]);
Console.WriteLine($"输出: {Path.GetFullPath(outPath)}");

static long ParseSeed(string text)
    {
        if (long.TryParse(text, out var v)) return v;
        return JavaHashCode(text);
    }

    /// <summary>取 --key=value 形式的选项值</summary>
    static string? OptValue(string[] args, string key)
    {
        string prefix = "--" + key + "=";
        foreach (var a in args)
            if (a.StartsWith(prefix, StringComparison.Ordinal)) return a[prefix.Length..];
        return null;
    }

    static int OptInt(string[] args, string key, int fallback)
        => int.TryParse(OptValue(args, key), out var v) ? v : fallback;

    static long OptLong(string[] args, string key, long fallback)
        => long.TryParse(OptValue(args, key), out var v) ? v : fallback;

    /// <summary>未给存档时的日历 tick：--ticks 优先，其次 --day/--hour（按 1 月 = daysInMonth 天铺开），默认 0</summary>
    static long ResolveCalendarTicks(string[] args, int daysInMonth)
    {
        if (OptValue(args, "ticks") is { } ticksText && long.TryParse(ticksText, out var ticks)) return ticks;
        long day = OptLong(args, "day", -1);
        int hour = OptInt(args, "hour", 12);
        if (day < 0) return 0;
        return day * TfcCalendar.TicksInDay + hour * TfcCalendar.TicksInHour;
    }

    /// <summary>Java String.hashCode() 语义（与游戏 WorldOptions.parseSeed 一致）</summary>
    static int JavaHashCode(string s)
    {
        int h = 0;
        foreach (char c in s) h = unchecked(31 * h + c);
        return h;
    }

static bool IsNumeric(string s) => long.TryParse(s, out _);

static Dictionary<string, int[]> LoadBiomeColors()
{
    var map = new Dictionary<string, int[]>();
    var asm = Assembly.GetExecutingAssembly();
    using var stream = asm.GetManifestResourceStream("TfcViewer.biome_colors.json");
    if (stream == null) return map;
    using var reader = new StreamReader(stream);
    string txt = reader.ReadToEnd();
    var m = System.Text.RegularExpressions.Regex.Match(txt, "\"([\\w:]+)\":\\s*\\[\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)\\s*]");
    while (m.Success)
    {
        map[m.Groups[1].Value] = new[] { int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value) };
        m = m.NextMatch();
    }
    return map;
}

static int BiomeColor(string biomeId, Dictionary<string, int[]> colors)
{
    if (colors.TryGetValue(biomeId, out var c))
        return (c[0] << 16) | (c[1] << 8) | c[2];
    int hash = Math.Abs(biomeId.GetHashCode());
    return HslToRgb((hash % 1000) / 1000.0f, 0.6f, 0.6f);
}

static int TemperatureColor(float temp)
{
    // 年最低/年最高口径下实际跨度约 -30 ~ +35 ℃
    float t = Math.Clamp(temp, -30f, 35f);
    float f = (t + 30f) / 65f;
    return Gradient(f, new[] { new[] { 0.15f, 0.3f, 0.75f }, new[] { 0.25f, 0.6f, 0.45f }, new[] { 0.85f, 0.85f, 0.3f }, new[] { 0.95f, 0.75f, 0.2f } });
}

static int RainfallColor(float rain)
{
    float f = Math.Clamp(rain, 0f, 500f) / 500f;
    return Gradient(f, new[] { new[] { 0.9f, 0.75f, 0.3f }, new[] { 0.6f, 0.8f, 0.4f }, new[] { 0.3f, 0.55f, 0.6f }, new[] { 0.15f, 0.3f, 0.6f } });
}

static int Gradient(float f, float[][] stops)
{
    f = Math.Clamp(f, 0, 1) * (stops.Length - 1);
    int i = Math.Min((int)f, stops.Length - 2);
    float d = f - i;
    var a = stops[i];
    var b = stops[i + 1];
    return ((int)(255 * (a[0] + (b[0] - a[0]) * d)) << 16)
        | ((int)(255 * (a[1] + (b[1] - a[1]) * d)) << 8)
        | (int)(255 * (a[2] + (b[2] - a[2]) * d));
}

static int HslToRgb(float h, float s, float l)
{
    float r, g, b;
    if (s == 0) { r = g = b = l; }
    else
    {
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
        float p = 2 * l - q;
        r = Hue2Rgb(p, q, h + 1f / 3f);
        g = Hue2Rgb(p, q, h);
        b = Hue2Rgb(p, q, h - 1f / 3f);
    }
    return ((int)(r * 255) << 16) | ((int)(g * 255) << 8) | (int)(b * 255);
}

static float Hue2Rgb(float p, float q, float t)
{
    if (t < 0) t += 1;
    if (t > 1) t -= 1;
    if (t < 1f / 6f) return p + (q - p) * 6 * t;
    if (t < 1f / 2f) return q;
    if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
    return p;
}