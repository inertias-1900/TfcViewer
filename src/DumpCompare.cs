using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Layer;
using TfcViewer.World.Region;

namespace TfcViewer;

/// <summary>诊断：DumpCompare（与 Java 版同格式 43 列 CSV）</summary>
public static class DumpCompare
{
    public static void Run(string[] args)
    {
        long seed = long.Parse(args[1]);
        string inCsv = args[2];
        string outCsv = args[3];

        var settings = Defaults.DefaultSettings();
        var s = Seed.Of(seed);
        var rg = new RegionGenerator(settings, s);

        var rows = new List<string>();
        // 层链分段（与渲染同款）
        var s2 = Seed.Of(seed);
        var rl = new RegionLayer(rg).Apply(s2.Next());
        var m0 = RegionBiomeLayer.Apply(rl);
        var m1 = RegionEdgeBiomeLayer.Apply(s2.Next(), m0);
        var m2 = ZoomLayer.ApplyNormal(s2.Next(), m1);
        var m3 = ShoreAndRiverLayer.Apply(s2.Next(), m2);
        var m4 = MoreShoresLayer.Apply(s2.Next(), m3);
        var m5 = IceSheetEdgeLayer.Apply(s2.Next(), m4);
        var m6 = ZoomLayer.ApplyNormal(s2.Next(), m5);
        var m7 = ZoomLayer.ApplyNormal(s2.Next(), m6);
        var m8 = ZoomLayer.ApplyNormal(s2.Next(), m7);
        var m9 = ZoomLayer.ApplyNormal(s2.Next(), m8);
        var m10 = SmoothLayer.Apply(s2.Next(), m9);

        foreach (var line in File.ReadLines(inCsv))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("blockX")) continue;
            var parts = line.Split(',');
            int bx = int.Parse(parts[0]);
            int bz = int.Parse(parts[1]);
            rows.Add(DumpOne(rg, bx, bz, m10, m9, m8, m7, m6, m5, m4, m3, m2));
        }
        File.WriteAllLines(outCsv, rows);
        Console.WriteLine($"written {outCsv} ({rows.Count} rows)");
    }

    private static string DumpOne(RegionGenerator rg, int bx, int bz, Area.Factory smooth, Area.Factory z5, Area.Factory z4, Area.Factory z3, Area.Factory z2, Area.Factory ice, Area.Factory more, Area.Factory shore, Area.Factory zoom1)
    {
        int gx = Units.BlockToGrid(bx);
        int gz = Units.BlockToGrid(bz);
        int quartX = bx >> 2;
        int quartZ = bz >> 2;

        var p = rg.GetOrCreateRegionPoint(gx, gz);
        var cell = rg.CellNoise.GetCell(gx, gz);
        float noiseF = (float)(cell.noise);
        var sb = new System.Text.StringBuilder();
        sb.Append(bx).Append(',').Append(bz).Append(',');
        sb.Append(gx).Append(',').Append(gz).Append(',');
        sb.Append(quartX).Append(',').Append(quartZ).Append(',');
        sb.Append(p.Biome).Append(',');
        sb.Append(TfcViewer.World.Layer.TFCLayers.GetFromLayerId(smooth().Get(quartX, quartZ))).Append(',');
        sb.Append(TfcViewer.World.Layer.TFCLayers.GetFromLayerId(z5().Get(quartX, quartZ))).Append(',');
        sb.Append(z4().Get(quartX, quartZ)).Append(',');
        sb.Append(z3().Get(quartX, quartZ)).Append(',');
        sb.Append(z2().Get(quartX, quartZ)).Append(',');
        sb.Append(ice().Get(quartX, quartZ)).Append(',');
        sb.Append(more().Get(quartX, quartZ)).Append(',');
        sb.Append(shore().Get(quartX, quartZ)).Append(',');
        sb.Append(zoom1().Get(quartX, quartZ)).Append(',');
        sb.Append(p.BaseLandHeight).Append(',');
        sb.Append(p.BiomeAltitude).Append(',');
        sb.Append(p.OceanDepth).Append(',');
        sb.Append(p.DistanceToOcean).Append(',');
        sb.Append(p.DistanceToDeepOcean).Append(',');
        sb.Append(p.DistanceToEdge).Append(',');
        sb.Append(p.DistanceToWestCoast).Append(',');
        sb.Append(p.DistanceToLand).Append(',');
        sb.Append(p.Temperature.ToString("R")).Append(',');
        sb.Append(p.Rainfall.ToString("R")).Append(',');
        sb.Append(p.RainfallVariance.ToString("R")).Append(',');
        sb.Append(p.Divergence.ToString("R")).Append(',');
        sb.Append(p.Rock).Append(',');
        sb.Append(p.HotSpotAge).Append(',');
        sb.Append(p.IsSurfaceRockKarst ? "true" : "false").Append(',');
        sb.Append(p.Land ? "true" : "false").Append(',');
        sb.Append(p.Island ? "true" : "false").Append(',');
        sb.Append(p.Shore ? "true" : "false").Append(',');
        sb.Append(p.River ? "true" : "false").Append(',');
        sb.Append(p.Lake ? "true" : "false").Append(',');
        sb.Append(p.Mountain ? "true" : "false").Append(',');
        sb.Append(p.CoastalMountain ? "true" : "false").Append(',');
        sb.Append(p.Volcanic ? "true" : "false").Append(',');
        sb.Append(p.BarrierIsland ? "true" : "false").Append(',');
        sb.Append(cell.x.ToString("R")).Append(',');
        sb.Append(cell.y.ToString("R")).Append(',');
        sb.Append(cell.cx).Append(',');
        sb.Append(cell.cy).Append(',');
        sb.Append(cell.f1.ToString("R")).Append(',');
        sb.Append(cell.f2.ToString("R")).Append(',');
        sb.Append(BitConverter.SingleToInt32Bits(noiseF)).Append(',');
        sb.Append(noiseF.ToString("R")).Append(',');
        sb.Append(cell.angle.ToString("R")).Append(',');
        sb.Append(rg.ContinentNoise.Noise(gx, gz).ToString("R")).Append(',');
        sb.Append(rg.TemperatureNoise.Noise(gx, gz).ToString("R")).Append(',');
        sb.Append(rg.RainfallNoise.Noise(gx, gz).ToString("R")).Append(',');
        sb.Append(p.Divergence.ToString("R")).Append(',');
        sb.Append(rg.BiomeArea.Get(gx, gz)); // DEBUG: areaSeed 探测
        return sb.ToString();
    }
}