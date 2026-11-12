using TfcViewer;
using TfcViewer.Climate;
using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Layer;
using TfcViewer.World.Region;

namespace TfcViewerGui;

/// <summary>一次渲染的结果</summary>
public sealed class RenderResult
{
    public Bitmap Image = null!;
    /// <summary>每像素分类 id（w*h），含义由筛选模式决定</summary>
    public int[]? ClassIds;
    public int W, H;
    /// <summary>分类 id → 该分类在画面里的像素数</summary>
    public Dictionary<int, int> Counts = new();
    /// <summary>群系 id → 颜色（群系模式图例用）</summary>
    public Dictionary<int, Color> BiomePalette = new();
}

/// <summary>
/// 视图渲染：筛选模式同时决定配色、分类矩阵与图例统计。
/// 温度/降水/气候都按 128 格气候网格「每格共享四角」计算（与 TFC ChunkData 插值等价），
/// 避免逐像素查 Region 造成的数量级放大。
/// </summary>
public static class ViewRenderer
{
    public static RenderResult Render(RegionGenerator rg, int w, int h,
        double centerBX, double centerBZ, double bpp,
        Dictionary<string, int[]> colors,
        ClimateProfile profile, int layerY, FilterMode filter,
        int rowOffset = 0, int totalHeight = -1)
    {
        int totalRows = totalHeight > 0 ? totalHeight : h;
        var result = new RenderResult { W = w, H = h };
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var classIds = new int[w * h];
        var quartCache = new Dictionary<long, int>();

        bool needBiome = filter == FilterMode.Biome;
        bool needTemp = filter is FilterMode.Temperature or FilterMode.Climate;
        bool needRain = filter is FilterMode.Rainfall or FilterMode.Climate;

        double minBX = centerBX - w * bpp / 2.0;
        double minBZ = centerBZ - totalRows * bpp / 2.0;

        // 128 格网格单元缓存（温度/降水共用四角）
        int cellX = int.MinValue, cellZ = int.MinValue;
        float t00 = 0, t01 = 0, t10 = 0, t11 = 0;
        float r00 = 0, r01 = 0, r10 = 0, r11 = 0;

        var rect = new Rectangle(0, 0, w, h);
        int rowZ = int.MinValue;
        TemperatureRange rowRange = default;
        var data = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        unsafe
        {
            byte* p0 = (byte*)data.Scan0;
            int stride = data.Stride;
            for (int y = 0; y < h; y++)
            {
                // 行序：y=0 → 北方（较大 Z），与历史版本一致
                int bz = (int)Math.Floor(minBZ + (totalRows - 1 - (rowOffset + y)) * bpp);
                byte* row = p0 + y * stride;

                if (needTemp && rowZ != bz)
                {
                    rowRange = profile.Range(bz, layerY, 0f);   // 纬度项只与 Z 有关
                    rowZ = bz;
                }

                for (int x = 0; x < w; x++)
                {
                    int bx = (int)Math.Floor(minBX + x * bpp);

                    float avgTemp = 0f;
                    float avgRain = 0f;
                    int biomeId = 0;

                    if (needTemp || needRain)
                    {
                        int gx = bx >> 7;      // Units.BlockToGrid：1 格 = 128 块
                        int gz = bz >> 7;
                        if (gx != cellX || gz != cellZ)
                        {
                            var p00 = rg.GetOrCreateRegionPoint(gx, gz);
                            var p01 = rg.GetOrCreateRegionPoint(gx, gz + 1);
                            var p10 = rg.GetOrCreateRegionPoint(gx + 1, gz);
                            var p11 = rg.GetOrCreateRegionPoint(gx + 1, gz + 1);
                            t00 = p00.Temperature; t01 = p01.Temperature; t10 = p10.Temperature; t11 = p11.Temperature;
                            r00 = p00.Rainfall; r01 = p01.Rainfall; r10 = p10.Rainfall; r11 = p11.Rainfall;
                            cellX = gx; cellZ = gz;
                        }

                        double deltaX = bx / 128.0 - gx;
                        double deltaZ = bz / 128.0 - gz;
                        const double dG = 16.0 / 128.0;
                        float fx = (bx & 15) / 16f;
                        float fz = (bz & 15) / 16f;

                        if (needTemp)
                        {
                            float s00 = (float)ClimateModel.Lerp4(t00, t01, t10, t11, deltaX, deltaZ);
                            float s01 = (float)ClimateModel.Lerp4(t00, t01, t10, t11, deltaX, deltaZ + dG);
                            float s10 = (float)ClimateModel.Lerp4(t00, t01, t10, t11, deltaX + dG, deltaZ);
                            float s11 = (float)ClimateModel.Lerp4(t00, t01, t10, t11, deltaX + dG, deltaZ + dG);
                            avgTemp = (float)ClimateModel.Lerp4(s00, s01, s10, s11, fx, fz) + rowRange.Mean;
                        }
                        if (needRain)
                        {
                            float s00 = (float)ClimateModel.Lerp4(r00, r01, r10, r11, deltaX, deltaZ);
                            float s01 = (float)ClimateModel.Lerp4(r00, r01, r10, r11, deltaX, deltaZ + dG);
                            float s10 = (float)ClimateModel.Lerp4(r00, r01, r10, r11, deltaX + dG, deltaZ);
                            float s11 = (float)ClimateModel.Lerp4(r00, r01, r10, r11, deltaX + dG, deltaZ + dG);
                            avgRain = (float)ClimateModel.Lerp4(s00, s01, s10, s11, fx, fz);
                        }
                    }

                    if (needBiome)
                    {
                        int qx = Mth.FloorDiv(bx, 16) * 4 + 1;
                        int qz = Mth.FloorDiv(bz, 16) * 4 + 1;
                        long key = (long)qx & 0xFFFFFFFFL | ((long)qz << 32);
                        if (!quartCache.TryGetValue(key, out biomeId))
                        {
                            biomeId = ViewerQuery.GetBiomeId(rg, qx, qz);
                            quartCache[key] = biomeId;
                        }
                    }

                    int classId;
                    int color;
                    switch (filter)
                    {
                        case FilterMode.Temperature:
                            classId = Filters.TempBin(avgTemp);
                            color = Filters.ColorOf(filter, classId).ToArgb() & 0xFFFFFF;
                            break;
                        case FilterMode.Rainfall:
                            classId = Filters.RainBin(avgRain);
                            color = Filters.ColorOf(filter, classId).ToArgb() & 0xFFFFFF;
                            break;
                        case FilterMode.Climate:
                            classId = KoppenClimate.Classify(avgTemp, avgRain);
                            color = Filters.ColorOf(filter, classId).ToArgb() & 0xFFFFFF;
                            break;
                        default:
                            classId = biomeId;
                            string biomeName = TFCLayers.GetFromLayerId(biomeId);
                            color = BiomeColor(biomeId, biomeName, colors, result.BiomePalette);
                            break;
                    }

                    classIds[y * w + x] = classId;
                    result.Counts.TryGetValue(classId, out int cnt);
                    result.Counts[classId] = cnt + 1;

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
        result.ClassIds = classIds;
        return result;
    }

    /// <summary>
    /// 并行渲染：按行分带，每带一个独立的 RegionGenerator（引擎内缓存非线程安全，不能共享）。
    /// 构造单例仅 ~15 ms，所以并行收益接近线性。
    /// </summary>
    public static RenderResult RenderParallel(Settings settings, long worldSeed, int w, int h,
        double centerBX, double centerBZ, double bpp,
        Dictionary<string, int[]> colors,
        ClimateProfile profile, int layerY, FilterMode filter, int threads)
    {
        int bands = Math.Clamp(threads, 1, Math.Max(1, h));
        int bandH = (h + bands - 1) / bands;
        var parts = new RenderResult?[bands];

        if (bands == 1)
            return Render(new RegionGenerator(settings, Seed.Of(worldSeed)), w, h, centerBX, centerBZ, bpp, colors, profile, layerY, filter);

        Parallel.For(0, bands, new ParallelOptions { MaxDegreeOfParallelism = bands }, i =>
        {
            int offset = i * bandH;
            int rows = Math.Min(bandH, h - offset);
            if (rows <= 0) return;
            var rg = new RegionGenerator(settings, Seed.Of(worldSeed));
            parts[i] = Render(rg, w, rows, centerBX, centerBZ, bpp, colors, profile, layerY, filter, offset, h);
        });

        var merged = new RenderResult { W = w, H = h };
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var classIds = new int[w * h];
        using (var g = Graphics.FromImage(bmp))
        {
            for (int i = 0; i < bands; i++)
            {
                var part = parts[i];
                if (part == null) continue;
                int offset = i * bandH;
                int rows = Math.Min(bandH, h - offset);
                g.DrawImageUnscaled(part.Image, 0, offset);
                Array.Copy(part.ClassIds!, 0, classIds, offset * w, rows * w);
                foreach (var kv in part.Counts)
                {
                    merged.Counts.TryGetValue(kv.Key, out int c);
                    merged.Counts[kv.Key] = c + kv.Value;
                }
                foreach (var kv in part.BiomePalette) merged.BiomePalette[kv.Key] = kv.Value;
                part.Image.Dispose();
            }
        }

        merged.Image = bmp;
        merged.ClassIds = classIds;
        return merged;
    }

    private static int BiomeColor(int biomeId, string name, Dictionary<string, int[]> colors, Dictionary<int, Color> palette)
    {
        if (colors.TryGetValue(name, out var c))
        {
            int col = (c[0] << 16) | (c[1] << 8) | c[2];
            if (!palette.ContainsKey(biomeId)) palette[biomeId] = Color.FromArgb(col | unchecked((int)0xFF000000));
            return col;
        }
        return FallbackColor(name);
    }

    private static int FallbackColor(string name)
    {
        int hash = Math.Abs(name.GetHashCode());
        float h = (hash % 1000) / 1000.0f;
        return HslToRgb(h, 0.6f, 0.6f);
    }

    /// <summary>温度：冷蓝(-30) → 绿(0) → 暖黄(+35)</summary>
    public static int TemperatureColor(float temp)
    {
        float t = Math.Clamp(temp, -30f, 35f);
        float f = (t + 30f) / 65f;
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
