using TfcViewer;
using TfcViewer.Climate;

namespace TfcViewerGui;

/// <summary>图例筛选模式（决定图例列什么、画布高亮按什么分类）</summary>
public enum FilterMode
{
    Biome,
    Temperature,
    Rainfall,
    Climate,
}

/// <summary>图例分类项（与群系共用同一套 id 空间；不同模式的 id 各自独立解释）</summary>
public readonly record struct FilterCategory(int Id, string NameZh, string NameEn, Color Color);

/// <summary>
/// 四种筛选模式的分档定义：温度按年均温、降水按插值降水、气候按 TFC 真实阈值分类。
/// id 只在同一模式内比较，切换模式时选择会清空。
/// </summary>
public static class Filters
{
    // ---- 温度：6 档（与图层温度口径一致，单位 ℃）----
    private static readonly (float Min, float Max)[] TempBins =
    {
        (float.NegativeInfinity, -10f),
        (-10f, 0f),
        (0f, 10f),
        (10f, 20f),
        (20f, 30f),
        (30f, float.PositiveInfinity),
    };

    private static readonly string[] TempZh = { "严寒 ≤ -10", "寒冷 -10~0", "温凉 0~10", "温和 10~20", "温暖 20~30", "炎热 ≥ 30" };
    private static readonly string[] TempEn = { "Freezing ≤ -10", "Cold -10~0", "Cool 0~10", "Mild 10~20", "Warm 20~30", "Hot ≥ 30" };

    // ---- 降水：6 档（阈值就是 TFC 气候分类用的干湿线，单位 mm）----
    private static readonly (float Min, float Max)[] RainBins =
    {
        (float.NegativeInfinity, 60f),
        (60f, 155f),
        (155f, 250f),
        (250f, 375f),
        (375f, 425f),
        (425f, float.PositiveInfinity),
    };

    private static readonly string[] RainZh = { "干旱 < 60", "半干旱 60~155", "中湿 155~250", "半湿润 250~375", "湿润 375~425", "极湿 ≥ 425" };
    private static readonly string[] RainEn = { "Arid < 60", "Semi-arid 60~155", "Sub-humid 155~250", "Semi-wet 250~375", "Wet 375~425", "Soaked ≥ 425" };

    // ---- 气候：12 类（TFC 原版 KoppenClimateClassification，顺序与枚举一致）----
    private static readonly Color[] ClimateColors =
    {
        Color.FromArgb(232, 244, 250), Color.FromArgb(201, 217, 196), Color.FromArgb(169, 192, 214),
        Color.FromArgb(217, 207, 163), Color.FromArgb(230, 192, 105), Color.FromArgb(156, 203, 111),
        Color.FromArgb(199, 212, 106), Color.FromArgb(111, 176, 106), Color.FromArgb(78, 158, 138),
        Color.FromArgb(127, 169, 184), Color.FromArgb(224, 165, 79), Color.FromArgb(46, 125, 50),
    };

    private static readonly Color[] TempColors =
    {
        Color.FromArgb(59, 111, 212), Color.FromArgb(79, 163, 199), Color.FromArgb(95, 191, 143),
        Color.FromArgb(168, 201, 74), Color.FromArgb(224, 168, 60), Color.FromArgb(214, 84, 59),
    };

    private static readonly Color[] RainColors =
    {
        Color.FromArgb(217, 190, 79), Color.FromArgb(199, 190, 106), Color.FromArgb(159, 191, 127),
        Color.FromArgb(110, 170, 143), Color.FromArgb(73, 143, 168), Color.FromArgb(43, 106, 160),
    };

    public static int TempBinCount => TempBins.Length;
    public static int RainBinCount => RainBins.Length;
    public static int ClimateClassCount => KoppenClimate.Count;

    public static int TempBin(float temperature)
    {
        for (int i = 0; i < TempBins.Length; i++)
            if (temperature < TempBins[i].Max) return i;
        return TempBins.Length - 1;
    }

    public static int RainBin(float rainfall)
    {
        for (int i = 0; i < RainBins.Length; i++)
            if (rainfall < RainBins[i].Max) return i;
        return RainBins.Length - 1;
    }

    /// <summary>某模式下的分类项总数</summary>
    public static int CategoryCount(FilterMode mode) => mode switch
    {
        FilterMode.Temperature => TempBinCount,
        FilterMode.Rainfall => RainBinCount,
        FilterMode.Climate => ClimateClassCount,
        _ => int.MaxValue,
    };

    /// <summary>分类项的名称（温度/降水带数值范围，气候用 TFC 分类名）</summary>
    public static string Name(FilterMode mode, int id, bool english) => mode switch
    {
        FilterMode.Temperature => english ? TempEn[id] : TempZh[id],
        FilterMode.Rainfall => english ? RainEn[id] : RainZh[id],
        FilterMode.Climate => (english ? KoppenClimate.Name(id, true) : KoppenClimate.Name(id, false)) + $"({KoppenClimate.Key(id)})",
        _ => id.ToString(),
    };

    /// <summary>分类项的固定颜色（温度/降水/气候；群系模式用渲染出的群系色）</summary>
    public static Color ColorOf(FilterMode mode, int id) => mode switch
    {
        FilterMode.Temperature => TempColors[id],
        FilterMode.Rainfall => RainColors[id],
        FilterMode.Climate => ClimateColors[id],
        _ => Color.Gray,
    };

    /// <summary>图例标题</summary>
    public static string Title(FilterMode mode, bool english) => mode switch
    {
        FilterMode.Temperature => english ? "Legend — temperature bins (click to multi-select)" : "图例 · 温度分档（点击多选）",
        FilterMode.Rainfall => english ? "Legend — rainfall bins (click to multi-select)" : "图例 · 降水分档（点击多选）",
        FilterMode.Climate => english ? "Legend — climate class (click to multi-select)" : "图例 · 气候分类（点击多选）",
        _ => english ? "Legend — biomes (click to multi-select)" : "图例 · 群系（点击多选）",
    };

    /// <summary>模式切换按钮文字</summary>
    public static string ModeName(FilterMode mode, bool english) => mode switch
    {
        FilterMode.Temperature => english ? "Temp" : "温度",
        FilterMode.Rainfall => english ? "Rain" : "降水",
        FilterMode.Climate => english ? "Climate" : "气候",
        _ => english ? "Biome" : "群系",
    };
}
