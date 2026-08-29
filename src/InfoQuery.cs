using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Layer;
using TfcViewer.World.Region;

namespace TfcViewer;

/// <summary>点击查询返回的详细信息</summary>
public sealed record BiomeInfo(
    int BlockX, int BlockZ, int ChunkX, int ChunkZ,
    string BiomeId, float Temperature, float Rainfall,
    string Climate, int ConfidencePercent);

/// <summary>
/// 查看器查询 API：层链+河流群系查询、点击信息（气候/置信度）。
/// 供 CLI 与 WinForms GUI 共用。
/// </summary>
public static class ViewerQuery
{
    /// <summary>等价 Java BiomeSourceExtension.getBiomeExtension：层链输出 + 河流边检测覆盖为 RIVER</summary>
    public static int GetBiomeId(RegionGenerator rg, int quartX, int quartZ)
    {
        int biomeId = rg.BiomeLayerArea.Get(quartX, quartZ);
        if (TFCLayers.HasRivers(biomeId))
        {
            // getPartition(quartX << 2, quartZ << 2)：quart→block 坐标（同位），再 block→grid
            var partitionPoint = rg.GetOrCreatePartitionPoint(
                Units.BlockToGrid(quartX << 2), Units.BlockToGrid(quartZ << 2));
            double exactGridX = Units.QuartToGridExact(quartX);
            double exactGridZ = Units.QuartToGridExact(quartZ);
            foreach (var edge in partitionPoint.Rivers)
            {
                if (edge.Fractal.Intersect(exactGridX, exactGridZ, 0.08f))
                    return TFCLayers.RIVER;
            }
        }
        return biomeId;
    }

    /// <summary>点击查询：群系（含河流）、温度、降水、气候类型、置信度</summary>
    /// <remarks>
    /// 气候类型 = TFC ChooseBiomes 真实阈值风格；
    /// 置信度 = 该 quart 的 3x3 邻域（smooth 层输出）与中心同群系的比例（启发式，0-100%）。
    /// </remarks>
    public static BiomeInfo GetInfoAt(RegionGenerator rg, int blockX, int blockZ)
    {
        int quartX = blockX >> 2;
        int quartZ = blockZ >> 2;
        int biomeId = GetBiomeId(rg, quartX, quartZ);
        string biome = TFCLayers.GetFromLayerId(biomeId);

        var p = rg.GetOrCreateRegionPoint(Units.BlockToGrid(blockX), Units.BlockToGrid(blockZ));
        string climate = ClassifyClimate(p.Temperature, p.Rainfall);

        int center = rg.BiomeLayerArea.Get(quartX, quartZ);
        int same = 0;
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                if (rg.BiomeLayerArea.Get(quartX + dx, quartZ + dz) == center) same++;
            }
        }
        int confidence = same * 100 / 25;

        return new BiomeInfo(blockX, blockZ, blockX >> 4, blockZ >> 4,
            biome, p.Temperature, p.Rainfall, climate, confidence);
    }

    /// <summary>气候分类：TFC ChooseBiomes 的阈值风格（冰盖线 -17+0.006r / 冰缘 +2.5 / 古冰缘 +6 / 沙漠 60 / 半干旱 155 / 喀斯特 250-425）</summary>
    public static string ClassifyClimate(float temperature, float rainfall)
    {
        float iceLine = -17f + 0.006f * rainfall;
        if (rainfall >= 0f && temperature < iceLine) return "冰盖";
        if (rainfall >= 0f && temperature < iceLine + 2.5f) return "冰缘";
        if (rainfall >= 0f && temperature < iceLine + 6f) return "古冰缘";
        if (rainfall < 60f) return "沙漠";
        if (rainfall < 155f) return "半干旱";
        if (rainfall > 425f && rainfall + 10f * temperature > 500f) return "湿热";
        if (rainfall > 375f) return "湿润";
        if (rainfall > 250f) return "半湿润";
        return "温带";
    }
}