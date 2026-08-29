using System.Reflection;
using TfcViewer;
using TfcViewer.Core;
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

if (args.Length >= 4 && args[0] == "--info")
{
    long iseed = ParseSeed(args[1]);
    int ibx = int.Parse(args[2]);
    int ibz = int.Parse(args[3]);
    var irg = new RegionGenerator(Defaults.DefaultSettings(), Seed.Of(iseed));
    var info = ViewerQuery.GetInfoAt(irg, ibx, ibz);
    Console.WriteLine($"block=({info.BlockX},{info.BlockZ}) chunk=({info.ChunkX},{info.ChunkZ})");
    Console.WriteLine($"biome={info.BiomeId} temp={info.Temperature:F1} rain={info.Rainfall:F1}");
    Console.WriteLine($"climate={ViewerQuery.ClimateLocalized(info.Climate, false)} confidence={info.ConfidencePercent}%");
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
    Console.WriteLine("  图层: biome(默认) | temperature | rainfall");
    return;
}

string seedText = args[0];
int size = args.Length > 1 ? int.Parse(args[1]) : 512;
int bpp = args.Length > 2 ? int.Parse(args[2]) : 16;
string layer = args.Length > 3 ? args[3] : "biome";
string prefix = layer switch { "temperature" => "temp_", "rainfall" => "rain_", _ => "biome_" };
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

int blocks = size * 16;
int minX = -blocks / 2;
int minZ = -blocks / 2;

Console.WriteLine($"采样 {size}x{size} 像素（1px={bpp}块, 图层={layer}, 中心 0,0）...");
int[] image = new int[size * size];
for (int iz = 0; iz < size; iz++)
{
    for (int ix = 0; ix < size; ix++)
    {
        int bx = minX + ix * bpp;
        int bz = minZ + iz * bpp;
        int color;
        switch (layer)
        {
            case "temperature":
            {
                var p = rg.GetOrCreateRegionPoint(Units.BlockToGrid(bx), Units.BlockToGrid(bz));
                color = TemperatureColor(p.Temperature);
                break;
            }
            case "rainfall":
            {
                var p = rg.GetOrCreateRegionPoint(Units.BlockToGrid(bx), Units.BlockToGrid(bz));
                color = RainfallColor(p.Rainfall);
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
    float t = Math.Clamp(temp, -30f, 30f);
    float f = (t + 30f) / 60f;
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