using TfcViewer;
using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Layer;
using TfcViewer.World.Region;

namespace TfcViewerGui;

/// <summary>一次渲染的结果</summary>
public sealed class RenderResult
{
    public Bitmap Image = null!;
    public int[]? BiomeIds;   // w*h（仅 biome 层非空）
    public int W, H;
    public List<(int Id, string Name, Color Color)> Present = new();
    public Dictionary<int, Color> Palette = new();
}

/// <summary>视图渲染（引擎直查 + quart 缓存），并统计出现的群系供图例使用</summary>
public static class ViewRenderer
{
    public static RenderResult Render(RegionGenerator rg, int w, int h,
        double centerBX, double centerBZ, double bpp, string layer,
        Dictionary<string, int[]> colors)
    {
        var result = new RenderResult { W = w, H = h };
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var ids = layer == "biome" ? new int[w * h] : null;
        var present = new Dictionary<int, string>();
        var palette = new Dictionary<int, Color>();
        var quartCache = new Dictionary<long, int>();

        double minBX = centerBX - w * bpp / 2.0;
        double minBZ = centerBZ - h * bpp / 2.0;

        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        unsafe
        {
            byte* p0 = (byte*)data.Scan0;
            int stride = data.Stride;
            for (int y = 0; y < h; y++)
            {
                int bz = (int)Math.Floor(minBZ + (h - 1 - y) * bpp);
                byte* row = p0 + y * stride;
                for (int x = 0; x < w; x++)
                {
                    int bx = (int)Math.Floor(minBX + x * bpp);
                    int color;
                    if (layer == "biome")
                    {
                        int qx = Mth.FloorDiv(bx, 16) * 4 + 1;
                        int qz = Mth.FloorDiv(bz, 16) * 4 + 1;
                        long key = (long)qx & 0xFFFFFFFFL | ((long)qz << 32);
                        if (!quartCache.TryGetValue(key, out int id))
                        {
                            id = ViewerQuery.GetBiomeId(rg, qx, qz);
                            quartCache[key] = id;
                        }
                        ids![y * w + x] = id;
                        string name = TFCLayers.GetFromLayerId(id);
                        if (!present.ContainsKey(id)) present[id] = name;
                        if (colors.TryGetValue(name, out var c))
                        {
                            color = (c[0] << 16) | (c[1] << 8) | c[2];
                            if (!palette.ContainsKey(id)) palette[id] = Color.FromArgb(color | unchecked((int)0xFF000000));
                        }
                        else
                        {
                            color = FallbackColor(name);
                        }
                    }
                    else
                    {
                        var p = rg.GetOrCreateRegionPoint(Units.BlockToGrid(bx), Units.BlockToGrid(bz));
                        color = layer == "temperature" ? TemperatureColor(p.Temperature) : RainfallColor(p.Rainfall);
                    }
                    byte* px = row + x * 4;
                    px[0] = (byte)(color & 0xFF);
                    px[1] = (byte)((color >> 8) & 0xFF);
                    px[2] = (byte)((color >> 16) & 0xFF);
                    px[3] = 255;
                }
            }
        }
        bmp.UnlockBits(data);

        result.Image = bmp;
        result.BiomeIds = ids;
        foreach (var kv in present)
            result.Present.Add((kv.Key, kv.Value, palette.TryGetValue(kv.Key, out var c) ? c : Color.Gray));
        result.Palette = palette;
        return result;
    }

    private static int FallbackColor(string name)
    {
        int hash = Math.Abs(name.GetHashCode());
        float h = (hash % 1000) / 1000.0f;
        return HslToRgb(h, 0.6f, 0.6f);
    }

    /// <summary>温度：冷蓝(-30) → 绿(0) → 暖黄(+30)（与 CLI/Java 同款配色）</summary>
    public static int TemperatureColor(float temp)
    {
        float t = Math.Clamp(temp, -30f, 30f);
        float f = (t + 30f) / 60f;
        return Gradient(f, new[] { new[] { 0.15f, 0.3f, 0.75f }, new[] { 0.25f, 0.6f, 0.45f }, new[] { 0.85f, 0.85f, 0.3f }, new[] { 0.95f, 0.75f, 0.2f } });
    }

    /// <summary>降雨：干旱黄(0) → 浅绿(150) → 深蓝(500)</summary>
    public static int RainfallColor(float rain)
    {
        float f = Math.Clamp(rain, 0f, 500f) / 500f;
        return Gradient(f, new[] { new[] { 0.9f, 0.75f, 0.3f }, new[] { 0.6f, 0.8f, 0.4f }, new[] { 0.3f, 0.55f, 0.6f }, new[] { 0.15f, 0.3f, 0.6f } });
    }

    private static int Gradient(float f, float[][] stops)
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

    private static int HslToRgb(float h, float s, float l)
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

    private static float Hue2Rgb(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6 * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
        return p;
    }
}