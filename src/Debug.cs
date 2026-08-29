using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Layer;
using TfcViewer.World.Region;

namespace TfcViewer;

/// <summary>诊断：逐层链输出对比 Java LayerTest</summary>
public static class DebugMain
{
    public static void Run()
    {
        var settings = Defaults.DefaultSettings();
        var s2 = Seed.Of(-1162099057L);
        // 模拟 RegionGenerator 构造序列（同 Java LayerTest）
        for (int i = 0; i < 5; i++) s2.Next();
        var bioF = TfcViewer.World.Layer.TFCLayers.CreateUniformLayer(s2, 2);
        var rockF = TfcViewer.World.Layer.TFCLayers.CreateUniformLayer(s2, 3);
        for (int i = 0; i < 5; i++) s2.Next();
        Console.WriteLine($"RG序列 rockArea(-10,-6) = {rockF().Get(-10, -6)}");
        Console.WriteLine($"RG序列 biomeArea(-10,-6) = {bioF().Get(-10, -6)}");

        var s = Seed.Of(-1162099057L);
        var rg = new RegionGenerator(settings, s);
        // 打印几个 region 的 data.Length（对比 Java 15251）
        var p0 = rg.GetOrCreateRegionPoint(-32, -32);
        var reg = rg.GetOrCreateRegionPoint(-9, -32);
        Console.WriteLine($"(-32,-32) region dataLength={rg.PeekDataLength(p0)}");
        Console.WriteLine($"(-9,-32) region dataLength={rg.PeekDataLength(reg)}");
        int[][] pts = {
            new[]{-1152,-4096}, new[]{-1024,-4096}, new[]{-1280,-768}, new[]{2240,-3968}, new[]{3296,-4080},
            new[]{352,-4096}, new[]{-256,-512}, new[]{1024,1024}, new[]{0,0}, new[]{-4096,-4096},
        };
        foreach (var p in pts)
        {
            int gx = p[0] >> 7, gz = p[1] >> 7;
            var cell = rg.CellNoise.GetCell(gx, gz);
            var bp = rg.GetOrCreateRegionPoint(gx, gz);
            Console.WriteLine($"({p[0]},{p[1]}) grid({gx},{gz}) bits={BitConverter.SingleToInt32Bits((float)cell.noise)} f1={cell.f1:F6} continent={rg.ContinentNoise.Noise(gx, gz):F4} land={bp.Land} oceandepth={bp.OceanDepth} altitude={bp.BiomeAltitude} temp={bp.Temperature:F2} rain={bp.Rainfall:F1} biome={TfcViewer.World.Layer.TFCLayers.GetFromLayerId(bp.Biome)} rock={bp.Rock} karst={bp.IsSurfaceRockKarst} rockArea={rg.RockArea.Get(gx, gz)} biomeArea={rg.BiomeArea.Get(gx, gz)}");
        }
    }
}